using System.Net.Quic;

// 图一 第 2 层实现：QUIC Stream 字节通道
// Kestrel 里对应的是 QuicTransportConnection（每个 HTTP/3 请求一个）
// 注意：Protocol 层只看到 ITransportConnection，看不到 QuicStream —— 这就是层间解耦。

public class QuicStreamTransport : ITransportConnection
{
    private readonly QuicConnection _connection;
    private readonly QuicStream _stream;
    private bool _disposed;

    public string RemoteAddress { get; }
    public string LocalAddress { get; }
    public CancellationToken Closed => CancellationToken.None;

    public QuicStreamTransport(QuicConnection connection, QuicStream stream)
    {
        _connection = connection;
        _stream = stream;

        try { RemoteAddress = connection.RemoteEndPoint?.ToString() ?? "(unknown)"; }
        catch { RemoteAddress = "(unknown)"; }

        try { LocalAddress = connection.LocalEndPoint?.ToString() ?? "(unknown)"; }
        catch { LocalAddress = "(unknown)"; }
    }

    public Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        => _stream.ReadAsync(buffer, ct).AsTask();

    public Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        => _stream.WriteAsync(buffer, ct).AsTask();

    // QUIC 才有真正的 End of Stream 语义
    public Task CompleteWritesAsync(CancellationToken ct = default)
    {
        _stream.CompleteWrites();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        return _stream.DisposeAsync();
    }
}
