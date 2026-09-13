using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace ExcelConfigCompiler.Runtime
{
    /// <summary>
    /// 轻量二进制读取器。ref struct，包一层 ReadOnlySpan&lt;byte&gt;，不做反射/装箱。
    /// 数值类型读取路径：0 GC。string / array 读取会各产生一次必要的分配。
    /// float/double 使用位转换，兼容 Unity 较旧的 BCL（无 ReadSingleLittleEndian）。
    /// </summary>
    public ref struct ByteReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public ByteReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _offset = 0;
        }

        public int Position => _offset;
        public int Length => _data.Length;
        public int Remaining => _data.Length - _offset;

        private ReadOnlySpan<byte> Slice(int size)
        {
            if (_offset + size > _data.Length)
                throw new InvalidOperationException($"ByteReader 越界：需要 {size} 字节，剩余 {Remaining} 字节。");
            var span = _data.Slice(_offset, size);
            _offset += size;
            return span;
        }

        public bool ReadMagicMatches(ReadOnlySpan<byte> expected)
        {
            if (Remaining < expected.Length) return false;
            var magic = Slice(expected.Length);
            return magic.SequenceEqual(expected);
        }

        public byte ReadByte() => Slice(1)[0];

        public sbyte ReadSByte() => (sbyte)Slice(1)[0];

        public short ReadInt16() => BinaryPrimitives.ReadInt16LittleEndian(Slice(2));

        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Slice(2));

        public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Slice(4));

        public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Slice(4));

        public long ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian(Slice(8));

        public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Slice(8));

        public float ReadSingle()
        {
            uint bits = BinaryPrimitives.ReadUInt32LittleEndian(Slice(4));
            return new FloatUInt { UIntValue = bits }.FloatValue;
        }

        public double ReadDouble()
        {
            ulong bits = BinaryPrimitives.ReadUInt64LittleEndian(Slice(8));
            return new DoubleULong { ULongValue = bits }.DoubleValue;
        }

        public bool ReadBoolean() => Slice(1)[0] != 0;

        /// <summary>null 用 -1 表示，空字符串用 0。</summary>
        public string ReadString()
        {
            int len = ReadInt32();
            if (len == BinaryFormat.NullStringLength) return null;
            if (len == 0) return string.Empty;
            if (len < 0) throw new InvalidOperationException($"非法 string 长度: {len}");
            var bytes = Slice(len);
            return Encoding.UTF8.GetString(bytes);
        }

        public byte[] ReadByteArray()
        {
            int count = ReadInt32();
            if (count <= 0) return Array.Empty<byte>();
            var arr = new byte[count];
            for (int i = 0; i < count; i++) arr[i] = ReadByte();
            return arr;
        }

        public int[] ReadInt32Array()
        {
            int count = ReadInt32();
            if (count <= 0) return Array.Empty<int>();
            var arr = new int[count];
            for (int i = 0; i < count; i++) arr[i] = ReadInt32();
            return arr;
        }

        public float[] ReadSingleArray()
        {
            int count = ReadInt32();
            if (count <= 0) return Array.Empty<float>();
            var arr = new float[count];
            for (int i = 0; i < count; i++) arr[i] = ReadSingle();
            return arr;
        }

        public string[] ReadStringArray()
        {
            int count = ReadInt32();
            if (count <= 0) return Array.Empty<string>();
            var arr = new string[count];
            for (int i = 0; i < count; i++) arr[i] = ReadString();
            return arr;
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
