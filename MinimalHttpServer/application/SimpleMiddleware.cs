// 简化版 Pipeline —— 就是我们之前塞在 HttpConnection.HandleRequest 里的路由逻辑
// Kestrel 里对应的是 MapGet / MapPost / UseMiddleware 等中间件链

public class SimpleMiddleware : IHttpApplication
{
    private readonly DateTimeOffset _startTimeUtc;

    public SimpleMiddleware()
    {
        _startTimeUtc = DateTimeOffset.UtcNow;
    }

    public Task ProcessRequestAsync(IFeatureCollection features, CancellationToken ct = default)
    {
        var request = features.Get<IHttpRequestFeature>()!;
        var response = features.Get<IHttpResponseFeature>()!;
        var conn = features.Get<IHttpConnectionFeature>();

        Console.WriteLine($"[Pipeline] {request.Method} {request.Path}  " +
                          $"→ {conn?.ConnectionId}  来自 {conn?.RemoteAddress}");

        // ===== 全部业务逻辑在这里，Connection 层完全不知道 =====

        if (request.Method != "GET")
        {
            response.StatusCode = 405;
            response.StatusText = "Method Not Allowed";
            response.SetText("目前只支持 GET 请求");
            return Task.CompletedTask;
        }

        response.StatusCode = 200;
        response.StatusText = "OK";

        switch (request.Path)
        {
            case "/":
                response.SetText("<h1>欢迎试试 /hello /time /about /echo</h1>");
                break;

            case "/hello":
                response.SetText("Hello, Kestrel 学生！👋");
                break;

            case "/time":
                response.SetText($"当前时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                break;

            case "/about":
                response.SetText("MinimalHttpServer v0.5 — Features 字典 + Pipeline 版");
                break;

            case "/echo":
                // 演示读取请求 Body
                var bodyText = System.Text.Encoding.UTF8.GetString(request.Body);
                response.SetText($"Echo: {bodyText}");
                break;

            case "/stats":
                response.SetText($"服务已运行: {GetUptime()}\nConnectionId: {conn?.ConnectionId}");
                break;

            default:
                response.StatusCode = 404;
                response.StatusText = "Not Found";
                response.SetText("不存在的路径");
                break;
        }

        return Task.CompletedTask;
    }

    private string GetUptime()
    {
        var uptime = DateTimeOffset.UtcNow - _startTimeUtc;
        return uptime.ToString(@"hh\:mm\:ss");
    }
}
