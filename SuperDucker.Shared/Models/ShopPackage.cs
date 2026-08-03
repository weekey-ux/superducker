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

    /// <summary>已安装应用在本地的版本号（AppEntry.Version），未安装时为 null。</summary>
    public string? InstalledVersion { get; set; }

    /// <summary>升级状态：None=无需处理；Upgrade=版本更高可升级；Reinstall=等于或低于，覆盖重装。</summary>
    public ShopUpgradeState UpgradeState { get; set; } = ShopUpgradeState.None;

    /// <summary>.sdzip 进入 localshop 的时间（UTC）。</summary>
    public DateTime AddedTime { get; set; } = DateTime.MinValue;

    /// <summary>保留天数，默认 30 天。超过则视为过期，可被自动清理（仅限未安装的包）。</summary>
    public int KeepDays { get; set; } = 30;

    /// <summary>过期时间点（AddedTime + KeepDays）。</summary>
    public DateTime ExpiresAt => AddedTime == DateTime.MinValue
        ? DateTime.MaxValue
        : AddedTime.AddDays(KeepDays);

    /// <summary>是否已过期（AddedTime 已记录且当前 UTC 已超过 ExpiresAt）。</summary>
    public bool IsExpired => AddedTime != DateTime.MinValue && DateTime.UtcNow > ExpiresAt;
}

/// <summary>本地商店中一个包相对已安装应用的升级状态。</summary>
public enum ShopUpgradeState
{
    /// <summary>无对应已安装应用（即全新安装），无需升级处理。</summary>
    None,
    /// <summary>包版本高于已安装版本，可"升级"。</summary>
    Upgrade,
    /// <summary>包版本等于或低于已安装版本，覆盖"重装"。</summary>
    Reinstall
}
