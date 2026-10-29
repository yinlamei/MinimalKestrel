// QUIC Stream 类型（HTTP/3 专用）— RFC 9114 Section 6.2
public enum Http3StreamType : long
{
    CONTROL = 0x00,   // 双向：SETTINGS / GOAWAY / MAX_PUSH_ID
    PUSH    = 0x01,   // 单向：服务器推送
    ENCODER = 0x02,   // 单向：QPACK Encoder（动态表同步）
    DECODER = 0x03,   // 单向：QPACK Decoder（动态表同步）
}
