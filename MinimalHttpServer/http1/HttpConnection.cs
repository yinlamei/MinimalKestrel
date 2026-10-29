// 简化版 ConnectionContext v0.5 —— 只负责 Protocol，业务逻辑全在 Pipeline
// 在 Kestrel 里，对应 Http1Connection，ProcessRequestAsync 调用 IHttpApplication.ProcessRequestAsync

public class HttpConnection : IAsyncDisposable
{
    private readonly ITransportConnection _transport;
    private readonly IHttpApplication _application;
    private readonly TimeSpan _idleTimeout;
    private readonly int _maxRequests;
    private readonly Http1Parser _parser = new(); // 🆕 状态机解析器，Keep-Alive 循环里 Reset 复用
    private int _requestCount;
    private bool _disposed;

    public DateTimeOffset LastActivity { get; private set; }
    public int RequestCount => _requestCount;
    public string? RemoteAddress => _transport.RemoteAddress;

    // 构造函数注入 Pipeline — Connection 不知道它在跟谁说话
    public HttpConnection(ITransportConnection transport,
        IHttpApplication application,
        TimeSpan? idleTimeout = null,
        int maxRequests = 1000)
    {
        _transport = transport;
        _application = application;
        _idleTimeout = idleTimeout ?? TimeSpan.FromSeconds(30);
        _maxRequests = maxRequests;
        LastActivity = DateTimeOffset.UtcNow;
    }

    // ===== 主入口：Keep-Alive 循环 =====
    public async Task StartAsync(CancellationToken globalCt = default)
    {
        while (_requestCount < _maxRequests)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(globalCt, _transport.Closed);
            timeoutCts.CancelAfter(_idleTimeout);

            _parser.Reset(); // 🆕 状态机归零 —— 每轮 Keep-Alive 循环从干净状态开始

            HttpRequest request;
            try
            {
                var body = await ReadFullRequestAsync(timeoutCts.Token);
                if (!_parser.IsComplete) return; // 用状态机完成与否来判断，不用 Body 长度

                // 从状态机解析器直接拿结果 — 不再调 Parse
                request = new HttpRequest
                {
                    Method = _parser.Method,
                    Path = _parser.Path,
                    Version = _parser.Version,
                    Headers = _parser.Headers,
                    Body = body,
                };
            }
            catch (OperationCanceledException)
            {
                if (timeoutCts.IsCancellationRequested && !globalCt.IsCancellationRequested)
                    Console.WriteLine($"[{NowStr()}] 连接空闲超时（{_requestCount} 个请求）");
                else
                    Console.WriteLine($"[{NowStr()}] 连接被取消");
                return;
            }
            catch (FormatException ex)
            {
                Console.Error.WriteLine($"[{NowStr()}] 请求格式错误: {ex.Message}");
                await WriteErrorResponseAsync(400, "Bad Request");
                return;
            }

            LastActivity = DateTimeOffset.UtcNow;

            // ===== 🆕 填 Features → 调 Pipeline =====
            var response = await ProcessRequestWithPipelineAsync(request, globalCt);

            _requestCount++;

            // ===== Keep-Alive 判定 =====
            var connectionHeader = request.Headers.TryGetValue("Connection", out var conn) ? conn : "";
            var isKeepAlive = !connectionHeader.Equals("close", StringComparison.OrdinalIgnoreCase);

            response.Headers["Connection"] = isKeepAlive ? "keep-alive" : "close";
            response.Headers["Keep-Alive"] = $"timeout={(int)_idleTimeout.TotalSeconds}, max={_maxRequests - _requestCount}";

            await WriteResponseAsync(response, globalCt);

            if (!isKeepAlive)
            {
                Console.WriteLine($"[{NowStr()}] 客户端请求关闭连接");
                break;
            }
        }

        if (_requestCount >= _maxRequests)
            Console.WriteLine($"[{NowStr()}] 达到最大请求数 {_maxRequests}");
    }

    // ===== 核心：Protocol 层 → Features → Pipeline → 取回 Response =====
    private async Task<HttpResponse> ProcessRequestWithPipelineAsync(HttpRequest request, CancellationToken ct)
    {
        // ① 建 FeatureCollection
        var features = new FeatureCollection();

        // ② Protocol 层填 Features —— Connection 层唯一知道的事
        features.Set<IHttpRequestFeature>(new HttpRequestFeature(request));
        features.Set<IHttpResponseFeature>(new HttpResponseFeature());
        features.Set<IHttpConnectionFeature>(new HttpConnectionFeature(_transport));

        // ③ 交给 Pipeline —— Connection 不知道 Pipeline 怎么处理
        await _application.ProcessRequestAsync(features, ct);

        // ④ 从 Features 取回 Pipeline 填写的 Response
        var responseFeature = features.Get<IHttpResponseFeature>()!;

        // ⑤ 转回我们的 HttpResponse POCO（为了序列化）
        var response = new HttpResponse
        {
            StatusCode = responseFeature.StatusCode,
            StatusText = responseFeature.StatusText,
            Body = responseFeature.Body
        };
        foreach (var kv in responseFeature.Headers)
            response.Headers[kv.Key] = kv.Value;

        return response;
    }

    // ===== 内部方法 =====

    // 🆕 状态机增量解析 — 每次 Read 完喂给 parser 新增的字节
    private async Task<byte[]> ReadFullRequestAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        int total = 0;
        bool headerComplete = false;

        // 第一阶段：读头部（状态机增量解析 — 每次只喂新字节）
        while (total < buffer.Length)
        {
            var prevTotal = total;
            var n = await _transport.ReadAsync(buffer.AsMemory()[total..], ct);
            if (n == 0) return Array.Empty<byte>(); // 对端断连
            total += n;

            // 🆕 只喂新增的字节 [prevTotal..total] —— 状态机内部维护自己的状态
            headerComplete = _parser.Parse(buffer.AsSpan()[prevTotal..total]);
            if (headerComplete) break;
        }

        if (total == 0 || !headerComplete)
            return Array.Empty<byte>();

        // 第二阶段：如果有 Content-Length，继续读 Body
        long contentLength = HttpParser.GetContentLength(_parser.Headers);
        int headerEnd = _parser.HeaderEnd; // 🆕 从状态机取绝对位置
        int bodyRead = total - headerEnd;
        while (bodyRead < contentLength && total < buffer.Length)
        {
            var n = await _transport.ReadAsync(buffer.AsMemory()[total..], ct);
            if (n == 0) break;
            total += n;
            bodyRead = total - headerEnd;
        }

        // 拷贝出来
        byte[] body;
        if (contentLength > 0 && bodyRead > 0)
        {
            body = new byte[bodyRead];
            Buffer.BlockCopy(buffer, headerEnd, body, 0, bodyRead);
        }
        else
        {
            body = Array.Empty<byte>();
        }

        return body;
    }

    private async Task WriteResponseAsync(HttpResponse response, CancellationToken ct)
    {
        var bytes = HttpParser.SerializeResponse(response);
        await _transport.WriteAsync(bytes, ct);
        LastActivity = DateTimeOffset.UtcNow;
    }

    private async Task WriteErrorResponseAsync(int code, string text)
    {
        var response = new HttpResponse { StatusCode = code, StatusText = text };
        response.SetText($"{code} {text}");
        await WriteResponseAsync(response, default);
    }

    private static string NowStr() => DateTime.Now.ToString("HH:mm:ss");

    // ===== IAsyncDisposable =====
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _transport.DisposeAsync();
        Console.WriteLine($"[{NowStr()}] [{RemoteAddress}] 连接关闭（共处理 {_requestCount} 个请求）");
    }
}
