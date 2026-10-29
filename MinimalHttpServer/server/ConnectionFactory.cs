using System.Net.Sockets;

// 图一 第 1 层 → 第 2/3 层的胶水：协议嗅探 + Connection 选择
// Kestrel 源码：ConnectionFactory.cs + HttpConnectionMiddleware（按 ALPN/preface 选协议）
// 这里是"层间契约"的落点：只有它同时知道 Socket、ITransportConnection 和具体 Connection。

public class ConnectionFactory
{
    private readonly IHttpApplication _app;

    public ConnectionFactory(IHttpApplication app)
    {
        _app = app;
    }

    /// <summary>
    /// TCP 连接入口：先读前 24 字节嗅探 HTTP/2 连接前言，
    /// 再决定用 H2Connection 还是 HttpConnection。
    /// </summary>
    public async Task ExecuteTcpConnectionAsync(Socket socket, CancellationToken ct = default)
    {
        IAsyncDisposable? connection = null;
        try
        {
            var preface = await ReadPrefaceAsync(socket, Http2Constants.ConnectionPreface.Length, ct);
            if (preface.Length == 0) return;   // 对端没发数据就断开了

            // ② Transport 层：把 Socket（含预读字节）包装成字节通道
            ITransportConnection transport = new SocketTransportWithPreface(socket, preface);

            // ③ Protocol 层：注入 Transport + Pipeline
            if (IsHttp2Preface(preface))
            {
                Console.WriteLine("[ConnectionFactory] 检测到 HTTP/2 连接前言 → H2Connection");
                connection = new H2Connection(transport, _app);
            }
            else
            {
                Console.WriteLine("[ConnectionFactory] 未检测到 HTTP/2 前言 → HttpConnection (HTTP/1.1)");
                connection = new HttpConnection(transport, _app);
            }

            // ④ 启动连接处理
            await RunAsync(connection, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ConnectionFactory] 连接处理异常: {ex}");
            try { socket.Close(); } catch { }
        }
    }

    /// <summary>
    /// HTTP/3 请求级入口：一条 QUIC Stream = 一个 HTTP/3 请求，
    /// 由 H3Connection 内部创建 FeatureCollection 并调用 Pipeline。
    /// </summary>
    public H3Connection CreateHttp3Connection(IMultiplexedTransport transport)
        => new H3Connection(transport, _app);

    private static bool IsHttp2Preface(byte[] preface)
        => preface.Length >= Http2Constants.ConnectionPreface.Length
           && preface.AsSpan(0, Http2Constants.ConnectionPreface.Length)
                     .SequenceEqual(Http2Constants.ConnectionPreface);

    private static async Task<byte[]> ReadPrefaceAsync(Socket socket, int length, CancellationToken ct)
    {
        var buffer = new byte[length];
        int total = 0;
        while (total < length)
        {
            var n = await socket.ReceiveAsync(buffer.AsMemory(total), ct);
            if (n == 0) return buffer[..total];   // 对端提前关闭
            total += n;
        }
        return buffer;
    }

    private static async Task RunAsync(IAsyncDisposable connection, CancellationToken ct)
    {
        await using (connection)
        {
            switch (connection)
            {
                case H2Connection h2:
                    await h2.StartAsync(ct);
                    break;
                case HttpConnection h1:
                    await h1.StartAsync(ct);
                    break;
            }
        }
    }
}
