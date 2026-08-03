using System.IO;
using SuperDucker.Shared.Helpers;
using SuperDucker.Shared.Models;

namespace SuperDucker.Shared.Data;

/// <summary>
/// 扫描 app/ 目录，将包含 manifest.json 但未登记到数据库的应用重新注册。
/// 提供灾难恢复（disaster recovery）能力。
/// </summary>
public static class RescanHelper
{
    public class RescanResult
    {
        public int TotalScanned { get; set; }
        public int Recovered { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public List<string> RecoveredNames { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// 扫描 app/ 目录，根据 manifest.json 重新注册缺失的应用。
    /// </summary>
    public static RescanResult Rescan(DatabaseManager db)
    {
        var result = new RescanResult();
        var appDir = DatabaseManager.GetAppDirectory();

        if (!Directory.Exists(appDir))
            return result;

        var subDirs = Directory.GetDirectories(appDir);
        result.TotalScanned = subDirs.Length;

        foreach (var dir in subDirs)
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                // No manifest — try to register from exe files
                TryRegisterWithoutManifest(db, dir, result);
                continue;
            }

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = PackageManifest.FromJson(json);
                if (manifest == null)
                {
                    result.Failed++;
                    result.Errors.Add($"{Path.GetFileName(dir)}: manifest.json 解析失败");
                    continue;
                }

                var abbreviation = manifest.Abbreviation?.ToUpperInvariant()
                                   ?? Path.GetFileName(dir).ToUpperInvariant();

                // Check if already registered
                if (db.AbbreviationExists(abbreviation))
                {
                    // Verify the existing entry's exe still exists
                    var existing = db.GetAppByAbbreviation(abbreviation);
                    if (existing != null && File.Exists(existing.TargetPath))
                    {
                        result.Skipped++;
                        continue;
                    }
                    // Entry exists but exe is broken — update it
                    if (existing != null)
                    {
                        var mainExePath = Path.Combine(dir, manifest.MainExe);
                        if (File.Exists(mainExePath))
                        {
                            existing.TargetPath = mainExePath;
                            existing.WorkingDirectory = dir;
                            db.UpdateApp(existing);
                            ShortcutManager.CreateShortcut(existing);
                            result.Recovered++;
                            result.RecoveredNames.Add($"{abbreviation} ({manifest.Name}) — 路径已修复");
                        }
                        else
                        {
                            result.Failed++;
                            result.Errors.Add($"{abbreviation}: 主程序不存在 {manifest.MainExe}");
                        }
                        continue;
                    }
                }

                // Resolve main exe path
                var exePath = Path.Combine(dir, manifest.MainExe);
                if (!File.Exists(exePath))
                {
                    result.Failed++;
                    result.Errors.Add($"{abbreviation}: 主程序不存在 {manifest.MainExe}");
                    continue;
                }

                // Find icon — check icons/ dir first, then local icon files
                string? iconPath = null;
                var iconsDir = WebHelper.GetIconsDirectory();
                if (Directory.Exists(iconsDir))
                {
                    foreach (var ext in new[] { ".ico", ".png", ".jpg", ".bmp" })
                    {
                        var candidate = Path.Combine(iconsDir, $"{abbreviation}{ext}");
                        if (File.Exists(candidate)) { iconPath = candidate; break; }
                    }
                }
                if (iconPath == null)
                {
                    // Check for icon file in the app dir itself
                    foreach (var ext in new[] { ".ico", ".png", ".jpg", ".bmp" })
                    {
                        var candidate = Path.Combine(dir, $"icon{ext}");
                        if (File.Exists(candidate)) { iconPath = candidate; break; }
                    }
                }

                // Ensure unique abbreviation
                if (db.AbbreviationExists(abbreviation))
                {
                    var shortAbbr = AbbreviationGenerator.GenerateShort(manifest.Name);
                    if (!string.IsNullOrEmpty(shortAbbr) && !db.AbbreviationExists(shortAbbr))
                        abbreviation = shortAbbr;
                    else
                    {
                        result.Failed++;
                        result.Errors.Add($"{abbreviation}: 缩写冲突且无法自动生成替代");
                        continue;
                    }
                }

                var appEntry = new AppEntry
                {
                    Abbreviation = abbreviation,
                    FriendlyName = manifest.Name,
                    TargetPath = exePath,
                    WorkingDirectory = dir,
                    Description = manifest.Description,
                    Category = manifest.Categories.FirstOrDefault(),
                    IconPath = iconPath,
                    IsBuiltIn = true
                };

                db.AddApp(appEntry);
                ShortcutManager.CreateShortcut(appEntry);
                result.Recovered++;
                result.RecoveredNames.Add($"{abbreviation} ({manifest.Name})");
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Errors.Add($"{Path.GetFileName(dir)}: {ex.Message}");
            }
        }

        return result;
    }

    private static void TryRegisterWithoutManifest(DatabaseManager db, string dir, RescanResult result)
    {
        try
        {
            var dirName = Path.GetFileName(dir);

            // Look for exe files
            var exeFiles = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly);
            if (exeFiles.Length == 0)
            {
                exeFiles = Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories)
                    .Where(f => !f.Contains(Path.DirectorySeparatorChar + "unins", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            if (exeFiles.Length == 0) return;

            // Pick the main exe (prefer the one in root, otherwise first found)
            var mainExe = exeFiles.FirstOrDefault(f =>
                Path.GetDirectoryName(f)?.Equals(dir, StringComparison.OrdinalIgnoreCase) == true)
                ?? exeFiles[0];
            var abbreviation = dirName.ToUpperInvariant();

            if (db.AbbreviationExists(abbreviation))
                return; // Already registered or abbreviation conflict

            var appEntry = new AppEntry
            {
                Abbreviation = abbreviation,
                FriendlyName = dirName,
                TargetPath = mainExe,
                WorkingDirectory = dir,
                IsBuiltIn = true
            };

            db.AddApp(appEntry);
            ShortcutManager.CreateShortcut(appEntry);
            result.Recovered++;
            result.RecoveredNames.Add($"{abbreviation} ({dirName}) — 无 manifest，自动检测");
        }
        catch (Exception ex)
        {
            result.Failed++;
            result.Errors.Add($"{Path.GetFileName(dir)}: {ex.Message}");
        }
    }
}
