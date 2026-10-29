// 图一 第 2 层契约：Transport —— 只提供 Read/Write 字节通道
// Kestrel 里对应的是 abstract class TransportConnection
// Protocol 层（HttpConnection / H2Connection / H3Connection）只依赖这个接口，
// 因此 TCP / TLS / QUIC 三种传输通道可以互换。

public interface ITransportConnection : IAsyncDisposable
{
    /// <summary>
    /// 从传输通道读取数据。返回 0 表示对端已关闭。
    /// </summary>
    Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default);

    /// <summary>
    /// 向传输通道写入数据。
    /// </summary>
    Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default);

    /// <summary>
    /// 半关闭写入端（告诉对端"我不会再发数据了"）。
    /// QUIC Stream 有明确的 End of Stream 语义；TCP 没有，实现为空操作。
    /// Kestrel 里对应 TransportConnection.CompleteWrites()。
    /// </summary>
    Task CompleteWritesAsync(CancellationToken ct = default);

    /// <summary>对端地址（如 "127.0.0.1:63969"）</summary>
    string RemoteAddress { get; }

    /// <summary>本地监听地址</summary>
    string LocalAddress { get; }

    /// <summary>传输层关闭时触发的令牌</summary>
    CancellationToken Closed { get; }
}
