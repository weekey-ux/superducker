using System.IO.Compression;
using SuperDucker.Shared.Helpers;
using SuperDucker.Shared.Models;

namespace SuperDucker.Shared.Data;

/// <summary>
/// Manages the local shop: scanning .sdzip packages, reading manifests, and installing.
/// </summary>
public static class ShopManager
{
    /// <summary>
    /// Gets the localshop directory path.
    /// </summary>
    public static string GetShopDirectory()
    {
        return Path.Combine(DatabaseManager.GetRootDirectory(), "localshop");
    }

    /// <summary>
    /// Gets the shop cache directory for extracted icons and temp files.
    /// </summary>
    public static string GetCacheDirectory()
    {
        return Path.Combine(GetShopDirectory(), ".cache");
    }

    /// <summary>
    /// Scans the localshop/ directory for .sdzip files and reads their manifests.
    /// Checks installation status against the database.
    /// </summary>
    public static List<ShopPackage> ScanPackages(DatabaseManager db)
    {
        var shopDir = GetShopDirectory();
        if (!Directory.Exists(shopDir))
            return new List<ShopPackage>();

        var cacheDir = GetCacheDirectory();
        Directory.CreateDirectory(cacheDir);

        var packages = new List<ShopPackage>();

        foreach (var sdzipPath in Directory.GetFiles(shopDir, "*.sdzip"))
        {
            try
            {
                var pkg = ReadPackageInfo(sdzipPath, cacheDir);
                if (pkg == null) continue;

                // Determine package state: available, installed, or uninstalled
                var abbr = pkg.Abbreviation.ToUpperInvariant();
                var existing = db.GetAppByAbbreviation(abbr);
                if (existing != null)
                {
                    pkg.AppEntryId = existing.Id;
                    pkg.IsInstalled = !existing.IsUninstalled;
                    pkg.IsUninstalled = existing.IsUninstalled;
                }

                packages.Add(pkg);
            }
            catch
            {
                // Skip corrupt or invalid .sdzip files
            }
        }

        return packages;
    }

    /// <summary>
    /// Reads package metadata from a .sdzip file without full extraction.
    /// Extracts icon to cache directory for UI display.
    /// </summary>
    private static ShopPackage? ReadPackageInfo(string sdzipPath, string cacheDir)
    {
        using var zipStream = File.OpenRead(sdzipPath);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        // Read manifest.json
        var manifestEntry = archive.GetEntry("manifest.json");
        if (manifestEntry == null) return null;

        using var reader = new StreamReader(manifestEntry.Open());
        var json = reader.ReadToEnd();
        var manifest = PackageManifest.FromJson(json);
        if (manifest == null) return null;

        var packageId = manifest.Id;
        if (string.IsNullOrEmpty(packageId))
            packageId = Path.GetFileNameWithoutExtension(sdzipPath);

        var pkg = new ShopPackage
        {
            SdzipPath = sdzipPath,
            PackageId = packageId,
            Name = manifest.Name,
            Abbreviation = manifest.Abbreviation?.ToUpperInvariant() ?? packageId.ToUpperInvariant(),
            Description = manifest.Description,
            Version = manifest.Version,
            Author = manifest.Author,
            Category = manifest.Categories.FirstOrDefault()
        };

        // Extract icon to cache (if present and not already cached)
        // Icon can be named either "icon.{ext}" or "{abbreviation}.{ext}"
        var iconEntry = archive.Entries.FirstOrDefault(e =>
            string.IsNullOrEmpty(Path.GetDirectoryName(e.FullName)) &&
            (e.Name.StartsWith("icon.", StringComparison.OrdinalIgnoreCase) ||
             e.Name.StartsWith(pkg.Abbreviation + ".", StringComparison.OrdinalIgnoreCase)));

        if (iconEntry != null)
        {
            var iconExt = Path.GetExtension(iconEntry.Name);
            var cachedIconPath = Path.Combine(cacheDir, $"{packageId}{iconExt}");
            if (!File.Exists(cachedIconPath))
            {
                iconEntry.ExtractToFile(cachedIconPath, true);
            }
            pkg.IconPath = cachedIconPath;
        }

        return pkg;
    }

