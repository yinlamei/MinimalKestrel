// HTTP/2 Connection — RFC 7540
// Kestrel 源码：Http2Connection.cs (~2000 行) + StreamDictionary + FrameReader + FrameWriter

public class H2Connection : IAsyncDisposable
{
    private readonly ITransportConnection _transport;
    private readonly IHttpApplication _app;
    private readonly Http2Parser _parser = new();
    private bool _disposed;
    private bool _handshaked; // 已确认客户端是 HTTP/2

    // 帧接收缓冲（HTTP/2 帧最大 = 9 + 16384）
    private readonly byte[] _rxBuf = new byte[9 + 16384 + 4096];
    private int _rxPos;  // 已读位置
    private int _rxLen;  // 有效数据长度

    // Stream 字典
    private readonly Dictionary<uint, Http2Stream> _streams = new();
    private readonly SemaphoreSlim _streamLock = new(1);
    private uint _nextStreamIdSeen; // 客户端 StreamID 递增

    public string? RemoteAddress => _transport.RemoteAddress;

    public H2Connection(ITransportConnection transport, IHttpApplication app)
    {
        _transport = transport;
        _app = app;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        // ========== 第一步：读客户端连接前言 ==========
        // RFC 7540 Section 3.5 — 客户端必须先发 "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n" (24 bytes)
        // 然后是一个 SETTINGS 帧
        var preface = Http2Constants.ConnectionPreface;
        while (_rxLen < preface.Length)
        {
            var n = await _transport.ReadAsync(_rxBuf.AsMemory()[_rxLen..], ct);
            if (n == 0) return;
            _rxLen += n;
        }

        // 验证前言
        if (!_rxBuf.AsSpan(0, preface.Length).SequenceEqual(preface))
        {
            Console.Error.WriteLine("[H2] Invalid connection preface, dropping connection");
            return;
        }

        _rxPos = preface.Length;
        Console.WriteLine("[H2] ✅ 收到连接前言，进入 HTTP/2 模式");

        // ========== 第二步：发服务端 SETTINGS ==========
        // Kestrel 默认发: MAX_CONCURRENT_STREAMS=100, INITIAL_WINDOW_SIZE=65535
        var mySettings = new List<Http2Setting>
        {
            new(Http2SettingId.MAX_CONCURRENT_STREAMS, 100),
            new(Http2SettingId.INITIAL_WINDOW_SIZE, 65535),
            new(Http2SettingId.MAX_FRAME_SIZE, 16384),
            new(Http2SettingId.MAX_HEADER_LIST_SIZE, 8192),
        };
        var settingsFrame = Http2Parser.BuildSettingsFrame(mySettings);
        await _transport.WriteAsync(settingsFrame, ct);
        Console.WriteLine("[H2] 已发送服务端 SETTINGS 帧");

        // ========== 第三步：帧主循环 ==========
        while (true)
        {
            // 确保 _rxPos 指向下一个帧的开始，数据从 _rxPos 到 _rxLen
            while (_rxPos < _rxLen)
            {
                // 等够 9 字节帧头
                if (_rxLen - _rxPos < 9) break;

                // 读帧头
                int length = (_rxBuf[_rxPos] << 16) | (_rxBuf[_rxPos + 1] << 8) | _rxBuf[_rxPos + 2];
                var type = (Http2FrameType)_rxBuf[_rxPos + 3];
                byte flags = _rxBuf[_rxPos + 4];
                uint streamId = ((uint)_rxBuf[_rxPos + 5] << 24) |
                                ((uint)_rxBuf[_rxPos + 6] << 16) |
                                ((uint)_rxBuf[_rxPos + 7] << 8)  |
                                (uint)_rxBuf[_rxPos + 8];
                streamId &= 0x7FFFFFFF;

                // 等够载荷
                int totalFrameLen = 9 + length;
                if (_rxLen - _rxPos < totalFrameLen) break; // 还没全到，继续等

                byte[]? payload = length > 0 ? new byte[length] : null;
                if (payload != null)
                    Buffer.BlockCopy(_rxBuf, _rxPos + 9, payload, 0, length);

                var frame = new Http2Frame(length, type, flags, streamId, payload ?? Array.Empty<byte>());
                _rxPos += totalFrameLen;

                // 处理帧
                await HandleFrameAsync(frame, ct);
            }

            // 如果缓冲不够，挪数据或继续读
            if (_rxPos > 0)
            {
                Buffer.BlockCopy(_rxBuf, _rxPos, _rxBuf, 0, _rxLen - _rxPos);
                _rxLen -= _rxPos;
                _rxPos = 0;
            }

            // 读更多
            if (_rxLen >= _rxBuf.Length - 4096)
            {
                Console.Error.WriteLine("[H2] Frame buffer overflow, closing");
                break;
            }
            var bytesRead = await _transport.ReadAsync(_rxBuf.AsMemory()[_rxLen..], ct);
            if (bytesRead == 0)
            {
                Console.WriteLine("[H2] 对端关闭连接");
                break;
            }
            _rxLen += bytesRead;
        }
    }

