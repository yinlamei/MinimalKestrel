using System.Text;

// HTTP/2 帧解析辅助
// Kestrel 源码：Http2FrameReader.cs / Http2FrameWriter.cs / HPackDecoder.cs / HPackEncoder.cs
// 简化版：不做 HPACK Huffman 压缩（Literal Header Field 原始 ASCII）
// 真正的 Kestrel 用完整 HPACK：静态表 + 动态表 + Huffman + 增量索引

public class Http2Parser
{
    // ============ 帧头解析 ============

    // HTTP/2 帧头 = 9 字节
    // +-----------------------------------------------+
    // |                 Length (24)                   |  ← 3 字节
    // +---------------+---------------+---------------+
    // |   Type (8)    |   Flags (8)                |  ← 2 字节
    // +-+-------------+---------------+-------------------+
    // |R|                 Stream Identifier (31)         |  ← 4 字节
    // +-+-------------------------------------------------+
    public static bool TryParseFrameHeader(ReadOnlySpan<byte> data, out int length, out Http2FrameType type, out byte flags, out uint streamId)
    {
        length = 0; type = 0; flags = 0; streamId = 0;
        if (data.Length < 9) return false;

        length = (data[0] << 16) | (data[1] << 8) | data[2];
        type = (Http2FrameType)data[3];
        flags = data[4];
        streamId = ((uint)data[5] << 24) | ((uint)data[6] << 16) |
                   ((uint)data[7] << 8)  | (uint)data[8];
        streamId &= 0x7FFFFFFF; // R 位清零
        return true;
    }

    // ============ 帧发送辅助 ============

    public static byte[] EncodeFrame(Http2FrameType type, byte flags, uint streamId, byte[] payload)
    {
        int length = payload.Length;
        var buf = new byte[9 + length];

        buf[0] = (byte)((length >> 16) & 0xFF);
        buf[1] = (byte)((length >> 8) & 0xFF);
        buf[2] = (byte)(length & 0xFF);

        buf[3] = (byte)type;
        buf[4] = flags;
        buf[5] = (byte)((streamId >> 24) & 0x7F);
        buf[6] = (byte)((streamId >> 16) & 0xFF);
        buf[7] = (byte)((streamId >> 8) & 0xFF);
        buf[8] = (byte)(streamId & 0xFF);

        Buffer.BlockCopy(payload, 0, buf, 9, length);
        return buf;
    }

    public static byte[] BuildSettingsFrame(List<Http2Setting>? settings = null, bool ack = false)
    {
        var payload = new MemoryStream();
        if (!ack && settings != null)
        {
            foreach (var s in settings)
            {
                payload.Write(BitConverter.GetBytes((ushort)s.Id));
                payload.Write(BitConverter.GetBytes(s.Value));
            }
        }
        byte flags = ack ? Http2Flags.ACK : (byte)0;
        return EncodeFrame(Http2FrameType.SETTINGS, flags, 0, payload.ToArray());
    }

    public static byte[] BuildSettingsAckFrame() => BuildSettingsFrame(null, ack: true);

    public static byte[] BuildRstStreamFrame(uint streamId, uint errorCode)
    {
        var payload = new byte[4];
        BitConverter.TryWriteBytes(payload, errorCode);
        return EncodeFrame(Http2FrameType.RST_STREAM, 0, streamId, payload);
    }

    public static byte[] BuildGoAwayFrame(uint lastStreamId, uint errorCode)
    {
        var payload = new byte[8];
        payload[0] = (byte)((lastStreamId >> 24) & 0x7F);
        payload[1] = (byte)((lastStreamId >> 16) & 0xFF);
        payload[2] = (byte)((lastStreamId >> 8) & 0xFF);
        payload[3] = (byte)(lastStreamId & 0xFF);
        payload[4] = (byte)((errorCode >> 24) & 0xFF);
        payload[5] = (byte)((errorCode >> 16) & 0xFF);
        payload[6] = (byte)((errorCode >> 8) & 0xFF);
        payload[7] = (byte)(errorCode & 0xFF);
        return EncodeFrame(Http2FrameType.GOAWAY, 0, 0, payload);
    }

    // ============ HEADERS 帧 — 简化 HPACK ============
    // Kestrel 用完整 HPACK：静态表 + 动态表 + Huffman 压缩
    // 我们用 Literal Header Field (Without Indexing) = 0x00 + 长度 + 内容

    public static byte[] EncodeHeadersFramePayload(Dictionary<string, string> headers)
    {
        var ms = new MemoryStream();
        foreach (var h in headers)
        {
            ms.WriteByte(0x00); // Literal Header Field Without Indexing
            WriteHpackString(ms, Encoding.ASCII.GetBytes(h.Key));
            WriteHpackString(ms, Encoding.ASCII.GetBytes(h.Value));
        }
        return ms.ToArray();
    }

    private static void WriteHpackString(MemoryStream ms, byte[] bytes)
    {
        int len = bytes.Length;
        if (len < 127)
            ms.WriteByte((byte)len);
        else if (len < 16384)
        {
            ms.WriteByte(127);
            ms.WriteByte((byte)((len >> 8) & 0xFF));
            ms.WriteByte((byte)(len & 0xFF));
        }
        else
            throw new ArgumentException("Header value too long");
        ms.Write(bytes);
    }

    public static Dictionary<string, string> DecodeHeadersFrame(byte[] payload)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        while (i < payload.Length)
        {
            byte prefix = payload[i++];
            if (prefix == 0x00)
            {
                var (name, nameLen) = ReadHpackString(payload, i); i += nameLen;
                var (val, valLen) = ReadHpackString(payload, i); i += valLen;
                result[name] = val;
            }
            else if ((prefix & 0x80) != 0)
            {
                // Indexed Header Field — Kestrel 查静态表，我们跳过
                i += 1;
            }
            else
            {
                // Literal with Incremental Indexing (0x40+) — 简化版跳过
                break;
            }
        }
        return result;
    }

    private static (string text, int bytesRead) ReadHpackString(byte[] buf, int start)
    {
        if (start >= buf.Length) return ("", 0);
        int len = buf[start] & 0x7F;
        int consumed = 1;
        int pos = start + 1;
        if (len == 127)
        {
            if (pos + 1 >= buf.Length) return ("", consumed);
            len = (buf[pos] << 8) | buf[pos + 1];
            consumed = 3;
            pos += 2;
        }
        if (pos + len > buf.Length)
            return ("", consumed + len);
        return (Encoding.ASCII.GetString(buf, pos, len), consumed + len);
    }
}
