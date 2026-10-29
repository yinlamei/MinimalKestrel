// HTTP/2 通用常量 — RFC 7540 Section 3.5 / Section 6.5.2
public static class Http2Constants
{
    // 连接前言 — 客户端必须先发这个来表明自己支持 HTTP/2（24 字节）
    public static readonly byte[] ConnectionPreface =
        { (byte)'P', (byte)'R', (byte)'I', (byte)' ', (byte)'*',
          (byte)' ', (byte)'H', (byte)'T', (byte)'T', (byte)'P', (byte)'/',
          (byte)'2', (byte)'.', (byte)'0', (byte)'\r', (byte)'\n',
          (byte)'\r', (byte)'\n', (byte)'S', (byte)'M',
          (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' };

    // 帧最大载荷默认 16KB（Kestrel 默认 16384）
    public const int MaxFramePayload = 16384;

    // SETTINGS 默认 INITIAL_WINDOW_SIZE
    public const int DefaultInitialWindowSize = 65535;

    // SETTINGS 默认 MAX_HEADER_LIST_SIZE
    public const int DefaultMaxHeaderListSize = 8192;
}
