// MinimalHttpClient —— MinimalHttpServer 的演示客户端
//
// 用法：
//   dotnet run -- h2raw /hello          原始帧 HTTP/2，对接本教学服务器（127.0.0.1:8080）
//   dotnet run -- h2  https://example.com   标准 HTTP/2，对接真实服务器
//   dotnet run -- h3  https://example.com   标准 HTTP/3，对接真实服务器
//   dotnet run --                         默认 h2raw /hello

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "h2raw";
try
{
    switch (mode)
    {
        case "h2raw":
        {
            // 对接 MinimalHttpServer 的 HTTP/2 端点（h2c 明文）
            var path = args.Length > 1 ? args[1] : "/hello";
            var client = new H2RawClient("127.0.0.1", 8080);
            await client.SendGetAsync(path);
            break;
        }

        case "h2":
        {
            var url = args.Length > 1 ? args[1] : "http://127.0.0.1:8080/hello";
            await StandardHttpClient.SendHttp2Async(url);
            break;
        }

        case "h3":
        {
            var url = args.Length > 1 ? args[1] : "https://localhost:8443/hello";
            await StandardHttpClient.SendHttp3Async(url);
            break;
        }

        default:
            Console.WriteLine("未知模式。用法: dotnet run -- [h2raw|h2|h3] [url]");
            Console.WriteLine("  h2raw  原始帧 HTTP/2，对接本教学服务器 127.0.0.1:8080");
            Console.WriteLine("  h2     标准 HTTP/2，对接真实服务器");
            Console.WriteLine("  h3     标准 HTTP/3，对接真实服务器");
            break;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"错误: {ex.Message}");
    Environment.ExitCode = 1;
}
