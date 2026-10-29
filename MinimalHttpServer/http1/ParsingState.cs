// HTTP/1.1 解析状态机的状态枚举
// 因为"一个类一个文件"的约定，单独从 Http1Parser 中拆出来

public enum ParsingState
{
    RequestLine,          // 正在读 METHOD PATH HTTP/1.1
    SkipRequestLF,        // 请求行的 \r 之后等 \n
    HeaderName,           // 正在读 Header Name（刚进 Header 或刚存完上一个）
    HeaderValue,          // 正在读 Header Value
    SkipHeaderLF,         // 刚读了 Header Value 末尾的 \r，等 \n
    SkipEndingLF,         // \r\n\r\n 的第二个 \r 之后等第二个 \n
    Complete              // 头部结束
}
