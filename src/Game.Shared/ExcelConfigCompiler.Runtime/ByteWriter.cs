using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace ExcelConfigCompiler.Runtime
{
    /// <summary>
    /// 导表阶段用的二进制写入器。内部使用 ArrayBufferWriter&lt;byte&gt;，最终 ToArray() 一次。
    /// 与 ByteReader 对称，数值全部 Little Endian。
    /// float/double 使用位转换，兼容 Unity 较旧的 BCL（无 WriteSingleLittleEndian）。
    /// </summary>
    public sealed class ByteWriter
    {
        private readonly ArrayBufferWriter<byte> _buffer = new ArrayBufferWriter<byte>(4096);

        public int WrittenCount => _buffer.WrittenCount;

        public void WriteByte(byte v)
        {
            var span = _buffer.GetSpan(1);
            span[0] = v;
            _buffer.Advance(1);
        }

        public void WriteSByte(sbyte v) => WriteByte((byte)v);

        public void WriteInt16(short v)
        {
            var span = _buffer.GetSpan(2);
            BinaryPrimitives.WriteInt16LittleEndian(span, v);
            _buffer.Advance(2);
        }

        public void WriteUInt16(ushort v)
        {
            var span = _buffer.GetSpan(2);
            BinaryPrimitives.WriteUInt16LittleEndian(span, v);
            _buffer.Advance(2);
        }

        public void WriteInt32(int v)
        {
            var span = _buffer.GetSpan(4);
            BinaryPrimitives.WriteInt32LittleEndian(span, v);
            _buffer.Advance(4);
        }

        public void WriteUInt32(uint v)
        {
            var span = _buffer.GetSpan(4);
            BinaryPrimitives.WriteUInt32LittleEndian(span, v);
            _buffer.Advance(4);
        }

        public void WriteInt64(long v)
        {
            var span = _buffer.GetSpan(8);
            BinaryPrimitives.WriteInt64LittleEndian(span, v);
            _buffer.Advance(8);
        }

        public void WriteUInt64(ulong v)
        {
            var span = _buffer.GetSpan(8);
            BinaryPrimitives.WriteUInt64LittleEndian(span, v);
            _buffer.Advance(8);
        }

        public void WriteSingle(float v)
        {
            var conv = new FloatUInt { FloatValue = v };
            WriteUInt32(conv.UIntValue);
        }

        public void WriteDouble(double v)
        {
            var conv = new DoubleULong { DoubleValue = v };
            WriteUInt64(conv.ULongValue);
        }

        public void WriteBoolean(bool v) => WriteByte(v ? (byte)1 : (byte)0);

        /// <summary>null 写 -1，空字符串写 0，否则写 UTF8 字节长度 + 内容。</summary>
        public void WriteString(string v)
        {
            if (v is null)
            {
                WriteInt32(BinaryFormat.NullStringLength);
                return;
            }
            if (v.Length == 0)
            {
                WriteInt32(0);
                return;
            }

            int byteCount = Encoding.UTF8.GetByteCount(v);
            WriteInt32(byteCount);
            var span = _buffer.GetSpan(byteCount);
            Encoding.UTF8.GetBytes(v, span);
            _buffer.Advance(byteCount);
        }

        public void WriteByteArray(byte[] v)
        {
            if (v is null || v.Length == 0)
            {
                WriteInt32(0);
                return;
            }
            WriteInt32(v.Length);
            foreach (var b in v) WriteByte(b);
        }

        public void WriteInt32Array(int[] v)
        {
            if (v is null || v.Length == 0)
            {
                WriteInt32(0);
                return;
            }
            WriteInt32(v.Length);
            foreach (var i in v) WriteInt32(i);
        }

        public void WriteSingleArray(float[] v)
        {
            if (v is null || v.Length == 0)
            {
                WriteInt32(0);
                return;
            }
            WriteInt32(v.Length);
            foreach (var f in v) WriteSingle(f);
        }

        public void WriteStringArray(string[] v)
        {
            if (v is null || v.Length == 0)
            {
                WriteInt32(0);
                return;
            }
            WriteInt32(v.Length);
            foreach (var s in v) WriteString(s);
        }

        public byte[] ToArray() => _buffer.WrittenSpan.ToArray();

        public void WriteMagic(ReadOnlySpan<byte> magic)
        {
            var span = _buffer.GetSpan(magic.Length);
            magic.CopyTo(span);
            _buffer.Advance(magic.Length);
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct FloatUInt
        {
            [FieldOffset(0)] public float FloatValue;
            [FieldOffset(0)] public uint UIntValue;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct DoubleULong
        {
            [FieldOffset(0)] public double DoubleValue;
            [FieldOffset(0)] public ulong ULongValue;
        }
    }
}
