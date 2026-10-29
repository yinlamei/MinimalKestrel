using System.Net;
using System.Net.Quic;
using System.Net.Security;

// 图一 第 1 层：QuicServer —— UDP 上的 Accept 循环（HTTP/3 入口）
// Kestrel 源码：QuicTransport.ListenAsync + 每个连接走 Http3Connection
// 职责同样只有 Bind / Listen / Accept，协议细节全部交给 H3Connection。

public class QuicServer
{
    private readonly IPEndPoint _endPoint;
    private readonly int _backlog;
    private readonly ConnectionFactory _connectionFactory;

    public QuicServer(IHttpApplication app, IPEndPoint endPoint, int backlog = 512)
    {
        _endPoint = endPoint;
        _backlog = backlog;
        _connectionFactory = new ConnectionFactory(app);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (!QuicListener.IsSupported)
        {
            Console.WriteLine("[QUIC] 当前系统不支持 QUIC，跳过 HTTP/3");
            return;
        }

        // QUIC 强制 TLS 1.3，所以必须有证书（开发用自签名）
        var cert = SelfSignedCertificate.Create("localhost");

        var options = new QuicListenerOptions
        {
            ListenEndPoint = _endPoint,
            ApplicationProtocols = new List<SslApplicationProtocol>
            {
                new(Http3Constants.AlpnHttp3),   // ALPN = "h3"
            },
            ListenBacklog = _backlog,
            ConnectionOptionsCallback = (_, _, _) =>
                new ValueTask<QuicServerConnectionOptions>(new QuicServerConnectionOptions
                {
                    DefaultCloseErrorCode = 0x0,
                    DefaultStreamErrorCode = 0x0,
                    ServerAuthenticationOptions = new SslServerAuthenticationOptions
                    {
                        ServerCertificate = cert,
                        ClientCertificateRequired = false,
                    },
                }),
        };

        await using var listener = await QuicListener.ListenAsync(options, ct);
        Console.WriteLine($"[QUIC] 监听 {_endPoint} — HTTP/3 (UDP)  证书: {cert.Subject}");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var quicConnection = await listener.AcceptConnectionAsync(ct);

                // ② Transport：把 QuicConnection 包装成多路复用通道
                var transport = new QuicMultiplexedTransport(quicConnection);

                // ③ Protocol：同样由 ConnectionFactory 组装（与 TCP 走同一条胶水路径）
                var connection = _connectionFactory.CreateHttp3Connection(transport);
                _ = RunConnectionAsync(connection);
            }
        }
        catch (OperationCanceledException) { }
        catch (QuicException ex)
        {
            Console.Error.WriteLine($"[QUIC] 连接异常: {ex.QuicError}");
        }
    }

    private static async Task RunConnectionAsync(H3Connection connection)
    {
        await using (connection)
        {
            await connection.StartAsync();
        }
    }
}
