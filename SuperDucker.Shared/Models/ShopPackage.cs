namespace SuperDucker.Shared.Models;

/// <summary>
/// 表示本地商店（localshop）中的一个 .sdzip 软件包。
/// </summary>
public class ShopPackage
{
    /// <summary>.sdzip 文件的完整路径。</summary>
    public string SdzipPath { get; set; } = string.Empty;

    /// <summary>来自清单的软件包 ID（例如 "notepad-plus-plus"）。</summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Win+R 启动缩写。</summary>
    public string Abbreviation { get; set; } = string.Empty;

    /// <summary>简短描述。</summary>
    public string? Description { get; set; }

    /// <summary>版本字符串。</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>作者名称。</summary>
    public string? Author { get; set; }

    /// <summary>第一个类别标签。</summary>
    public string? Category { get; set; }

    /// <summary>已解压图标的路径（来自缓存）。</summary>
    public string? IconPath { get; set; }

    /// <summary>该软件包是否已安装到系统中。</summary>
    public bool IsInstalled { get; set; }

    /// <summary>该软件包是否曾被安装但当前已卸载（软移除）。</summary>
    public bool IsUninstalled { get; set; }

    /// <summary>安装或卸载时的数据库条目 ID；尚未安装则为 null。</summary>
    public int? AppEntryId { get; set; }
}
