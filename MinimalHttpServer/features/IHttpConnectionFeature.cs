// IHttpConnectionFeature —— 连接级信息
// Kestrel 源码：src/Http/Http.Features/IHttpConnectionFeature.cs

public interface IHttpConnectionFeature
{
    string? RemoteAddress { get; }
    string? LocalAddress { get; }
    string ConnectionId { get; }   // 每次 Connection 唯一
    DateTimeOffset ConnectTimeUtc { get; }
}
