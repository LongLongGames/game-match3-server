using System;
using System.Globalization;
using System.Text;

namespace ExcelConfigCompiler.Runtime
{
    /// <summary>
    /// 极简 JSON 写出器。与 JsonReader / JsonGenerator 对称。
    /// 仅用于编译期导表；运行时一般不需要。
    /// </summary>
    public sealed class JsonWriter
    {
        private readonly StringBuilder _sb = new StringBuilder(256);

        public void Clear() => _sb.Clear();

        public override string ToString() => _sb.ToString();

        public void WriteRaw(string s) => _sb.Append(s);

        public void WriteNull() => _sb.Append("null");

        public void WriteBoolean(bool v) => _sb.Append(v ? "true" : "false");

        public void WriteNumber(long v) => _sb.Append(v.ToString(CultureInfo.InvariantCulture));

        public void WriteNumber(double v) => _sb.Append(v.ToString("G17", CultureInfo.InvariantCulture));

        public void WriteString(string s)
        {
            if (s == null) { WriteNull(); return; }
            _sb.Append('"');
            EscapeTo(s);
            _sb.Append('"');
        }

        public void WritePropertyName(string name)
        {
            WriteString(name);
            _sb.Append(':');
        }

        public void BeginObject() => _sb.Append('{');
        public void EndObject() => _sb.Append('}');
        public void BeginArray() => _sb.Append('[');
        public void EndArray() => _sb.Append(']');
        public void Comma() => _sb.Append(',');

        private void EscapeTo(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    case '\b': _sb.Append("\\b"); break;
                    case '\f': _sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            _sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            _sb.Append(c);
                        break;
                }
            }
        }
    }
}
