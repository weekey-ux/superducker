namespace SuperDucker.Shared.Helpers;

/// <summary>
/// Generates abbreviation strings from friendly names.
/// </summary>
public static class AbbreviationGenerator
{
    private const int DefaultMaxLength = 8;

    /// <summary>
    /// Generates a readable abbreviation from a name, up to 8 characters.
    /// Prefers initials for multi-word names, falling back to a truncated cleaned name.
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
    /// Generates a short fallback abbreviation from a name using ASCII letters/digits only.
    /// </summary>
    /// <param name="name">Source name.</param>
    /// <param name="length">Maximum length of the generated abbreviation.</param>
    public static string GenerateShort(string name, int length = 4)
    {
        if (string.IsNullOrWhiteSpace(name) || length <= 0) return "";
        return new string(name.Where(char.IsAsciiLetterOrDigit).Take(length).ToArray()).ToUpperInvariant();
    }
}
