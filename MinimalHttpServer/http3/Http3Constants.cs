// HTTP/3 通用常量
public static class Http3Constants
{
    // HTTP/3 ALPN — RFC 9114 规定正式版是 "h3"（QUIC 握手时靠它协商）
    public const string AlpnHttp3 = "h3";

    // HTTP/2 ALPN（对照用）
    public const string AlpnHttp2 = "h2";

    // 一次读取的上限，避免单 Stream 无限堆积
    public const int MaxHeaderBuffer = 8192;
}
