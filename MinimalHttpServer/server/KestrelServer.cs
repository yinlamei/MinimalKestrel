using System.Net;
using System.Net.Sockets;

// 图一 第 1 层：KestrelServer —— Accept 循环
// Kestrel 源码：KestrelServerImpl.cs（StartAsync → AcceptLoopAsync → OnAccepted）
// 职责只有三件事：Bind / Listen / Accept，然后把 Socket 交给 ConnectionFactory。
// 这一层不知道 HTTP 是什么，更不知道 HTTP/1.1 还是 HTTP/2。

public class KestrelServer
{
    private readonly IHttpApplication _app;
    private readonly IPEndPoint _endPoint;
    private readonly int _backlog;
    private readonly ConnectionFactory _connectionFactory;

    public KestrelServer(IHttpApplication app, IPEndPoint endPoint, int backlog = 128)
    {
        _app = app;
        _endPoint = endPoint;
        _backlog = backlog;
        _connectionFactory = new ConnectionFactory(app);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(_endPoint);
        listener.Listen(_backlog);
        Console.WriteLine($"[TCP] 监听 {_endPoint} — HTTP/1.1 + HTTP/2 (h2c) 自动分流");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // ① 拿到一个新 Socket（Kestrel 的 TransportManager 做的事）
                var socket = await listener.AcceptAsync(ct);

                // ② 交给 ConnectionFactory：由它 sniff 协议、构造 Transport、挑 Connection
                //    fire-and-forget，绝不阻塞 Accept 循环
                _ = _connectionFactory.ExecuteTcpConnectionAsync(socket, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException ex)
        {
            Console.Error.WriteLine($"[TCP] Accept 失败: {ex.SocketErrorCode}");
        }
        finally
        {
            try { listener.Close(); } catch { }
        }
    }
}