    /// <summary>
    /// Installs a package from its .sdzip file into the app/ directory.
    /// If the package was previously uninstalled, restores the existing entry.
    /// Returns the installed AppEntry, or null if already installed or on failure.
    /// </summary>
    public static AppEntry? InstallPackage(ShopPackage package, DatabaseManager db)
    {
        var appDir = DatabaseManager.GetAppDirectory();
        var targetDir = Path.Combine(appDir, package.PackageId);

        // Already installed and not uninstalled
        var existing = package.AppEntryId.HasValue ? db.GetAppById(package.AppEntryId.Value) : null;
        if (existing != null && !existing.IsUninstalled)
            return null;

        // Previously uninstalled: restore if files still exist, otherwise re-extract
        if (existing != null && existing.IsUninstalled)
        {
            if (Directory.Exists(targetDir))
            {
                db.SetAppUninstalled(existing.Id, false);
                ShortcutManager.CreateShortcut(existing);
                package.IsInstalled = true;
                package.IsUninstalled = false;
                return existing;
            }
            // Files are gone: fall through to fresh install and reuse the DB record
        }

        // Fresh install: directory must not exist
        if (Directory.Exists(targetDir)) return null;

        Directory.CreateDirectory(targetDir);

        string? extractedIconPath = null;
        PackageManifest? manifest = null;

        using (var zipStream = File.OpenRead(package.SdzipPath))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                // Read manifest
                if (entry.FullName == "manifest.json")
                {
                    using var reader = new StreamReader(entry.Open());
                    var json = reader.ReadToEnd();
                    manifest = PackageManifest.FromJson(json);
                    // Save manifest copy for recovery
                    File.WriteAllText(Path.Combine(targetDir, "manifest.json"), json);
                    continue;
                }

                if (string.IsNullOrEmpty(entry.Name)) continue;

                // Extract icon (can be named "icon.{ext}" or "{abbreviation}.{ext}")
                if (!entry.FullName.Contains('/') &&
                    (entry.Name.StartsWith("icon.", StringComparison.OrdinalIgnoreCase) ||
                     entry.Name.StartsWith(package.Abbreviation + ".", StringComparison.OrdinalIgnoreCase)))
                {
                    var iconsDir = WebHelper.GetIconsDirectory();
                    Directory.CreateDirectory(iconsDir);
                    var iconExt = Path.GetExtension(entry.Name);
                    var iconAbbr = package.Abbreviation;
                    extractedIconPath = Path.Combine(iconsDir, $"{iconAbbr}{iconExt}");
                    entry.ExtractToFile(extractedIconPath, true);
                    continue;
                }

                // Extract app files (strip "app/" prefix)
                var entryPath = entry.FullName;
                if (entryPath.StartsWith("app/"))
                    entryPath = entryPath[4..];

                var targetPath = Path.GetFullPath(Path.Combine(targetDir, entryPath));

                // Zip-slip protection: ensure resolved path is within targetDir
                if (!targetPath.StartsWith(targetDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    !targetPath.Equals(targetDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                var targetFileDir = Path.GetDirectoryName(targetPath);
                if (targetFileDir != null) Directory.CreateDirectory(targetFileDir);

                entry.ExtractToFile(targetPath, true);
            }
        }

        if (manifest == null) return null;

        if (existing != null)
        {
            // Reuse existing DB record (was uninstalled and files were missing)
            existing.TargetPath = Path.Combine(targetDir, manifest.MainExe);
            existing.WorkingDirectory = targetDir;
            existing.IconPath = extractedIconPath ?? existing.IconPath;
            existing.IsUninstalled = false;
            db.UpdateApp(existing);
            ShortcutManager.CreateShortcut(existing);
            package.IsInstalled = true;
            package.IsUninstalled = false;
            return existing;
        }

        // Resolve abbreviation conflicts
        var abbreviation = package.Abbreviation;
        if (db.AbbreviationExists(abbreviation))
        {
            var shortAbbr = AbbreviationGenerator.GenerateShort(manifest.Name);
            if (!string.IsNullOrEmpty(shortAbbr) && !db.AbbreviationExists(shortAbbr))
                abbreviation = shortAbbr;
            else
                return null;
        }

        var mainExePath = Path.Combine(targetDir, manifest.MainExe);

        var appEntry = new AppEntry
        {
            Abbreviation = abbreviation,
            FriendlyName = manifest.Name,
            TargetPath = mainExePath,
            WorkingDirectory = targetDir,
            Description = manifest.Description,
            Category = manifest.Categories.FirstOrDefault(),
            IconPath = extractedIconPath,
            IsBuiltIn = true
        };

        db.AddApp(appEntry);
        ShortcutManager.CreateShortcut(appEntry);

        // Auto-create tab from first tag
        try
        {
            if (manifest.Tags.Count > 0)
            {
                var tagName = manifest.Tags[0];
                var tabs = db.GetAllTabs();
                var tab = tabs.FirstOrDefault(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
                if (tab == null)
                    tab = db.AddTab(new TabEntry { Name = tagName, SortOrder = tabs.Count });
                db.SetEntryTab("app_entries", appEntry.Id, tab.Id);
            }
        }
        catch { /* Tab creation is best-effort */ }

        package.IsInstalled = true;
        package.IsUninstalled = false;
        package.AppEntryId = appEntry.Id;
        return appEntry;
    }

    /// <summary>
    /// Soft-uninstalls a package: hides it from the main UI but preserves app files.
    /// </summary>
    public static bool UninstallPackage(ShopPackage package, DatabaseManager db)
    {
        if (!package.AppEntryId.HasValue) return false;

        var entry = db.GetAppById(package.AppEntryId.Value);
        if (entry == null || entry.IsUninstalled) return false;

        db.SetAppUninstalled(entry.Id, true);
        ShortcutManager.DeleteShortcut(entry.Abbreviation);

        package.IsInstalled = false;
        package.IsUninstalled = true;
        return true;
    }

    /// <summary>
    /// Permanently deletes a package: removes DB record, app directory, and shortcuts.
    /// </summary>
    public static bool DeletePackage(ShopPackage package, DatabaseManager db)
    {
        if (!package.AppEntryId.HasValue) return false;

        var entry = db.GetAppById(package.AppEntryId.Value);
        if (entry == null) return false;

        var appDir = DatabaseManager.GetAppDirectory();
        var targetDir = Path.Combine(appDir, package.PackageId);

        try
        {
            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, recursive: true);
        }
        catch { /* Best-effort directory cleanup */ }

        ShortcutManager.DeleteShortcut(entry.Abbreviation);
        db.DeleteApp(entry.Id);

        package.IsInstalled = false;
        package.IsUninstalled = false;
        package.AppEntryId = null;
        return true;
    }
}