    // ============ 帧处理分发 ============

    private async Task HandleFrameAsync(Http2Frame frame, CancellationToken ct)
    {
        switch (frame.Type)
        {
            case Http2FrameType.SETTINGS:
                await HandleSettingsFrameAsync(frame, ct);
                break;

            case Http2FrameType.HEADERS:
                await HandleHeadersFrameAsync(frame, ct);
                break;

            case Http2FrameType.DATA:
                HandleDataFrame(frame);
                break;

            case Http2FrameType.WINDOW_UPDATE:
                // 简化版忽略流控 — 实际 Kestrel 严格遵守
                break;

            case Http2FrameType.RST_STREAM:
                await HandleRstStreamFrameAsync(frame, ct);
                break;

            case Http2FrameType.GOAWAY:
                Console.WriteLine("[H2] 收到 GOAWAY 帧，客户端请求关闭连接");
                throw new OperationCanceledException();

            case Http2FrameType.PING:
                var pong = Http2Parser.EncodeFrame(Http2FrameType.PING, Http2Flags.ACK, 0, frame.Payload);
                await _transport.WriteAsync(pong, ct);
                break;

            default:
                Console.WriteLine($"[H2] 忽略未知帧类型: {frame.Type}");
                break;
        }
    }

    private async Task HandleSettingsFrameAsync(Http2Frame frame, CancellationToken ct)
    {
        bool isAck = (frame.Flags & Http2Flags.ACK) != 0;
        if (!isAck)
        {
            // 客户端发的 SETTINGS — 解析但简化版不做复杂协商
            // 然后回复 SETTINGS ACK
            var ack = Http2Parser.BuildSettingsAckFrame();
            await _transport.WriteAsync(ack, ct);
            _handshaked = true;
            Console.WriteLine("[H2] 收到客户端 SETTINGS → 已回复 ACK");
        }
        else
        {
            Console.WriteLine("[H2] 收到 SETTINGS ACK（客户端接受我们的 SETTINGS）");
        }
    }

    private async Task HandleHeadersFrameAsync(Http2Frame frame, CancellationToken ct)
    {
        bool endStream = (frame.Flags & Http2Flags.END_STREAM) != 0;
        bool endHeaders = (frame.Flags & Http2Flags.END_HEADERS) != 0;

        // 解析头部
        var headers = Http2Parser.DecodeHeadersFrame(frame.Payload);

        // 创建或获取 Stream
        await _streamLock.WaitAsync(ct);
        try
        {
            if (!_streams.TryGetValue(frame.StreamId, out var stream))
            {
                stream = new Http2Stream(frame.StreamId);
                _streams[frame.StreamId] = stream;
                Console.WriteLine($"[H2] 🆕 新 Stream #{frame.StreamId} 创建");
            }

            // 填充请求
            foreach (var h in headers)
            {
                switch (h.Key.ToLowerInvariant())
                {
                    case ":method": stream.Method = h.Value; break;
                    case ":path": stream.Path = h.Value; break;
                    case ":scheme": stream.Scheme = h.Value; break;
                    case ":authority": stream.Authority = h.Value; break;
                    default: stream.Headers[h.Key] = h.Value; break;
                }
            }

            if (endHeaders) stream.NotifyHeadersReceived();
            if (endStream) stream.NotifyEndStreamReceived();

            _nextStreamIdSeen = Math.Max(_nextStreamIdSeen, frame.StreamId);
        }
        finally { _streamLock.Release(); }

        // 头部完整了 — 启动 Pipeline 处理
        if (endHeaders)
        {
            _ = RunStreamPipelineAsync(frame.StreamId, ct);
        }
    }

    private void HandleDataFrame(Http2Frame frame)
    {
        bool endStream = (frame.Flags & Http2Flags.END_STREAM) != 0;

        _streamLock.Wait();
        try
        {
            if (_streams.TryGetValue(frame.StreamId, out var stream))
            {
                if (frame.Payload.Length > 0)
                {
                    var old = stream.RequestBody;
                    stream.RequestBody = new byte[old.Length + frame.Payload.Length];
                    Buffer.BlockCopy(old, 0, stream.RequestBody, 0, old.Length);
                    Buffer.BlockCopy(frame.Payload, 0, stream.RequestBody, old.Length, frame.Payload.Length);
                }
                if (endStream) stream.NotifyEndStreamReceived();
            }
        }
        finally { _streamLock.Release(); }
    }

