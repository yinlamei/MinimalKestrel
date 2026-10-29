using System.Text;

// HTTP/1.1 状态机解析器 —— 简化版
// Kestrel 源码：src/Servers/Kestrel/Http/Shared/HttpParser.cs（~2000 行）
// 支持：请求行 + 多个 Header + \r\n\r\n 头部结束
//
// 关键点：HttpConnection 每次只把"新到的那一小段字节"喂进来，
// 所以正在扫描的 token（请求行 / Header 名 / Header 值）必须自己跨 chunk 累积，
// 不能靠"本次 data 的偏移"来切片 —— 否则 token 被切片拆开就会越界。

public class Http1Parser
{
    internal ParsingState _state = ParsingState.RequestLine;
    public ParsingState State => _state;

    // 解析输出
    public string Method { get; private set; } = "";
    public string Path { get; private set; } = "";
    public string Version { get; private set; } = "";
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int HeaderEnd { get; private set; } = -1;   // Body 在累计字节里的**绝对**起始位置
    public bool IsComplete => _state == ParsingState.Complete;

    // 当前 token 的累积缓冲（跨 chunk）
    private readonly StringBuilder _segment = new();
    private string _currentHeaderName = "";
    private bool _skipLeadingSpace;   // Header 值开头的空格要吃掉
    private int _bytesProcessed;      // 之前各 chunk 累计处理过的字节数

    /// <summary>
    /// 喂入增量字节。返回 true 表示头部已完整。
    /// </summary>
    public bool Parse(ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];

            switch (_state)
            {
                case ParsingState.RequestLine:
                    if (b == '\r')
                    {
                        ParseRequestLine(_segment.ToString());
                        _segment.Clear();
                        _state = ParsingState.SkipRequestLF;
                    }
                    else
                    {
                        _segment.Append((char)b);
                    }
                    break;

                case ParsingState.SkipRequestLF:
                    if (b == '\n')
                    {
                        _segment.Clear();
                        _state = ParsingState.HeaderName;
                    }
                    break;

                case ParsingState.HeaderName:
                    if (b == ':')
                    {
                        _currentHeaderName = _segment.ToString().Trim();
                        _segment.Clear();
                        _skipLeadingSpace = true;
                        _state = ParsingState.HeaderValue;
                    }
                    else if (b == '\r')
                    {
                        // 空行 —— 头部结束
                        _segment.Clear();
                        _state = ParsingState.SkipEndingLF;
                    }
                    else
                    {
                        _segment.Append((char)b);
                    }
                    break;

                case ParsingState.HeaderValue:
                    if (_skipLeadingSpace && (b == ' ' || b == '\t'))
                        break;   // 值开头的空格/制表符跳过
                    _skipLeadingSpace = false;

                    if (b == '\r')
                    {
                        Headers[_currentHeaderName] = _segment.ToString().Trim();
                        _segment.Clear();
                        _state = ParsingState.SkipHeaderLF;
                    }
                    else
                    {
                        _segment.Append((char)b);
                    }
                    break;

                case ParsingState.SkipHeaderLF:
                    if (b == '\n')
                    {
                        _segment.Clear();
                        _state = ParsingState.HeaderName;
                    }
                    break;

                case ParsingState.SkipEndingLF:
                    if (b == '\n')
                    {
                        // 绝对位置 = 之前各 chunk 的字节数 + 本次已扫到的下标 + 1（LF 之后）
                        HeaderEnd = _bytesProcessed + i + 1;
                        _state = ParsingState.Complete;
                        return true;
                    }
                    break;
            }
        }

        _bytesProcessed += data.Length;
        return IsComplete;
    }

    private void ParseRequestLine(string line)
    {
        int sp1 = -1, sp2 = -1;
        for (int j = 0; j < line.Length; j++)
        {
            if (line[j] == ' ')
            {
                if (sp1 < 0) sp1 = j;
                else { sp2 = j; break; }
            }
        }
        if (sp1 < 0 || sp2 < 0)
            throw new FormatException("Invalid request line, expected: METHOD /path HTTP/1.1");

        Method = line[..sp1];
        Path = line[(sp1 + 1)..sp2];
        Version = line[(sp2 + 1)..];
    }

    public void Reset()
    {
        _state = ParsingState.RequestLine;
        Method = "";
        Path = "";
        Version = "";
        Headers.Clear();
        HeaderEnd = -1;
        _currentHeaderName = "";
        _segment.Clear();
        _skipLeadingSpace = false;
        _bytesProcessed = 0;
    }
}
