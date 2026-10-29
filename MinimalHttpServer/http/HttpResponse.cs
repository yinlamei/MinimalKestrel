// 简化版 HttpResponse POCO
// 在 Kestrel 里，这个概念对应的是 IHttpResponseFeature 接口

public class HttpResponse
{
    public int StatusCode { get; set; } = 200;
    public string StatusText { get; set; } = "OK";
    public Dictionary<string, string> Headers { get; set; } = new();
    public byte[] Body { get; set; } = Array.Empty<byte>();

    // 便捷方法：设置文本 Body，自动加 Content-Type 和 Content-Length
    public void SetText(string text, string contentType = "text/html; charset=utf-8")
    {
        Body = System.Text.Encoding.UTF8.GetBytes(text);
        Headers["Content-Type"] = contentType;
        Headers["Content-Length"] = Body.Length.ToString();
    }
}
