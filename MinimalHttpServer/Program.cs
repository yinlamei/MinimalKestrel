using System.Net;

// 整个程序的"装配车间"——只做 wiring，不写业务、不写协议
// 图一四层在这里被组合起来：
//   第 1 层 KestrelServer / QuicServer（Accept 循环）
//   第 2 层 SocketTransport / QuicMultiplexedTransport（Read/Write 字节通道）
//   第 3 层 HttpConnection / H2Connection / H3Connection（读/解析/填 Features）
//   第 4 层 SimpleMiddleware（Pipeline 路由 + 生成响应）

var app = new SimpleMiddleware();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("║   MinimalHttpServer v0.7  (HTTP/1.1 + HTTP/2 + HTTP/3)    ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine("  HTTP/1.1 或 HTTP/2 (h2c)  →  http://localhost:8080");
Console.WriteLine("  HTTP/3                    →  https://localhost:8443");
Console.WriteLine("  Routes: /  /hello  /time  /about  /echo  /stats");
Console.WriteLine();

// 图一 第 1 层：两种传输各起一个 Accept 循环
var tcpServer = new KestrelServer(app, new IPEndPoint(IPAddress.Loopback, 8080));
var quicServer = new QuicServer(app, new IPEndPoint(IPAddress.Loopback, 8443));

try
{
    await Task.WhenAll(
        tcpServer.StartAsync(cts.Token),
        quicServer.StartAsync(cts.Token));
}
catch (OperationCanceledException)
{
    Console.WriteLine("\n正在关闭...");
}
