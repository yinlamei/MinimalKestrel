// IHttpRequestFeature 的实现
// 由 Protocol 层（HttpConnection / H2Connection / H3Connection）从解析结果创建

public class HttpRequestFeature : IHttpRequestFeature
{
    public string Method { get; }
    public string Path { get; }
    public string Protocol { get; }
    public IDictionary<string, string> Headers { get; }
    public byte[] Body { get; }

    public HttpRequestFeature(HttpRequest request)
    {
        Method = request.Method;
        Path = request.Path;
        Protocol = request.Version;
        Headers = request.Headers;
        Body = request.Body;
    }
}
