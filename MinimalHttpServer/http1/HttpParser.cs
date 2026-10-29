using System.Text;

// HTTP 工具方法（序列化 + 辅助）
// Http1Parser 负责状态机解析头部

public static class HttpParser
{
    // 从头部字典找 Content-Length
    public static long GetContentLength(Dictionary<string, string> headers)
    {
        return headers.TryGetValue("Content-Length", out var cl)
            && long.TryParse(cl, out var length) ? length : 0;
    }

    // 找 Transfer-Encoding 是否为 chunked
    public static bool IsChunked(Dictionary<string, string> headers)
    {
        return headers.TryGetValue("Transfer-Encoding", out var te)
            && te.Equals("chunked", StringComparison.OrdinalIgnoreCase);
    }

    // 将 HttpResponse 序列化为 HTTP/1.1 字节流（头部 + 可选 Body）
    public static byte[] SerializeResponse(HttpResponse response)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {response.StatusCode} {response.StatusText}\r\n");

        // 自动补 Content-Length（如果没设且有 Body）
        if (response.Body.Length > 0
            && !response.Headers.ContainsKey("Content-Length")
            && !response.Headers.ContainsKey("Transfer-Encoding"))
        {
            response.Headers["Content-Length"] = response.Body.Length.ToString();
        }

        foreach (var kv in response.Headers)
        {
            sb.Append($"{kv.Key}: {kv.Value}\r\n");
        }
        sb.Append("\r\n");

        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        var result = new byte[headerBytes.Length + response.Body.Length];
        Buffer.BlockCopy(headerBytes, 0, result, 0, headerBytes.Length);
        Buffer.BlockCopy(response.Body, 0, result, headerBytes.Length, response.Body.Length);
        return result;
    }
}
