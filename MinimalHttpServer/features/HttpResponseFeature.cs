using System.Text;

// IHttpResponseFeature 的实现

public class HttpResponseFeature : IHttpResponseFeature
{
    public int StatusCode { get; set; } = 200;
    public string StatusText { get; set; } = "OK";
    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public byte[] Body { get; set; } = Array.Empty<byte>();
    public bool HasStarted { get; set; }

    public void SetText(string text, string contentType = "text/html; charset=utf-8")
    {
        Body = Encoding.UTF8.GetBytes(text);
        Headers["Content-Type"] = contentType;
        Headers["Content-Length"] = Body.Length.ToString();
    }
}
