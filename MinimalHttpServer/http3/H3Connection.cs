// HTTP/3 Connection — RFC 9114 (HTTP/3) + RFC 9000 (QUIC)
// Kestrel 源码：src/Servers/Kestrel/Http/Http3Connection.cs（~800 行）
//               src/Servers/Kestrel/Transport/QuicTransportConnection.cs
//
// 图一 第 3 层（Protocol）：只依赖 IMultiplexedTransport 这个抽象，
// 完全不认识 QuicListener / QuicConnection / QuicStream —— 那些在第 2 层。
// 每个 QUIC Stream 就是一个 HTTP/3 请求，因此这里"一条通道 = 一个请求"。

public class H3Connection : IAsyncDisposable
{
    private readonly IMultiplexedTransport _transport;
    private readonly IHttpApplication _app;
    private bool _disposed;

    public H3Connection(IMultiplexedTransport transport, IHttpApplication app)
    {
        _transport = transport;
        _app = app;
    }

    // ===== 主循环：从多路复用通道上不断取"D 一条请求通道" =====
    public async Task StartAsync(CancellationToken ct = default)
    {
        Console.WriteLine($"[H3] QUIC 连接已建立（{_transport.RemoteAddress}），进入 HTTP/3 模式");

        while (!ct.IsCancellationRequested)
        {
            ITransportConnection stream;
            try
            {
                stream = await _transport.AcceptStreamAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[H3] QUIC 连接结束: {ex.Message}");
                break;
            }

            Console.WriteLine("[H3] 收到请求 Stream");
            _ = RunRequestAsync(stream, ct);   // fire-and-forget，多 Stream 并发
        }
    }

    // ===== 单个请求：读帧 → 填 Features → Pipeline → 写帧 =====
    private async Task RunRequestAsync(ITransportConnection stream, CancellationToken ct)
    {
        await using (stream)
        {
            try
            {
                // ① 读 HEADERS 帧（含 :method / :path 伪头部）
                var headers = await ReadHeadersAsync(stream, ct);

                string method = "", path = "";
                foreach (var h in headers)
                {
                    switch (h.Key.ToLowerInvariant())
                    {
                        case ":method": method = h.Value; break;
                        case ":path":   path   = h.Value; break;
                    }
                }

                Console.WriteLine($"[H3] {method} {path}");

                // ② 填 Features —— 和 HTTP/1.1、HTTP/2 完全同一套契约
                var requestHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in headers)
                {
                    if (!kv.Key.StartsWith(':')) requestHeaders[kv.Key] = kv.Value;
                }

                var features = new FeatureCollection();
                features.Set<IHttpRequestFeature>(new HttpRequestFeature(new HttpRequest
                {
                    Method = method,
                    Path = path,
                    Version = "HTTP/3",
                    Headers = requestHeaders,
                }));
                features.Set<IHttpResponseFeature>(new HttpResponseFeature());
                features.Set<IHttpConnectionFeature>(new HttpConnectionFeature(stream));

                // ③ 交给 Pipeline
                await _app.ProcessRequestAsync(features, ct);

                var resp = features.Get<IHttpResponseFeature>()!;

                // ④ 响应编成 HTTP/3 帧写回 Stream
                await SendHeadersAsync(stream, resp, ct);
                await SendBodyAsync(stream, resp.Body, ct);

                // ⑤ 半关闭写入端（QUIC 的 End of Stream）
                await stream.CompleteWritesAsync(ct);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[H3] Stream 错误: {ex.Message}");
            }
        }
    }

    // ===== HEADERS 帧解析 =====

    private static async Task<Dictionary<string, string>> ReadHeadersAsync(ITransportConnection stream, CancellationToken ct)
    {
        var buffer = new byte[Http3Constants.MaxHeaderBuffer];
        int total = 0;

        // HTTP/3 的 HEADERS 帧用 QPACK 压缩；简化版沿用 HTTP/2 的 HPACK Literal 编码
        // Kestrel 用完整 QPACK（比 HPACK 多了动态表同步流）
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
            if (n == 0) break;
            total += n;

            // 帧可能被拆到多次 read，能解出完整 HEADERS 就返回
            foreach (var frame in Http3Parser.DecodeAllFrames(buffer.AsSpan(0, total).ToArray()))
            {
                if (frame.Type == Http3FrameType.HEADERS)
                    return Http2Parser.DecodeHeadersFrame(frame.Payload);
            }
        }

        // 兜底：对端可能直接裸发 HPACK（不带 HTTP/3 帧头）
        return Http2Parser.DecodeHeadersFrame(buffer.AsSpan(0, total).ToArray());
    }

    // ===== 响应帧写出 =====

    private static async Task SendHeadersAsync(ITransportConnection stream, IHttpResponseFeature resp, CancellationToken ct)
    {
        var headers = new Dictionary<string, string>(resp.Headers, StringComparer.OrdinalIgnoreCase)
        {
            [":status"] = resp.StatusCode.ToString()
        };

        var frame = Http3Parser.BuildHeadersFrame(headers);
        await stream.WriteAsync(frame, ct);
    }

    private static async Task SendBodyAsync(ITransportConnection stream, byte[] body, CancellationToken ct)
    {
        if (body.Length == 0) return;

        const int maxChunk = 16384;
        int offset = 0;
        while (offset < body.Length)
        {
            int chunk = Math.Min(maxChunk, body.Length - offset);
            var chunkBytes = new byte[chunk];
            Buffer.BlockCopy(body, offset, chunkBytes, 0, chunk);

            var frame = Http3Parser.BuildDataFrame(chunkBytes);
            await stream.WriteAsync(frame, ct);

            offset += chunk;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Protocol 层拥有 Transport（和 HttpConnection 一样）
        try { await _transport.DisposeAsync(); } catch { }
    }
}
