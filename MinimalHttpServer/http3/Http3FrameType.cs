// HTTP/3 帧类型 — RFC 9114 Section 7.2
// Kestrel 源码：Http3FrameType.cs (src/Servers/Kestrel/Http/Shared)
public enum Http3FrameType : long   // QUIC varint，所以用 long
{
    DATA        = 0x00,
    HEADERS     = 0x01,
    CANCEL_PUSH = 0x03,
    SETTINGS    = 0x04,
    PUSH_PROMISE= 0x05,
    GOAWAY      = 0x07,
    MAX_PUSH_ID = 0x0D,
}
