namespace SuperDucker.Shared.Helpers;

/// <summary>
/// 从友好名称生成缩写字符串。
/// </summary>
public static class AbbreviationGenerator
{
    private const int DefaultMaxLength = 8;

    /// <summary>
    /// 从名称生成可读缩写，最长 8 个字符。
    /// 多词名称优先取各词首字母，失败则回退为清理后名称的截断形式。
    /// </summary>
    public static string Generate(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";

        var cleaned = new string(name.Where(char.IsLetterOrDigit).ToArray());
        if (cleaned.Length <= DefaultMaxLength)
            return cleaned.ToUpperInvariant();

        var parts = name.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1)
        {
            var abbr = new string(parts.Select(p => p[0]).Take(DefaultMaxLength).ToArray());
            if (abbr.Length >= 2) return abbr.ToUpperInvariant();
        }

        return cleaned[..Math.Min(cleaned.Length, DefaultMaxLength)].ToUpperInvariant();
    }

    /// <summary>
    /// 从名称生成仅含 ASCII 字母/数字的简短回退缩写。
    /// </summary>
    /// <param name="name">源名称。</param>
    /// <param name="length">生成缩写的最大长度。</param>
    public static string GenerateShort(string name, int length = 4)
    {
        if (string.IsNullOrWhiteSpace(name) || length <= 0) return "";
        return new string(name.Where(char.IsAsciiLetterOrDigit).Take(length).ToArray()).ToUpperInvariant();
    }
}
