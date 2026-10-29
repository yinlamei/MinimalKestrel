// HTTP/3 SETTINGS 参数 ID — RFC 9114 Section 7.2.4
public enum Http3SettingId : long
{
    QPACK_MAX_TABLE_CAPACITY = 0x01,
    MAX_FIELD_SECTION_SIZE   = 0x06,
    WEBTransport             = 0x0B,
    ENABLE_CONNECT           = 0x0C,
}
