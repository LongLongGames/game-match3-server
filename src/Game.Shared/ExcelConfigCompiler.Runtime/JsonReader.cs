using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ExcelConfigCompiler.Runtime
{
    /// <summary>
    /// 极简 forward-only JSON 读取器。ref struct，零反射。
    /// 只支持本工具导表产出的子集：object / array / number / string / bool / null。
    /// 不做完整 RFC 校验；保证与 JsonGenerator 对称即可。
    /// AOT / IL2CPP 安全，不依赖 System.Text.Json / Newtonsoft / JsonUtility。
    /// </summary>
    public ref struct JsonReader
    {
        private readonly ReadOnlySpan<char> _json;
        private int _pos;

        public JsonReader(ReadOnlySpan<char> json)
        {
            _json = json;
            _pos = 0;
        }

        public JsonReader(string json) : this((json ?? string.Empty).AsSpan()) { }

        public int Position => _pos;

        public void SkipWhitespace()
        {
            while (_pos < _json.Length)
            {
                char c = _json[_pos];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') _pos++;
                else break;
            }
        }

        public char Peek()
        {
            SkipWhitespace();
            return _pos < _json.Length ? _json[_pos] : '\0';
        }

        public void Expect(char c)
        {
            SkipWhitespace();
            if (_pos >= _json.Length || _json[_pos] != c)
                throw new InvalidOperationException($"JSON 期望 '{c}'，实际 '{Peek()}' @ {_pos}");
            _pos++;
        }

        public bool TryExpect(char c)
        {
            SkipWhitespace();
            if (_pos < _json.Length && _json[_pos] == c)
            {
                _pos++;
                return true;
            }
            return false;
        }

        public string ReadPropertyName()
        {
            Expect('"');
            int start = _pos;
            while (_pos < _json.Length && _json[_pos] != '"')
            {
                if (_json[_pos] == '\\') _pos += 2;
                else _pos++;
            }
            var name = Unescape(_json.Slice(start, _pos - start));
            Expect('"');
            Expect(':');
            return name;
        }

        public void SkipValue()
        {
            SkipWhitespace();
            char c = Peek();
            if (c == '"') { ReadString(); return; }
            if (c == '{') { SkipObject(); return; }
            if (c == '[') { SkipArray(); return; }
            if (c == 't' || c == 'f') { ReadBoolean(); return; }
            if (c == 'n') { ReadNull(); return; }
            while (_pos < _json.Length && "-+0123456789.eE".IndexOf(_json[_pos]) >= 0)
                _pos++;
        }

        private void SkipObject()
        {
            Expect('{');
            if (TryExpect('}')) return;
            while (true)
            {
                ReadPropertyName();
                SkipValue();
                if (TryExpect('}')) break;
                Expect(',');
            }
        }

        private void SkipArray()
        {
            Expect('[');
            if (TryExpect(']')) return;
            while (true)
            {
                SkipValue();
                if (TryExpect(']')) break;
                Expect(',');
            }
        }

        public void ReadNull()
        {
            SkipWhitespace();
            if (_pos + 4 <= _json.Length && _json.Slice(_pos, 4).SequenceEqual("null".AsSpan()))
                _pos += 4;
            else
                throw new InvalidOperationException($"期望 null @ {_pos}");
        }

        public bool IsNull()
        {
            SkipWhitespace();
            return _pos + 4 <= _json.Length && _json.Slice(_pos, 4).SequenceEqual("null".AsSpan());
        }

        public bool ReadBoolean()
        {
            SkipWhitespace();
            if (_pos + 4 <= _json.Length && _json.Slice(_pos, 4).SequenceEqual("true".AsSpan()))
            {
                _pos += 4;
                return true;
            }
            if (_pos + 5 <= _json.Length && _json.Slice(_pos, 5).SequenceEqual("false".AsSpan()))
            {
                _pos += 5;
                return false;
            }
            throw new InvalidOperationException($"期望 bool @ {_pos}");
        }

        public string ReadString()
        {
            SkipWhitespace();
            if (IsNull())
            {
                ReadNull();
                return null;
            }
            Expect('"');
            int start = _pos;
            while (_pos < _json.Length && _json[_pos] != '"')
            {
                if (_json[_pos] == '\\') _pos += 2;
                else _pos++;
            }
            var s = Unescape(_json.Slice(start, _pos - start));
            Expect('"');
            return s;
        }

        public long ReadInt64()
        {
            SkipWhitespace();
            int start = _pos;
            if (_pos < _json.Length && (_json[_pos] == '-' || _json[_pos] == '+')) _pos++;
            while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;
            if (_pos == start || (_pos == start + 1 && !char.IsDigit(_json[start])))
                throw new InvalidOperationException($"期望整数 @ {_pos}");
            // Unity / 较旧 BCL：Span 重载为 (span, NumberStyles, IFormatProvider)
            return long.Parse(
                _json.Slice(start, _pos - start),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture);
        }

        public int ReadInt32() => (int)ReadInt64();
        public byte ReadByte() => (byte)ReadInt64();
        public sbyte ReadSByte() => (sbyte)ReadInt64();
        public short ReadInt16() => (short)ReadInt64();
        public ushort ReadUInt16() => (ushort)ReadInt64();
        public uint ReadUInt32() => (uint)ReadInt64();
        public ulong ReadUInt64() => (ulong)ReadInt64();

        public double ReadDouble()
        {
            SkipWhitespace();
            int start = _pos;
            while (_pos < _json.Length && "-+0123456789.eE".IndexOf(_json[_pos]) >= 0)
                _pos++;
            if (_pos == start)
                throw new InvalidOperationException($"期望数字 @ {_pos}");
            return double.Parse(
                _json.Slice(start, _pos - start),
                NumberStyles.Float,
                CultureInfo.InvariantCulture);
        }

        public float ReadSingle() => (float)ReadDouble();

        public int[] ReadInt32Array()
        {
            if (IsNull()) { ReadNull(); return null; }
            Expect('[');
            if (TryExpect(']')) return Array.Empty<int>();
            var list = new List<int>(8);
            while (true)
            {
                list.Add(ReadInt32());
                if (TryExpect(']')) break;
                Expect(',');
            }
            return list.ToArray();
        }

        public float[] ReadSingleArray()
        {
            if (IsNull()) { ReadNull(); return null; }
            Expect('[');
            if (TryExpect(']')) return Array.Empty<float>();
            var list = new List<float>(8);
            while (true)
            {
                list.Add(ReadSingle());
                if (TryExpect(']')) break;
                Expect(',');
            }
            return list.ToArray();
        }

        public string[] ReadStringArray()
        {
            if (IsNull()) { ReadNull(); return null; }
            Expect('[');
            if (TryExpect(']')) return Array.Empty<string>();
            var list = new List<string>(8);
            while (true)
            {
                list.Add(ReadString());
                if (TryExpect(']')) break;
                Expect(',');
            }
            return list.ToArray();
        }

        public byte[] ReadByteArray()
        {
            if (IsNull()) { ReadNull(); return null; }
            Expect('[');
            if (TryExpect(']')) return Array.Empty<byte>();
            var list = new List<byte>(8);
            while (true)
            {
                list.Add(ReadByte());
                if (TryExpect(']')) break;
                Expect(',');
            }
            return list.ToArray();
        }

        private static string Unescape(ReadOnlySpan<char> s)
        {
            if (s.IndexOf('\\') < 0) return s.ToString();
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    sb.Append(n switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '"' => '"',
                        '\\' => '\\',
                        '/' => '/',
                        'b' => '\b',
                        'f' => '\f',
                        _ => n
                    });
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }
}
