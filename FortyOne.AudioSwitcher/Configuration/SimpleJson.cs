// ============================================================
// SimpleJson.cs - 手写的扁平 JSON 读写器
// 功能：在字符串与 Dictionary 之间互转「键和值都是字符串」的单层 JSON 对象，供设置文件使用
// 设计理念：设置只有一层键值，为此拉入第三方 JSON 库不划算；这里逐字符扫描、不建语法树，遇错即抛
// 编译：build.cmd（详见 编译说明.md）
// ============================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FortyOne.AudioSwitcher.Configuration
{
    internal static class SimpleJson
    {
        // ===================== 解析 =====================
        // 解析单层 JSON 对象  参数：JSON 文本  返回：键值字典；文本为 null 或字面量 null 时返回 null
        // 顶层只认 {...} 对象（整个文本为 null 时例外）：配置里没有嵌套结构与数组，
        // 键必须是字符串，值可带引号或写成裸记号；解析失败抛 FormatException，由调用方统一处理
        public static Dictionary<string, string> ParseFlatObject(string text)
        {
            // null 表示「没有配置文件内容」，属于正常分支，交给调用方保留现有设置
            if (text == null)
                return null;

            var p = new Reader(text);
            p.SkipWhite();
            if (p.End)
                return null;

            // 顶层允许写 null：旧版本或外部工具可能写出这种内容，语义上等同于「没有配置」
            if (p.Peek == 'n')
            {
                var lit = p.ReadBareToken();
                if (string.Equals(lit, "null", StringComparison.Ordinal))
                    return null;
                throw new FormatException("不是一个 JSON 对象");
            }

            // 顶层必须是对象：设置文件是扁平的键值表，数组或标量都不是合法配置
            if (p.Peek != '{')
                throw new FormatException("不是一个 JSON 对象");

            p.Next();
            var map = new Dictionary<string, string>();
            p.SkipWhite();

            // 空对象 {} 单独走掉，省得进循环后还要额外判断一次
            if (!p.End && p.Peek == '}')
            {
                p.Next();
                return map;
            }

            while (true)
            {
                // === 读键 ===
                p.SkipWhite();
                var key = p.ReadString();
                p.SkipWhite();
                if (p.End || p.Peek != ':')
                    throw new FormatException("键后面缺少冒号");
                p.Next();
                p.SkipWhite();

                // === 读值 ===
                string value;
                // 带引号的值走转义解析；裸值中的 null 存成 null，数字与 true/false 原样转成字符串。
                // 这里故意不做类型推断：设置项除 null 外都以字符串存放，推断一次再转回去只会丢格式
                if (!p.End && p.Peek == '"')
                    value = p.ReadString();
                else
                {
                    var token = p.ReadBareToken();
                    if (string.Equals(token, "null", StringComparison.Ordinal))
                        value = null;
                    else
                        value = token;
                }

                map[key] = value;

                // === 分隔符与收尾 ===
                p.SkipWhite();
                if (p.End)
                    throw new FormatException("对象没有闭合");
                if (p.Peek == ',')
                {
                    p.Next();
                    continue;
                }
                if (p.Peek == '}')
                {
                    p.Next();
                    return map;
                }
                throw new FormatException("对象里出现了意外字符");
            }
        }

        // ===================== 序列化 =====================
        // 把字典写成单层 JSON 对象  参数：键值表、pretty 为 true 时分行缩进  返回：JSON 文本
        public static string Serialize(IDictionary<string, string> map, bool pretty)
        {
            // 与解析端对称：null 写成 JSON 字面量 null，回读时又还原成「没有配置」
            if (map == null)
                return "null";

            var sb = new StringBuilder();
            sb.Append('{');

            var first = true;
            foreach (var pair in map)
            {
                // 逗号写在元素前面：这样最后一项后面不会多出逗号，就不必回头删
                if (!first)
                    sb.Append(',');
                first = false;

                // 缩进固定三个空格，够人眼读文件即可，不追求标准格式化器的对齐宽度
                if (pretty)
                    sb.Append("\r\n   ");
                sb.Append(Encode(pair.Key));
                sb.Append(pretty ? " : " : ":");
                sb.Append(pair.Value == null ? "null" : Encode(pair.Value));
            }

            if (pretty && !first)
                sb.Append("\r\n");
            sb.Append('}');
            return sb.ToString();
        }

        // ===================== 字符串转义 =====================
        // 把原始字符串编码成带引号的 JSON 字符串  参数：原文  返回：含首尾引号的转义结果
        private static string Encode(string s)
        {
            // 先按长度 + 8 预留容量，常见键值远小于此，减少 StringBuilder 中途扩容
            var sb = new StringBuilder(s.Length + 8);
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;   // 引号不转义会提前闭合字符串
                    case '\\': sb.Append("\\\\"); break;  // 反斜杠是转义引导符，自身先转义
                    case '\b': sb.Append("\\b"); break;   // 退格，没有可打印形式
                    case '\f': sb.Append("\\f"); break;   // 换页
                    case '\n': sb.Append("\\n"); break;   // 换行，配置文件里不写裸换行
                    case '\r': sb.Append("\\r"); break;   // 回车
                    case '\t': sb.Append("\\t"); break;   // 制表符
                    default:
                        // 其余控制字符统一写成 \u00xx，保证输出在任何 JSON 解析器里都合法
                        if (c < ' ')
                            sb.Append("\\u").Append(((int) c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        // ===================== 逐字符读取器 =====================
        private sealed class Reader
        {
            private readonly string _s;  // 已整体读入内存的文本
            private int _i;              // 游标：指向下一个待读字符

            // 绑定文本并把游标停在开头  参数：完整 JSON 文本  返回：无
            // 一次性把文件读成字符串再用下标扫描，不必处理编码探测与流缓冲，解析几 KB 的配置最省事
            public Reader(string s)
            {
                _s = s;
                _i = 0;
            }

            // 游标是否已到末尾：循环里先问它再取 Peek，避免越界
            public bool End
            {
                get { return _i >= _s.Length; }
            }

            // 看一眼当前字符但不前进  参数：无  返回：当前字符；越界时返回 '\0'
            public char Peek
            {
                get { return _i < _s.Length ? _s[_i] : '\0'; }
            }

            // 前进一个字符；调用方必须先用 Peek 确认过当前字符的语义
            public void Next()
            {
                _i++;
            }

            // 跳过所有空白（空格、制表、换行）  参数：无  返回：无
            // JSON 允许在任意记号之间出现空白，所以每次读记号前都要先跳过
            public void SkipWhite()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
                    _i++;
            }

            // 读一个带引号的 JSON 字符串  参数：无  返回：反转义后的内容
            // 状态机只靠游标 _i 前进，无回退：
            //   1. 引号 → 字符串结束，返回累积结果
            //   2. 反斜杠 → 读下一个字符查转义表翻译
            //   3. 其他 → 普通字符，直接追加
            public string ReadString()
            {
                if (End || _s[_i] != '"')
                    throw new FormatException("期望一个字符串（位置 " + _i + "）");
                _i++;

                var sb = new StringBuilder();
                while (true)
                {
                    // 先判越界：未闭合的字符串不能让 _s[_i] 抛 IndexOutOfRange
                    if (End)
                        throw new FormatException("字符串没有闭合");

                    var c = _s[_i++];
                    if (c == '"')
                        return sb.ToString();

                    // 普通字符是最常见的一支，先处理掉再管转义，避免每字符都进 switch
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }

                    // 反斜杠后面必须还有字符，否则说明转义序列被截断
                    if (End)
                        throw new FormatException("转义符后没有字符");
                    var e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;  // \/ 是 JSON 为内嵌 HTML 保留的写法
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            // \uXXXX 定长四字符，剩余长度不够说明文件被截断
                            if (_i + 4 > _s.Length)
                                throw new FormatException("\\u 转义不完整");
                            var hex = _s.Substring(_i, 4);
                            sb.Append((char) int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default:
                            // 不认识的转义宁可报错，也不要静默丢掉反斜杠把内容读歪
                            throw new FormatException("不认识的转义 \\" + e);
                    }
                }
            }

            // 读一段不带引号的裸记号  参数：无  返回：以逗号、右花括号、右方括号或空白为界的原文
            public string ReadBareToken()
            {
                var start = _i;
                while (_i < _s.Length && _s[_i] != ',' && _s[_i] != '}' && _s[_i] != ']' && !char.IsWhiteSpace(_s[_i]))
                    _i++;
                return _s.Substring(start, _i - start);
            }
        }
    }
}
