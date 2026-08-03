namespace SuperDucker.Shared.Models;

public class UrlEntry
{
    public int Id { get; set; }

    /// <summary>
    /// 唯一大写缩写。
    /// </summary>
    public string Abbreviation { get; set; } = string.Empty;

    /// <summary>
    /// 中文友好名称。
    /// </summary>
    public string? FriendlyName { get; set; }

    /// <summary>
    /// 在默认浏览器中打开的目标网址。
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 悬停时显示的描述信息。
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 用于分组的类别。
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// 自定义或抓取得到的图标文件路径。
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// 显示排序。
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 可选的标签页归属（用于分组）。
    /// </summary>
    public int? TabId { get; set; }

    /// <summary>
    /// 面板显示名称：若设置了友好名称则优先，否则使用缩写。
    /// </summary>
    public string DisplayName => FriendlyName ?? Abbreviation;
}
