// HTTP/2 帧类型常量 — RFC 7540 Section 11.2
// Kestrel 源码：Http2FrameType.cs (src/Servers/Kestrel/Http/Shared)

public enum Http2FrameType : byte
{
    DATA          = 0x0,
    HEADERS       = 0x1,
    PRIORITY      = 0x2,
    RST_STREAM    = 0x3,
    SETTINGS      = 0x4,
    PUSH_PROMISE  = 0x5,
    PING          = 0x6,
    GOAWAY        = 0x7,
    WINDOW_UPDATE = 0x8,
    CONTINUATION  = 0x9
}
