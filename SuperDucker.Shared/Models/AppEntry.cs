namespace SuperDucker.Shared.Models;

public class AppEntry
{
    public int Id { get; set; }

    /// <summary>
    /// Unique uppercase abbreviation for Win+R launch (e.g. "CHROME")
    /// </summary>
    public string Abbreviation { get; set; } = string.Empty;

    /// <summary>
    /// Chinese friendly name, shown when Ctrl is held in panel.
    /// Null = use built-in recommendation if available.
    /// </summary>
    public string? FriendlyName { get; set; }

    /// <summary>
    /// Absolute path to the executable file
    /// </summary>
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>
    /// Working directory for the process. Null = use exe's directory.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Description shown on hover in panel or via `sd e`
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Custom icon path. Null = use exe's own icon.
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// Category for grouping in panel
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Whether this is a built-in green app (true) or external program (false)
    /// </summary>
    public bool IsBuiltIn { get; set; } = true;

    /// <summary>
    /// Display order within category
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Optional tab assignment for grouping
    /// </summary>
    public int? TabId { get; set; }

    /// <summary>
    /// Soft-uninstalled: hidden from main views but DB record and app files preserved.
    /// </summary>
    public bool IsUninstalled { get; set; }

    /// <summary>
    /// Display name for panel: FriendlyName if set, else Abbreviation
    /// </summary>
    public string DisplayName => FriendlyName ?? Abbreviation;
}
