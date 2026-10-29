using System.Net.Sockets;

// 图一 第 2 层：带"预读字节"的 TCP 字节通道
// 真正的字节通道工作全部委托给 SocketTransport（也就是图一里 "new SocketTransport(socket)" 那一步），
// 本类只额外负责：把 ConnectionFactory 嗅探协议时已经读走的 24 字节前言先吐出来。
// 这样 Protocol 层依然只面对 ITransportConnection，完全不知道字节被预读过。

public class SocketTransportWithPreface : ITransportConnection
{
    private readonly SocketTransport _inner;   // 复用标准 Transport 实现
    private readonly byte[] _preface;          // 预读出来的字节
    private int _prefacePos;                   // 预读缓冲已消费到的位置

    public string RemoteAddress => _inner.RemoteAddress;
    public string LocalAddress => _inner.LocalAddress;
    public CancellationToken Closed => _inner.Closed;

    public SocketTransportWithPreface(Socket socket, byte[] preface)
    {
        _inner = new SocketTransport(socket);
        _preface = preface;
        _prefacePos = 0;
    }

    public Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        // ① 先吐出预读缓冲
        if (_prefacePos < _preface.Length)
        {
            int toCopy = Math.Min(_preface.Length - _prefacePos, buffer.Length);
            _preface.AsSpan(_prefacePos, toCopy).CopyTo(buffer.Span);
            _prefacePos += toCopy;
            return Task.FromResult(toCopy);
        }

        // ② 预读缓冲空了，交给底层 SocketTransport 正常读
        return _inner.ReadAsync(buffer, ct);
    }

    public Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        => _inner.WriteAsync(buffer, ct);

    public Task CompleteWritesAsync(CancellationToken ct = default)
        => _inner.CompleteWritesAsync(ct);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
