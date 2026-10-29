// HTTP/2 帧 — RFC 7540 Section 11（9 字节帧头 + 载荷）
public record struct Http2Frame(
    int Length,          // 24 位：载荷长度
    Http2FrameType Type, // 8 位：帧类型
    byte Flags,          // 8 位：标志
    uint StreamId,       // 31 位（R 位清零）
    byte[] Payload       // 帧载荷
);