    private async Task HandleRstStreamFrameAsync(Http2Frame frame, CancellationToken ct)
    {
        await _streamLock.WaitAsync(ct);
        try
        {
            if (_streams.TryGetValue(frame.StreamId, out var stream))
            {
                stream.Close();
                _streams.Remove(frame.StreamId);
            }
        }
        finally { _streamLock.Release(); }
    }

    // ============ Stream Pipeline 调度 ============

    private async Task RunStreamPipelineAsync(uint streamId, CancellationToken ct)
    {
        Http2Stream stream;
        await _streamLock.WaitAsync(ct);
        try { stream = _streams[streamId]; }
        finally { _streamLock.Release(); }

        // 等 END_STREAM（如果客户端有 Body 的话）
        // 简化版：如果没有 Content-Length 或 Transfer-Encoding，就假设没有 Body
        await Task.Run(() => stream.HeadersReady.Wait(5000), ct);

        if (!string.IsNullOrEmpty(stream.Path))
        {
            Console.WriteLine($"[H2-#{streamId}] {stream.Method} {stream.Path}");

            // 构建 IHttpRequestFeature 风格的 request
            var request = new HttpRequest
            {
                Method = stream.Method,
                Path = stream.Path,
                Version = stream.Scheme.ToUpper() + "/2.0",
                Headers = stream.Headers,
                Body = stream.RequestBody,
            };

            // 填 FeatureCollection → 交给 Pipeline
            var features = new FeatureCollection();
            features.Set<IHttpRequestFeature>(new HttpRequestFeature(request));
            features.Set<IHttpResponseFeature>(new HttpResponseFeature());
            features.Set<IHttpConnectionFeature>(new HttpConnectionFeature(_transport));

            try
            {
                await _app.ProcessRequestAsync(features, ct);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[H2-#{streamId}] Pipeline error: {ex.Message}");
                stream.ResponseStatusCode = 500;
                stream.ResponseStatusText = "Internal Server Error";
            }

            // 取回 Pipeline 写的响应
            var resp = features.Get<IHttpResponseFeature>()!;
            stream.ResponseStatusCode = resp.StatusCode;
            stream.ResponseStatusText = resp.StatusText;
            stream.ResponseBody = resp.Body;
            stream.ResponseHeaders = new Dictionary<string, string>(resp.Headers);

            // 构建 HTTP/2 :status 头部（HPACK Literal Header Field）
            stream.ResponseHeaders[":status"] = stream.ResponseStatusCode.ToString();
        }

        // ============ 发送响应帧 ============
        try
        {
            await SendResponseHeadersAsync(stream, ct);
            await SendResponseDataAsync(stream, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[H2-#{streamId}] 发送响应失败: {ex.Message}");
            try
            {
                var rst = Http2Parser.BuildRstStreamFrame(stream.StreamId, 0x1); // PROTOCOL_ERROR
                await _transport.WriteAsync(rst, ct);
            }
            catch { }
        }

        // 清理 Stream
        await _streamLock.WaitAsync(ct);
        try { stream.Close(); _streams.Remove(stream.StreamId); }
        finally { _streamLock.Release(); }
    }

    private async Task SendResponseHeadersAsync(Http2Stream stream, CancellationToken ct)
    {
        bool endStream = stream.ResponseBody.Length == 0;

        var payload = Http2Parser.EncodeHeadersFramePayload(stream.ResponseHeaders);
        byte flags = Http2Flags.END_HEADERS;
        if (endStream) flags |= Http2Flags.END_STREAM;

        var frame = Http2Parser.EncodeFrame(Http2FrameType.HEADERS, flags, stream.StreamId, payload);
        await _transport.WriteAsync(frame, ct);
        Console.WriteLine($"[H2-#{stream.StreamId}] 已发送响应 HEADERS: {stream.ResponseStatusCode}");
    }

    private async Task SendResponseDataAsync(Http2Stream stream, CancellationToken ct)
    {
        if (stream.ResponseBody.Length == 0) return;

        int remaining = stream.ResponseBody.Length;
        int offset = 0;
        bool first = true;

        while (remaining > 0)
        {
            int chunk = Math.Min(remaining, 16384);
            var chunkBytes = new byte[chunk];
            Buffer.BlockCopy(stream.ResponseBody, offset, chunkBytes, 0, chunk);
            offset += chunk;
            remaining -= chunk;

            byte flags = 0;
            if (remaining == 0) flags = Http2Flags.END_STREAM;

            var frame = Http2Parser.EncodeFrame(Http2FrameType.DATA, flags, stream.StreamId, chunkBytes);
            await _transport.WriteAsync(frame, ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            var goAway = Http2Parser.BuildGoAwayFrame(_nextStreamIdSeen, 0x0);
            await _transport.WriteAsync(goAway);
        }
        catch { }
        try { await _transport.DisposeAsync(); } catch { }
        Console.WriteLine($"[H2] [{RemoteAddress}] 连接关闭（{_streams.Count} 个残留 Stream）");
    }
}
