using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace SuperDucker.App;

/// <summary>
/// 主题预设：一组配色（6 个键，与 App.xaml 的 DynamicResource 完全对齐）。
/// 内建主题（深色 / 浅色）以 IsBuiltIn=true 存在，自定义主题持久化到 settings.theme_list。
/// </summary>
public class ThemePreset : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>主题名称。内建主题为固定常量 "Dark" / "Light"。</summary>
    public string Name { get; set; } = "";

    public bool IsBuiltIn { get; set; }

    private Color _bgDark;
    private Color _bgMedium;
    private Color _bgCard;
    private Color _bgCardHover;
    private Color _textPrimary;
    private Color _textSecondary;

    public Color BgDark { get => _bgDark; set { _bgDark = value; PropertyChanged?.Invoke(this, new(nameof(BgDark))); } }
    public Color BgMedium { get => _bgMedium; set { _bgMedium = value; PropertyChanged?.Invoke(this, new(nameof(BgMedium))); } }
    public Color BgCard { get => _bgCard; set { _bgCard = value; PropertyChanged?.Invoke(this, new(nameof(BgCard))); } }
    public Color BgCardHover { get => _bgCardHover; set { _bgCardHover = value; PropertyChanged?.Invoke(this, new(nameof(BgCardHover))); } }
    public Color TextPrimary { get => _textPrimary; set { _textPrimary = value; PropertyChanged?.Invoke(this, new(nameof(TextPrimary))); } }
    public Color TextSecondary { get => _textSecondary; set { _textSecondary = value; PropertyChanged?.Invoke(this, new(nameof(TextSecondary))); } }

    public ThemePreset() { }

    public ThemePreset(string name, Color bgDark, Color bgMedium, Color bgCard,
        Color bgCardHover, Color textPrimary, Color textSecondary, bool isBuiltIn = false)
    {
        Name = name;
        BgDark = bgDark;
        BgMedium = bgMedium;
        BgCard = bgCard;
        BgCardHover = bgCardHover;
        TextPrimary = textPrimary;
        TextSecondary = textSecondary;
        IsBuiltIn = isBuiltIn;
    }

    /// <summary>深拷贝一份（用于「派生新建」时以当前主题作为起点）。</summary>
    public ThemePreset Clone(string? newName = null) => new(
        newName ?? Name,
        BgDark, BgMedium, BgCard, BgCardHover, TextPrimary, TextSecondary,
        IsBuiltIn);

    // ─── 持久化（自定义主题存 JSON） ───
    public class Dto
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("bgDark")] public string BgDark { get; set; } = "";
        [JsonPropertyName("bgMedium")] public string BgMedium { get; set; } = "";
        [JsonPropertyName("bgCard")] public string BgCard { get; set; } = "";
        [JsonPropertyName("bgCardHover")] public string BgCardHover { get; set; } = "";
        [JsonPropertyName("textPrimary")] public string TextPrimary { get; set; } = "";
        [JsonPropertyName("textSecondary")] public string TextSecondary { get; set; } = "";
    }

    public Dto ToDto() => new()
    {
        Name = Name,
        BgDark = ColorHelper.ToHex(BgDark),
        BgMedium = ColorHelper.ToHex(BgMedium),
        BgCard = ColorHelper.ToHex(BgCard),
        BgCardHover = ColorHelper.ToHex(BgCardHover),
        TextPrimary = ColorHelper.ToHex(TextPrimary),
        TextSecondary = ColorHelper.ToHex(TextSecondary)
    };

    public static ThemePreset FromDto(Dto d) => new(
        d.Name,
        ColorHelper.FromHex(d.BgDark),
        ColorHelper.FromHex(d.BgMedium),
        ColorHelper.FromHex(d.BgCard),
        ColorHelper.FromHex(d.BgCardHover),
        ColorHelper.FromHex(d.TextPrimary),
        ColorHelper.FromHex(d.TextSecondary),
        isBuiltIn: false);
}

/// <summary>Color 与 #RRGGBB（含 #FF 前缀兼容）的互转工具。</summary>
public static class ColorHelper
{
    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color FromHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Colors.Black;
        var s = hex.Trim().TrimStart('#');
        // 兼容 #RRGGBB 与 #AARRGGBB
        if (s.Length == 8)
            s = s.Substring(2);
        if (s.Length != 6)
            return Colors.Black;
        try
        {
            var r = Convert.ToByte(s.Substring(0, 2), 16);
            var g = Convert.ToByte(s.Substring(2, 2), 16);
            var b = Convert.ToByte(s.Substring(4, 2), 16);
            return Color.FromRgb(r, g, b);
        }
        catch
        {
            return Colors.Black;
        }
    }
}
