using System.Net;
using System.Net.Sockets;

// 图一 第 2 层实现：TCP 字节通道
// Kestrel 里对应的是 SocketsTransportConnection

public class SocketTransport : ITransportConnection
{
    private readonly Socket _socket;
    private readonly CancellationTokenSource _closedCts;
    private bool _disposed;

    public string RemoteAddress { get; }
    public string LocalAddress { get; }
    public CancellationToken Closed => _closedCts.Token;

    public SocketTransport(Socket socket)
    {
        _socket = socket;
        _socket.NoDelay = true; // Socket 专属优化，封装在 Transport 实现内部

        RemoteAddress = TryGetAddress(socket.RemoteEndPoint);
        LocalAddress = TryGetAddress(socket.LocalEndPoint);

        _closedCts = new CancellationTokenSource();
    }

    public async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        int n;
        try
        {
            n = await _socket.ReceiveAsync(buffer, ct);
        }
        catch when (!ct.IsCancellationRequested)
        {
            _closedCts.Cancel();
            throw;
        }

        if (n == 0) _closedCts.Cancel(); // 对端优雅关闭
        return n;
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        try
        {
            await _socket.SendAsync(buffer, ct);
        }
        catch when (!ct.IsCancellationRequested)
        {
            _closedCts.Cancel();
            throw;
        }
    }

    // TCP 没有"半关闭写入"的协议语义（HTTP/1.1 靠 Connection: close 表示）
    public Task CompleteWritesAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try { _socket.Shutdown(SocketShutdown.Both); } catch { }
        try { _socket.Close(); } catch { }

        if (!_closedCts.IsCancellationRequested)
            _closedCts.Cancel();
        _closedCts.Dispose();

        await Task.CompletedTask;
    }

    private static string? TryGetAddress(EndPoint? ep)
    {
        try { return ep?.ToString(); }
        catch { return null; }
    }
}
