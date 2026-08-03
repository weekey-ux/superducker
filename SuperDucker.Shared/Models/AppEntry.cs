namespace SuperDucker.Shared.Models;

public class AppEntry
{
    public int Id { get; set; }

    /// <summary>
    /// 用于 Win+R 启动的唯一大写缩写（例如 "CHROME"）。
    /// </summary>
    public string Abbreviation { get; set; } = string.Empty;

    /// <summary>
    /// 中文友好名称，按住 Ctrl 时在面板中显示。
    /// 为 null 时使用内置推荐名称（若存在）。
    /// </summary>
    public string? FriendlyName { get; set; }

    /// <summary>
    /// 可执行文件的绝对路径。
    /// </summary>
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>
    /// 进程的工作目录。为 null 时使用 exe 所在目录。
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// 描述信息，在面板悬停或执行 `sd e` 时显示。
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 自定义图标路径。为 null 时使用 exe 自身的图标。
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// 用于面板分组的类别。
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// 是否为内置绿色软件（true）或外部程序（false）。
    /// </summary>
    public bool IsBuiltIn { get; set; } = true;

    /// <summary>
    /// 类别内的显示排序。
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 可选的标签页归属（用于分组）。
    /// </summary>
    public int? TabId { get; set; }

    /// <summary>
    /// 软卸载标记：隐藏于主视图，但数据库记录与应用文件仍保留。
    /// </summary>
    public bool IsUninstalled { get; set; }

    /// <summary>
    /// 安装的应用版本号（取自 manifest.json 的 version）。旧记录为 null，升级时回读补齐。
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// 面板显示名称：若设置了友好名称则优先，否则使用缩写。
    /// </summary>
    public string DisplayName => FriendlyName ?? Abbreviation;
}
