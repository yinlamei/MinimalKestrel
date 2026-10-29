// IHttpConnectionFeature 的实现
// 由 Protocol 层从注入进来的 ITransportConnection 提取地址信息

public class HttpConnectionFeature : IHttpConnectionFeature
{
    private static long _counter;

    public string? RemoteAddress { get; }
    public string? LocalAddress { get; }
    public string ConnectionId { get; }
    public DateTimeOffset ConnectTimeUtc { get; }

    public HttpConnectionFeature(ITransportConnection transport)
    {
        RemoteAddress = transport.RemoteAddress;
        LocalAddress = transport.LocalAddress;
        ConnectionId = $"conn-{Interlocked.Increment(ref _counter):D8}";
        ConnectTimeUtc = DateTimeOffset.UtcNow;
    }
}
