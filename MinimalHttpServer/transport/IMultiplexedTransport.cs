// 图一 第 2 层契约（多路复用变体）—— 一个连接上能开出多条"子通道"
// Kestrel 里对应的是 Microsoft.AspNetCore.Connections.MultiplexedConnectionContext
//   → AcceptAsync() 返回 ConnectionContext（一个 Stream）
// 存在的意义：HTTP/3 每个请求占一个 QUIC Stream，
// 于是 Protocol 层（H3Connection）不再直接 new QuicConnection，
// 而是面向这个接口拿"一条条请求通道"。

public interface IMultiplexedTransport : IAsyncDisposable
{
    /// <summary>接收客户端新开的一条 Stream，包装成标准字节通道</summary>
    Task<ITransportConnection> AcceptStreamAsync(CancellationToken ct = default);

    /// <summary>对端地址</summary>
    string RemoteAddress { get; }

    /// <summary>本地监听地址</summary>
    string LocalAddress { get; }
}
