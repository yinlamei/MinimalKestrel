using System.Text;

// HTTP/3 帧编解码 — RFC 9114
// Kestrel 源码：Http3FrameReader.cs / Http3FrameWriter.cs
// 核心差异：HTTP/3 帧头是 QUIC varint 编码，不是 HTTP/2 的固定 9 字节

public static class Http3Parser
{
    // ===== QUIC Varint 编码 — RFC 9000 Section 16
    // 0x00-0x3F: 1 字节 (0-63)
    // 0x40-0xBF: 2 字节 (64-16383)
    // 0xC0-0xFF: 4 字节 (16384-1073741823)
    // 0xFE-0xFF: 8 字节 (1073741824+)

    public static int EncodeVarint(long value, Span<byte> buf)
    {
        if (value < 64)
        {
            buf[0] = (byte)value;
            return 1;
        }
        if (value < 16384)
        {
            buf[0] = (byte)(0x40 | ((value >> 8) & 0x3F));
            buf[1] = (byte)(value & 0xFF);
            return 2;
        }
        if (value < 1073741824)
        {
            buf[0] = (byte)(0x80 | ((value >> 24) & 0x3F));
            buf[1] = (byte)((value >> 16) & 0xFF);
            buf[2] = (byte)((value >> 8) & 0xFF);
            buf[3] = (byte)(value & 0xFF);
            return 4;
        }
        buf[0] = (byte)(0xC0 | ((value >> 56) & 0x3F));
        buf[1] = (byte)((value >> 48) & 0xFF);
        buf[2] = (byte)((value >> 40) & 0xFF);
        buf[3] = (byte)((value >> 32) & 0xFF);
        buf[4] = (byte)((value >> 24) & 0xFF);
        buf[5] = (byte)((value >> 16) & 0xFF);
        buf[6] = (byte)((value >> 8) & 0xFF);
        buf[7] = (byte)(value & 0xFF);
        return 8;
    }

    public static (long value, int bytesRead) DecodeVarint(ReadOnlySpan<byte> buf)
    {
        if (buf.Length == 0) return (-1, 0);
        byte first = buf[0];
        int prefix = first >> 6;

        switch (prefix)
        {
            case 0: // 1 字节
                return (first, 1);

            case 1: // 2 字节
                if (buf.Length < 2) return (-1, 0);
                return (((long)(first & 0x3F) << 8) | buf[1], 2);

            case 2: // 4 字节
                if (buf.Length < 4) return (-1, 0);
                return (((long)(first & 0x3F) << 24) |
                        ((long)buf[1] << 16) |
                        ((long)buf[2] << 8) |
                        buf[3], 4);

            case 3: // 8 字节
                if (buf.Length < 8) return (-1, 0);
                return (((long)(first & 0x3F) << 56) |
                        ((long)buf[1] << 48) |
                        ((long)buf[2] << 40) |
                        ((long)buf[3] << 32) |
                        ((long)buf[4] << 24) |
                        ((long)buf[5] << 16) |
                        ((long)buf[6] << 8) |
                        buf[7], 8);
        }
        return (-1, 0);
    }

    // ===== 帧编码：type (varint) + length (varint) + payload =====

    public static byte[] EncodeFrame(Http3FrameType type, byte[] payload)
    {
        int typeLen = VarintByteLength((long)type);
        int lenLen = VarintByteLength(payload.Length);
        var buf = new byte[typeLen + lenLen + payload.Length];

        int pos = 0;
        pos += EncodeVarint((long)type, buf.AsSpan(pos));
        pos += EncodeVarint(payload.Length, buf.AsSpan(pos));
        Buffer.BlockCopy(payload, 0, buf, pos, payload.Length);

        return buf;
    }

    public static bool TryDecodeFrame(ReadOnlySpan<byte> data, out Http3Frame frame, out int consumed)
    {
        frame = default; consumed = 0;

        var (typeVal, typeBytes) = DecodeVarint(data);
        if (typeVal < 0) return false;

        var (lenVal, lenBytes) = DecodeVarint(data[typeBytes..]);
        if (lenVal < 0) return false;

        consumed = typeBytes + lenBytes + (int)lenVal;
        if (data.Length < consumed) return false; // 还没全到

        var payload = data.Slice(typeBytes + lenBytes, (int)lenVal).ToArray();
        frame = new Http3Frame((Http3FrameType)typeVal, payload);
        return true;
    }

    private static int VarintByteLength(long value)
    {
        if (value < 64) return 1;
        if (value < 16384) return 2;
        if (value < 1073741824) return 4;
        return 8;
    }

    // ===== SETTINGS 帧编解码 =====

    public static byte[] EncodeSettingsFrame(Dictionary<Http3SettingId, long>? settings = null)
    {
        // HTTP/3 SETTINGS = 一个 frame (type=SETTINGS) 包含多个 setting id+value varint 对
        var ms = new MemoryStream();
        if (settings != null)
        {
            foreach (var kv in settings)
            {
                Span<byte> tmp = stackalloc byte[8];
                int n = EncodeVarint((long)kv.Key, tmp);
                ms.Write(tmp.Slice(0, n));
                n = EncodeVarint(kv.Value, tmp);
                ms.Write(tmp.Slice(0, n));
            }
        }
        return EncodeFrame(Http3FrameType.SETTINGS, ms.ToArray());
    }

    // ===== 方便方法：发 DATA 帧 =====

    public static byte[] BuildDataFrame(byte[] data) => EncodeFrame(Http3FrameType.DATA, data);

    // ===== 方便方法：发 HEADERS 帧（HPACK，简化版同 HTTP/2） =====

    public static byte[] BuildHeadersFrame(Dictionary<string, string> headers)
    {
        var hpackPayload = Http2Parser.EncodeHeadersFramePayload(headers);
        return EncodeFrame(Http3FrameType.HEADERS, hpackPayload);
    }

    // ===== 方便方法：读一帧（带缓冲） =====

    public static List<Http3Frame> DecodeAllFrames(byte[] data)
    {
        var result = new List<Http3Frame>();
        int pos = 0;
        while (pos < data.Length)
        {
            if (!TryDecodeFrame(data.AsSpan(pos), out var frame, out var consumed))
                break;
            result.Add(frame);
            pos += consumed;
        }
        return result;
    }
}
