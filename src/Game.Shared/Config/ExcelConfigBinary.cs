using System.Buffers.Binary;
using System.Text;

namespace Game.Shared.Config;

/// <summary>
/// ExcelConfigCompiler 二进制格式（与 Unity Runtime BinaryFormat 对齐）。
/// MAGIC "EXCF" + version int32 + count int32 + rows...
/// </summary>
public static class ExcelConfigBinary
{
    public static readonly byte[] Magic = [(byte)'E', (byte)'X', (byte)'C', (byte)'F'];
    public const int CurrentVersion = 1;
    public const int NullStringLength = -1;

    public static void ValidateHeader(ReadOnlySpan<byte> data, out int version, out int count, out int offset)
    {
        if (data.Length < 12)
            throw new InvalidDataException("ExcelConfig .bytes too short (header).");
        if (!data[..4].SequenceEqual(Magic))
            throw new InvalidDataException("ExcelConfig .bytes magic mismatch (expect EXCF).");
        version = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4, 4));
        if (version != CurrentVersion)
            throw new InvalidDataException($"ExcelConfig version mismatch: file={version}, code={CurrentVersion}");
        count = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(8, 4));
        if (count < 0)
            throw new InvalidDataException($"ExcelConfig invalid row count: {count}");
        offset = 12;
    }

    public static int ReadInt32(ReadOnlySpan<byte> data, ref int offset)
    {
        var v = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;
        return v;
    }

    public static string? ReadString(ReadOnlySpan<byte> data, ref int offset)
    {
        int len = ReadInt32(data, ref offset);
        if (len == NullStringLength) return null;
        if (len == 0) return string.Empty;
        if (len < 0 || offset + len > data.Length)
            throw new InvalidDataException($"Invalid string length {len} at {offset}");
        var s = Encoding.UTF8.GetString(data.Slice(offset, len));
        offset += len;
        return s;
    }
}
