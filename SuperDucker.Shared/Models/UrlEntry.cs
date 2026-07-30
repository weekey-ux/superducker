namespace SuperDucker.Shared.Models;

public class UrlEntry
{
    public int Id { get; set; }

    /// <summary>
    /// Unique uppercase abbreviation
    /// </summary>
    public string Abbreviation { get; set; } = string.Empty;

    /// <summary>
    /// Chinese friendly name
    /// </summary>
    public string? FriendlyName { get; set; }

    /// <summary>
    /// The URL to open in default browser
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Description shown on hover
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Category for grouping
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Path to custom or fetched icon file
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// Display order
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Optional tab assignment for grouping
    /// </summary>
    public int? TabId { get; set; }

    /// <summary>
    /// Display name for panel
    /// </summary>
    public string DisplayName => FriendlyName ?? Abbreviation;
}
