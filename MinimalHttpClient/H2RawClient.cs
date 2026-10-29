using System.Net.Sockets;
using System.Text;

// HTTP/2 原始帧客户端 —— 完全不依赖 HttpClient / SocketsHttpHandler 的 HPACK。
//
// 为什么要手搓帧？因为 MinimalHttpServer 只实现了简化版 HPACK（前缀 0x00），
// 真实 HttpClient 会用静态表索引(0x80) / Huffman，服务器解析不了。
// 所以演示 HTTP/2 帧结构时，直接拼字节最直观。
//
// 对应服务端：http2/H2Connection.cs + http2/Http2Parser.cs

public class H2RawClient
{
    // 24 字节连接前言 (RFC 7540 3.5) —— 客户端必须先发
    private static readonly byte[] ConnectionPreface =
        Encoding.ASCII.GetBytes("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n");

    // 帧类型常量
    private const byte FrameSettings = 0x04;
    private const byte FrameHeaders = 0x01;
    private const byte FrameData = 0x00;

    // 帧 Flags
    private const byte FlagAck = 0x01;
    private const byte FlagEndHeaders = 0x04;
    private const byte FlagEndStream = 0x01;

    private readonly string _host;
    private readonly int _port;

    public H2RawClient(string host, int port)
    {
        _host = host;
        _port = port;
    }

    /// <summary>
    /// 发送一个 GET 请求并打印收到的所有帧。
    /// </summary>
    public async Task SendGetAsync(string path, CancellationToken ct = default)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(_host, _port, ct);
        var stream = client.GetStream();

        // ========== ① 连接前言 + 空 SETTINGS（告诉服务端"我是 HTTP/2"）==========
        var clientSettings = BuildFrame(FrameSettings, 0, 0, Array.Empty<byte>());
        await stream.WriteAsync(ConnectionPreface, ct);
        await stream.WriteAsync(clientSettings, ct);
        Console.WriteLine("→ 已发送连接前言 + 客户端 SETTINGS");

        // ========== ② HEADERS 帧（带 :method/:path/:scheme/:authority 伪头部）==========
        var headers = new Dictionary<string, string>
        {
            [":method"] = "GET",
            [":path"] = path,
            [":scheme"] = "http",
            [":authority"] = $"{_host}:{_port}",
        };
        var hpackPayload = SimpleHpackEncoder.EncodeHeaders(headers);
        // END_HEADERS | END_STREAM = 0x04 | 0x01 = 0x05（GET 没有 Body，头部结束就是请求结束）
        var headersFrame = BuildFrame(FrameHeaders, FlagEndHeaders | FlagEndStream, streamId: 1, hpackPayload);
        await stream.WriteAsync(headersFrame, ct);
        Console.WriteLine($"→ 已发送 HEADERS 帧 (stream=1, path={path})");

        // ========== ③ 循环读响应帧，直到对端关闭 ==========
        var buffer = new byte[65536];
        int total = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            while (true)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(total), cts.Token);
                if (n == 0) break;   // 对端关闭
                total += n;

                // 解析所有已到齐的帧
                int pos = 0;
                while (pos + 9 <= total)
                {
                    int len = (buffer[pos] << 16) | (buffer[pos + 1] << 8) | buffer[pos + 2];
                    if (pos + 9 + len > total) break;   // 帧还没收全

                    byte type = buffer[pos + 3];
                    byte flags = buffer[pos + 4];
                    uint sid = ((uint)buffer[pos + 5] << 24) | ((uint)buffer[pos + 6] << 16)
                              | ((uint)buffer[pos + 7] << 8) | buffer[pos + 8];
                    sid &= 0x7FFFFFFF;

                    string name = type switch
                    {
                        0x00 => "DATA",
                        0x01 => "HEADERS",
                        0x04 => "SETTINGS",
                        0x06 => "PING",
                        0x07 => "GOAWAY",
                        _ => $"TYPE_{type:X2}",
                    };

                    Console.WriteLine($"← {name,-8} len={len,-4} flags=0x{flags:X2} stream={sid}");

                    if (type == 0x00 && len > 0)
                    {
                        // DATA 帧载荷就是响应 Body
                        var body = Encoding.UTF8.GetString(buffer, pos + 9, len);
                        Console.WriteLine($"  Body: {body}");
                    }

                    pos += 9 + len;
                }

                // 已消费的帧挪走，不完整的帧保留
                if (pos > 0)
                {
                    Buffer.BlockCopy(buffer, pos, buffer, 0, total - pos);
                    total -= pos;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("  (读取超时，连接可能已结束)");
        }
    }

    // 构造一个 9 字节帧头 + 载荷
    private static byte[] BuildFrame(byte type, byte flags, uint streamId, byte[] payload)
    {
        int len = payload.Length;
        var buf = new byte[9 + len];
        buf[0] = (byte)((len >> 16) & 0xFF);
        buf[1] = (byte)((len >> 8) & 0xFF);
        buf[2] = (byte)(len & 0xFF);
        buf[3] = type;
        buf[4] = flags;
        buf[5] = (byte)((streamId >> 24) & 0x7F);   // R 位必须为 0
        buf[6] = (byte)((streamId >> 16) & 0xFF);
        buf[7] = (byte)((streamId >> 8) & 0xFF);
        buf[8] = (byte)(streamId & 0xFF);
        if (len > 0) Buffer.BlockCopy(payload, 0, buf, 9, len);
        return buf;
    }
}
