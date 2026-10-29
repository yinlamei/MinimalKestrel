using System.Text;

// 简化版 HPACK 编码器 —— 只支持 Literal Header Field Without Indexing (前缀 0x00)
// 这正好对应 MinimalHttpServer 里 Http2Parser.DecodeHeadersFrame 的简化实现。
//
// 真正的 HPACK (RFC 7541) 还包括：静态表索引 (0x80)、增量索引 (0x40)、Huffman 压缩；
// 那些需要完整 HPackDecoder，教学版故意不做。

public static class SimpleHpackEncoder
{
    /// <summary>
    /// 把一组头部编码成 HPACK 字节流（简化版：每个头部 = 0x00 + 名字 + 值）。
    /// 顺序必须是：伪头部（:method / :path / :scheme / :authority）在前，普通头部在后。
    /// </summary>
    public static byte[] EncodeHeaders(IEnumerable<KeyValuePair<string, string>> headers)
    {
        using var ms = new MemoryStream();
        foreach (var h in headers)
        {
            ms.WriteByte(0x00);                                   // Literal Without Indexing
            WriteString(ms, Encoding.ASCII.GetBytes(h.Key));      // 名字
            WriteString(ms, Encoding.ASCII.GetBytes(h.Value));    // 值
        }
        return ms.ToArray();
    }

    // 字符串长度编码：1 字节（<127）或 3 字节（0x7F + 2 字节大端）
    private static void WriteString(MemoryStream ms, byte[] bytes)
    {
        int len = bytes.Length;
        if (len < 127)
        {
            ms.WriteByte((byte)len);
        }
        else if (len < 16384)
        {
            ms.WriteByte(0x7F);
            ms.WriteByte((byte)((len >> 8) & 0xFF));
            ms.WriteByte((byte)(len & 0xFF));
        }
        else
        {
            throw new ArgumentException("Header too long");
        }
        ms.Write(bytes);
    }
}
