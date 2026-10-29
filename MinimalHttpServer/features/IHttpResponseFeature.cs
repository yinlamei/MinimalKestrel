// IHttpResponseFeature —— Pipeline 写、Protocol 层读
// Kestrel 源码：src/Http/Http.Features/IHttpResponseFeature.cs

public interface IHttpResponseFeature
{
    int StatusCode { get; set; }
    string StatusText { get; set; }
    IDictionary<string, string> Headers { get; }
    byte[] Body { get; set; }

    /// <summary>响应头是否已发送（一旦 start，就不能再改 StatusCode/Headers 了）</summary>
    bool HasStarted { get; set; }

    // 便捷方法
    void SetText(string text, string contentType = "text/html; charset=utf-8");
}
