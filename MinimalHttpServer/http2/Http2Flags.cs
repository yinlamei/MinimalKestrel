// HTTP/2 帧标志位 — RFC 7540 Section 6
public static class Http2Flags
{
    public const byte END_STREAM  = 0x01;
    public const byte ACK         = 0x01; // SETTINGS / PING
    public const byte END_HEADERS = 0x04; // HEADERS / CONTINUATION
    public const byte PADDED      = 0x08;
    public const byte PRIORITY    = 0x20;
}
