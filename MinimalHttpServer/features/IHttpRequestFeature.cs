// IHttpRequestFeature —— Protocol 层写、Pipeline 读
// Kestrel 源码：src/Http/Http.Features/IHttpRequestFeature.cs

public interface IHttpRequestFeature
{
    string Method { get; }
    string Path { get; }
    string Protocol { get; }   // HTTP/1.1, HTTP/2, HTTP/3
    IDictionary<string, string> Headers { get; }
    byte[] Body { get; }
}
