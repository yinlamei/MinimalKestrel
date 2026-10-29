using System.Net.Quic;

// 图一 第 2 层实现：QUIC 多路复用传输
// Kestrel 里对应的是 QuicTransport + IQuicServerTransport（底层 System.Net.Quic）
// 职责：把 QuicConnection 的多路复用能力暴露成 IMultiplexedTransport

public class QuicMultiplexedTransport : IMultiplexedTransport
{
    private readonly QuicConnection _connection;
    private bool _disposed;

    public string RemoteAddress { get; }
    public string LocalAddress { get; }

    public QuicMultiplexedTransport(QuicConnection connection)
    {
        _connection = connection;
        RemoteAddress = SafeAddress(connection.RemoteEndPoint);
        LocalAddress = SafeAddress(connection.LocalEndPoint);
    }

    public async Task<ITransportConnection> AcceptStreamAsync(CancellationToken ct = default)
    {
        // 一个 QUIC Stream 就是一条请求级字节通道
        var stream = await _connection.AcceptInboundStreamAsync(ct);
        return new QuicStreamTransport(_connection, stream);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try { await _connection.CloseAsync(0x0, default); } catch { }
        try { await _connection.DisposeAsync(); } catch { }
    }

    private static string SafeAddress(System.Net.EndPoint? ep)
    {
        try { return ep?.ToString() ?? "(unknown)"; }
        catch { return "(unknown)"; }
    }
}
