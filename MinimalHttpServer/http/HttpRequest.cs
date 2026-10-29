// 简化版 HttpRequest POCO
// 在 Kestrel 里，这个概念对应的是 IHttpRequestFeature 接口

public class HttpRequest
{
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = "/";
    public string Version { get; set; } = "HTTP/1.1";
    public Dictionary<string, string> Headers { get; set; } = new();
    public byte[] Body { get; set; } = Array.Empty<byte>();
}
