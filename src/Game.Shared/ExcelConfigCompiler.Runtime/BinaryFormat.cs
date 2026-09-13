namespace ExcelConfigCompiler.Runtime
{
    /// <summary>
    /// 二进制配置文件公共格式定义。Compiler（写）和 Runtime（读）必须引用同一份，
    /// 保证 Magic / Version 永远只有一份定义。
    /// 
    /// 文件布局（Little Endian）：
    ///   MAGIC   4 bytes  = 'E','X','C','F'
    ///   VERSION 4 bytes  = int32
    ///   COUNT   4 bytes  = int32（行数）
    ///   ROW DATA * COUNT
    /// </summary>
    public static class BinaryFormat
    {
        public static readonly byte[] Magic = { (byte)'E', (byte)'X', (byte)'C', (byte)'F' };

        public const int CurrentVersion = 1;

        /// <summary>string 的 null 标记：写入长度字段为 -1 表示 null（区别于空字符串的 0）。</summary>
        public const int NullStringLength = -1;
    }
}
