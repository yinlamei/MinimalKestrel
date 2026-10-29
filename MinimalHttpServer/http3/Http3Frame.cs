// HTTP/3 帧 — RFC 9114 Section 7.2（varint type + varint length + payload）
public record Http3Frame(Http3FrameType Type, byte[] Payload);
