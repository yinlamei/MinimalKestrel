using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

// 标准客户端 —— 用 .NET 内置 HttpClient 发 HTTP/2 和 HTTP/3。
//
// 注意：这种客户端使用完整 HPACK / QPACK，所以**不能**对接 MinimalHttpServer
// （它只实现了简化版 HPACK）。它的目标是真实的 HTTP/2 / HTTP/3 服务器。

public static class StandardHttpClient
{
    /// <summary>
    /// 发一个 HTTP/2 请求。默认走 h2（TLS ALPN）；若要 h2c（明文）需用 SocketsHttpHandler。
    /// </summary>
    public static async Task SendHttp2Async(string url, CancellationToken ct = default)
    {
        using var handler = new SocketsHttpHandler();
        using var http = new HttpClient(handler);

        var req = new HttpRequestMessage(HttpMethod.Get, url)
        {
            // 强制使用 HTTP/2；如果服务器不支持会失败（不降级）
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };

        Console.WriteLine($"→ HTTP/2 GET {url}");
        using var resp = await http.SendAsync(req, ct);
        Console.WriteLine($"← 版本 {resp.Version}  状态 {resp.StatusCode}");
        var body = await resp.Content.ReadAsStringAsync(ct);
        Console.WriteLine($"  Body: {body}");
    }

    /// <summary>
    /// 发一个 HTTP/3 请求。需要：
    ///   1. 目标 URL 是 https://
    ///   2. 服务器支持 ALPN "h3"
    ///   3. 本机系统支持 QUIC（Windows 需启用 MSQuic）
    /// </summary>
    public static async Task SendHttp3Async(string url, CancellationToken ct = default)
    {
        using var handler = new SocketsHttpHandler
        {
            // 演示/自签名证书场景下禁用证书校验；生产环境千万不要这样做！
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, cert, _, errors) =>
                {
                    Console.WriteLine($"  证书: {cert?.Subject}  错误: {errors}");
                    return true;   // 仅用于演示
                },
            },
        };
        using var http = new HttpClient(handler);

        var req = new HttpRequestMessage(HttpMethod.Get, url)
        {
            Version = HttpVersion.Version30,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };

        Console.WriteLine($"→ HTTP/3 GET {url}");
        using var resp = await http.SendAsync(req, ct);
        Console.WriteLine($"← 版本 {resp.Version}  状态 {resp.StatusCode}");
        var body = await resp.Content.ReadAsStringAsync(ct);
        Console.WriteLine($"  Body: {body}");
    }
}
