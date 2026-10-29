// HTTP/2 Stream — RFC 7540 Section 5
// Kestrel 里对应：Http2Stream.cs / Http2Connection.StreamDictionary

public class Http2Stream
{
    public uint StreamId { get; }
    public bool IsOpen { get; private set; } = true;
    public bool HeadersEnded { get; private set; }
    public bool EndStreamReceived { get; private set; }
    public bool EndStreamSent { get; private set; }

    // 请求
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public string Scheme { get; set; } = "https";
    public string Authority { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public byte[] RequestBody { get; set; } = Array.Empty<byte>();

    // 响应
    public Dictionary<string, string> ResponseHeaders { get; set; } = new();
    public byte[] ResponseBody { get; set; } = Array.Empty<byte>();
    public int ResponseStatusCode { get; set; } = 200;
    public string ResponseStatusText { get; set; } = "OK";

    // 事件
    public ManualResetEventSlim HeadersReady { get; } = new(false);
    public ManualResetEventSlim ResponseSent { get; } = new(false);

    public Http2Stream(uint streamId)
    {
        StreamId = streamId;
    }

    public void NotifyHeadersReceived()
    {
        HeadersEnded = true;
        HeadersReady.Set();
    }

    public void NotifyEndStreamReceived()
    {
        EndStreamReceived = true;
    }

    public void Close()
    {
        IsOpen = false;
        ResponseSent.Set();
    }
}
