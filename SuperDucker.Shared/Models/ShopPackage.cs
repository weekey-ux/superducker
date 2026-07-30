namespace SuperDucker.Shared.Models;

/// <summary>
/// Represents a .sdzip package in the local shop.
/// </summary>
public class ShopPackage
{
    /// <summary>Full path to the .sdzip file.</summary>
    public string SdzipPath { get; set; } = string.Empty;

    /// <summary>Package ID from manifest (e.g. "notepad-plus-plus").</summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Win+R abbreviation.</summary>
    public string Abbreviation { get; set; } = string.Empty;

    /// <summary>Short description.</summary>
    public string? Description { get; set; }

    /// <summary>Version string.</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>Author name.</summary>
    public string? Author { get; set; }

    /// <summary>First category label.</summary>
    public string? Category { get; set; }

    /// <summary>Path to extracted icon (from cache).</summary>
    public string? IconPath { get; set; }

    /// <summary>Whether this package is already installed in the system.</summary>
    public bool IsInstalled { get; set; }

    /// <summary>Whether this package was previously installed but is currently uninstalled (soft remove).</summary>
    public bool IsUninstalled { get; set; }

    /// <summary>Database entry id when installed or uninstalled; null if not yet installed.</summary>
    public int? AppEntryId { get; set; }
}
