using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using SuperDucker.Shared;
using SuperDucker.Shared.Helpers;
using SuperDucker.Shared.Models;

namespace SuperDucker.Shared.Data;

/// <summary>
/// 管理本地商店（localshop）：扫描 .sdzip 软件包、读取清单（manifest）并完成安装。
/// </summary>
public static class ShopManager
{
    /// <summary>
    /// 获取 localshop 目录的路径。
    /// </summary>
    public static string GetShopDirectory()
    {
        return Path.Combine(DatabaseManager.GetRootDirectory(), "localshop");
    }

    /// <summary>
    /// 获取商店缓存目录（用于解压图标与临时文件）的路径。
    /// </summary>
    public static string GetCacheDirectory()
    {
        return Path.Combine(GetShopDirectory(), ".cache");
    }

    /// <summary>
    /// 扫描 localshop/ 中所有 .sdzip 的 manifest，聚合去重的标签列表（按字母序）。
    /// 用于打包界面下拉框自动建议标签。
    /// </summary>
    public static List<string> GetAllTagsFromShop()
    {
        var shopDir = GetShopDirectory();
        if (!Directory.Exists(shopDir))
            return new List<string>();

        var tags = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sdzipPath in Directory.GetFiles(shopDir, "*.sdzip"))
        {
            try
            {
                using var zipStream = File.OpenRead(sdzipPath);
                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
                var manifestEntry = archive.GetEntry("manifest.json");
                if (manifestEntry == null) continue;
                using var reader = new StreamReader(manifestEntry.Open());
                var manifest = PackageManifest.FromJson(reader.ReadToEnd());
                if (manifest?.Tags == null) continue;
                foreach (var t in manifest.Tags)
                {
                    if (!string.IsNullOrWhiteSpace(t))
                        tags.Add(t.Trim());
                }
            }
            catch { /* 单个包解析失败不影响其它 */ }
        }
        return tags.ToList();
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

                    // 与已安装应用比对状态（本地包/远程包复用同一逻辑）
                    FillInstalledState(pkg, db);

                    // 登记安装包加入时间（用于定时清理）。已安装的应用其包保留作为升级源，
                    // 不会被自动删除；未安装的包过期后由 CleanupExpiredPackages 清理。
                    pkg.AddedTime = db.GetShopPackageAddedTime(sdzipPath) ?? DateTime.MinValue;
                    if (pkg.AddedTime == DateTime.MinValue)
                    {
                        db.UpsertShopPackage(sdzipPath, pkg.KeepDays);
                        pkg.AddedTime = db.GetShopPackageAddedTime(sdzipPath) ?? DateTime.MinValue;
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
    /// 将一组软件包与已安装应用（依据缩写匹配）比对，回填
    /// AppEntryId / IsInstalled / IsUninstalled / InstalledVersion / UpgradeState。
    /// 本地包与远程（商店服务）包共用此逻辑，使远程包也能显示"已安装/可升级"。
    /// </summary>
    public static void FillInstalledStates(IEnumerable<ShopPackage> packages, DatabaseManager db)
    {
        foreach (var pkg in packages)
            FillInstalledState(pkg, db);
    }

    /// <summary>
    /// 单个包的安装状态比对（详见 <see cref="FillInstalledStates"/>）。
    /// </summary>
    private static void FillInstalledState(ShopPackage pkg, DatabaseManager db)
    {
        if (string.IsNullOrEmpty(pkg.Abbreviation)) return;

        var abbr = pkg.Abbreviation.ToUpperInvariant();
        var existing = db.GetAppByAbbreviation(abbr);
        if (existing == null) return;

        pkg.AppEntryId = existing.Id;
        pkg.IsInstalled = !existing.IsUninstalled;
        pkg.IsUninstalled = existing.IsUninstalled;

        // 补齐已安装应用的版本号：旧记录可能为 null，回读 manifest.json
        var installedVersion = existing.Version;
        if (string.IsNullOrEmpty(installedVersion))
        {
            var appDir = DatabaseManager.GetAppDirectory();
            var manifestPath = Path.Combine(appDir, pkg.PackageId, "manifest.json");
            installedVersion = ReadInstalledVersion(manifestPath);
            if (!string.IsNullOrEmpty(installedVersion))
            {
                existing.Version = installedVersion;
                db.UpdateApp(existing);
            }
        }

        pkg.InstalledVersion = installedVersion;

        // 计算升级状态：高于→升级；等于/低于→重装（仅对已安装的应用有意义）
        if (!string.IsNullOrEmpty(installedVersion))
        {
            var cmp = UpdateChecker.CompareSemVer(
                UpdateChecker.NormalizeVersion(pkg.Version) ?? "0.0.0",
                UpdateChecker.NormalizeVersion(installedVersion) ?? "0.0.0");
            pkg.UpgradeState = cmp > 0 ? ShopUpgradeState.Upgrade : ShopUpgradeState.Reinstall;
        }
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
    /// 从已安装应用目录的 manifest.json 中回读版本号（用于补齐旧记录缺失的 AppEntry.Version）。
    /// </summary>
    private static string? ReadInstalledVersion(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath)) return null;
            var json = File.ReadAllText(manifestPath);
            var manifest = PackageManifest.FromJson(json);
            return manifest?.Version;
        }
        catch
        {
            return null;
        }
    }
    /// <summary>
    /// 将 .sdzip 包安装到 app/ 目录下。
    /// 若该包此前被卸载，则恢复既有条目（而非新建）。
    /// 返回安装后的 AppEntry；若已安装或安装失败则返回 null。
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

    /// <summary>
    /// 升级 / 覆盖重装一个已安装的应用：将 .sdzip 内容覆盖解压到 app/{PackageId} 目录，
    /// 更新数据库中的 TargetPath / Version，并重建快捷方式。仅对已安装的应用有效。
    /// 返回更新后的 AppEntry，失败返回 null。
    /// </summary>
    public static AppEntry? UpgradePackage(ShopPackage package, DatabaseManager db)
    {
        if (!package.AppEntryId.HasValue) return null;

        var entry = db.GetAppById(package.AppEntryId.Value);
        if (entry == null || entry.IsUninstalled) return null;

        var appDir = DatabaseManager.GetAppDirectory();
        var targetDir = Path.Combine(appDir, package.PackageId);

        // 确保目标目录存在（理论上已安装应存在，缺失则创建）
        Directory.CreateDirectory(targetDir);

        // 原子替换：先解压到临时目录，全部成功后再整体替换 app/{PackageId}。
        // 这样即便老程序占用部分文件，也不会留下"新旧混杂"的半完成状态。
        var cacheDir = GetCacheDirectory();
        Directory.CreateDirectory(cacheDir);
        var tempDir = Path.Combine(cacheDir, $"{package.PackageId}.upgrade.tmp");
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);

        string? extractedIconPath = null;
        PackageManifest? manifest = null;

        try
        {
            using (var zipStream = File.OpenRead(package.SdzipPath))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                foreach (var entryItem in archive.Entries)
                {
                    if (entryItem.FullName == "manifest.json")
                    {
                        using var reader = new StreamReader(entryItem.Open());
                        var json = reader.ReadToEnd();
                        manifest = PackageManifest.FromJson(json);
                        File.WriteAllText(Path.Combine(tempDir, "manifest.json"), json);
                        continue;
                    }

                    if (string.IsNullOrEmpty(entryItem.Name)) continue;

                    if (!entryItem.FullName.Contains('/') &&
                        (entryItem.Name.StartsWith("icon.", StringComparison.OrdinalIgnoreCase) ||
                         entryItem.Name.StartsWith(package.Abbreviation + ".", StringComparison.OrdinalIgnoreCase)))
                    {
                        var iconsDir = WebHelper.GetIconsDirectory();
                        Directory.CreateDirectory(iconsDir);
                        var iconExt = Path.GetExtension(entryItem.Name);
                        extractedIconPath = Path.Combine(iconsDir, $"{package.Abbreviation}{iconExt}");
                        entryItem.ExtractToFile(extractedIconPath, true);
                        continue;
                    }

                    var entryPath = entryItem.FullName;
                    if (entryPath.StartsWith("app/"))
                        entryPath = entryPath[4..];

                    var targetPath = Path.GetFullPath(Path.Combine(tempDir, entryPath));
                    if (!targetPath.StartsWith(tempDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                        !targetPath.Equals(tempDir, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var targetFileDir = Path.GetDirectoryName(targetPath);
                    if (targetFileDir != null) Directory.CreateDirectory(targetFileDir);

                    entryItem.ExtractToFile(targetPath, true);
                }
            }

            if (manifest == null)
            {
                Directory.Delete(tempDir, true);
                return null;
            }

            // 升级前结束占用老程序的进程，确保旧文件可被整体替换。
            TryStopRunningProcess(entry.TargetPath, package.Name);

            // 原子替换：把旧目录移走作备份，再把临时目录重命名为目标目录。
            var backupDir = Path.Combine(cacheDir, $"{package.PackageId}.backup.tmp");
            if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);

            bool backupOk = false;
            try
            {
                if (Directory.Exists(targetDir))
                {
                    Directory.Move(targetDir, backupDir);
                    backupOk = true;
                }
                Directory.Move(tempDir, targetDir);

                // 升级后从备份目录救回用户数据（配置文件/数据文件），
                // 避免高版本包覆盖掉老程序用户生成的内容。
                var preserve = manifest?.UninstallActions?.PreserveUserData;
                if (backupOk && preserve != null && preserve.Count > 0)
                {
                    RestorePreservedItems(backupDir, targetDir, preserve);
                }
            }
            catch (Exception)
            {
                // 替换失败：回滚到旧目录（若已移走）
                if (backupOk && !Directory.Exists(targetDir))
                {
                    try { Directory.Move(backupDir, targetDir); } catch { }
                }
                else if (!backupOk && Directory.Exists(tempDir))
                {
                    try { Directory.Move(tempDir, targetDir); } catch { }
                }
                throw;
            }
            finally
            {
                // 替换成功后清理备份；无论如何清理临时目录
                if (Directory.Exists(backupDir))
                {
                    try { Directory.Delete(backupDir, true); } catch { }
                }
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }

            entry.TargetPath = Path.Combine(targetDir, manifest.MainExe);
            entry.WorkingDirectory = targetDir;
            entry.IconPath = extractedIconPath ?? entry.IconPath;
            entry.Version = manifest.Version; // 升级后写入新版本号
            db.UpdateApp(entry);

            ShortcutManager.DeleteShortcut(entry.Abbreviation);
            ShortcutManager.CreateShortcut(entry);

            package.IsInstalled = true;
            package.IsUninstalled = false;
            package.InstalledVersion = manifest.Version;
            package.UpgradeState = ShopUpgradeState.None;
            return entry;
        }
        catch (Exception)
        {
            // 任何阶段失败都清理临时目录，保证 app/{PackageId} 要么保持旧版、要么已是新版
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            return null;
        }
    }

        /// <summary>
        /// 升级后从旧目录（备份）把用户数据救回到新目录。
        /// 规则（新包优先、独有文件救回）：
        ///  - 条目以 / 结尾：作为目录合并，仅当目标缺文件时才救回旧文件；
        ///  - 条目含通配符 * 或 ?：在旧目录递归匹配，逐个救回；
        ///  - 其它：精确相对路径，仅当目标缺该文件时救回。
        /// 这样既能保留用户生成的数据/配置，又让新包的同名默认文件优先。
        /// </summary>
        private static void RestorePreservedItems(string backupDir, string targetDir, List<string> patterns)
        {
            foreach (var raw in patterns)
            {
                var pattern = raw.Replace('\\', '/').Trim('/', ' ');
                if (string.IsNullOrEmpty(pattern)) continue;

                var isDir = pattern.EndsWith('/');
                if (isDir) pattern = pattern.TrimEnd('/');

                // 目录合并
                if (isDir)
                {
                    var srcDir = Path.Combine(backupDir, pattern);
                    if (!Directory.Exists(srcDir)) continue;
                    foreach (var file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(srcDir, file);
                        var dst = Path.Combine(targetDir, pattern, rel);
                        RestoreSingle(file, dst);
                    }
                    continue;
                }

                // 通配符匹配
                if (pattern.Contains('*') || pattern.Contains('?'))
                {
                    var srcRoot = backupDir;
                    var idx = pattern.IndexOfAny(new[] { '*', '?' });
                    // 取通配符之前最后一段分隔符位置作为固定前缀目录
                    var prefix = pattern[..idx];
                    var slash = prefix.LastIndexOf('/');
                    if (slash >= 0)
                    {
                        srcRoot = Path.Combine(backupDir, prefix[..slash]);
                        pattern = pattern[(slash + 1)..];
                    }
                    else
                    {
                        // 仅在根目录匹配，pattern 保持不变
                    }
                    if (!Directory.Exists(srcRoot)) continue;
                    foreach (var file in Directory.EnumerateFiles(srcRoot, pattern, SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(backupDir, file);
                        var dst = Path.Combine(targetDir, rel);
                        RestoreSingle(file, dst);
                    }
                    continue;
                }

                // 精确路径
                var exactSrc = Path.Combine(backupDir, pattern);
                if (!File.Exists(exactSrc)) continue;
                var exactDst = Path.Combine(targetDir, pattern);
                RestoreSingle(exactSrc, exactDst);
            }
        }

        /// <summary>
        /// 仅当目标文件不存在时才从源复制（保留新包的同名文件，救回用户独有文件）。
        /// </summary>
        private static void RestoreSingle(string src, string dst)
        {
            try
            {
                if (File.Exists(dst)) return;
                var dir = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.Copy(src, dst, false);
            }
            catch { }
        }

    /// <summary>
    /// 升级前尝试结束正在运行、且以指定 exe 路径为主模块的目标进程，
    /// 以便释放被锁定的文件，使 app/{PackageId} 目录可被整体替换。
    /// 流程：先发送关闭窗口消息（优雅退出）→ 超时/失败则强制结束（Kill）。
    /// 若进程无法结束（如权限不足），方法会等待其真正退出，仍未退出则抛出
    /// IOException 风格的异常交由上层提示用户手动关闭。
    /// </summary>
    private static void TryStopRunningProcess(string? targetExePath, string appName)
    {
        if (string.IsNullOrEmpty(targetExePath)) return;

        var exeFull = Path.GetFullPath(targetExePath);
        List<Process>? procs = null;
        try
        {
            procs = Process.GetProcesses()
                .Where(p =>
                {
                    try
                    {
                        return string.Equals(p.MainModule?.FileName, exeFull, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        // 无法读取模块（如跨位宽/权限不足），跳过该进程
                        return false;
                    }
                })
                .ToList();
        }
        catch
        {
            procs = null;
        }

        if (procs == null || procs.Count == 0) return;

        // 1) 优雅关闭：请求主窗口退出
        foreach (var p in procs)
        {
            try { if (!p.HasExited) p.CloseMainWindow(); } catch { }
        }

        // 2) 等待优雅退出（最多 8 秒）
        var deadline = DateTime.UtcNow.AddSeconds(8);
        foreach (var p in procs)
        {
            try
            {
                while (!p.HasExited && DateTime.UtcNow < deadline)
                    p.WaitForExit(250);
            }
            catch { }
        }

        // 3) 仍未退出的，强制结束
        foreach (var p in procs)
        {
            try { if (!p.HasExited) p.Kill(); } catch { }
        }

        // 4) 等待进程真正结束（最多再等 3 秒）
        foreach (var p in procs)
        {
            try
            {
                if (!p.HasExited) p.WaitForExit(3000);
            }
            catch { }
        }
    }

    /// <summary>
    /// 手动删除本地商店中的 .sdzip 安装包（仅删除包文件与元信息，不影响已安装的应用）。
    /// </summary>
    public static bool DeleteLocalPackage(ShopPackage package, DatabaseManager db)
    {
        try
        {
            if (File.Exists(package.SdzipPath))
                File.Delete(package.SdzipPath);
        }
        catch
        {
            return false;
        }

        db.DeleteShopPackage(package.SdzipPath);

        // 已安装的包不应被简单"删除安装包"操作移除（保留作升级源）；这里仅清引用
        if (!package.IsInstalled && !package.IsUninstalled)
        {
            package.AppEntryId = null;
        }
        return true;
    }

    /// <summary>
    /// 清理过期的 .sdzip 安装包：仅删除"未安装"且超过 keep_days（默认30天）的包及其元信息。
    /// 已安装的包即使过期也保留，以作为升级来源。返回被删除的包路径列表。
    /// </summary>
    public static List<string> CleanupExpiredPackages(DatabaseManager db, int keepDays = 30)
    {
        var shopDir = GetShopDirectory();
        var removed = new List<string>();

        if (!Directory.Exists(shopDir))
            return removed;

        foreach (var sdzipPath in Directory.GetFiles(shopDir, "*.sdzip"))
        {
            var added = db.GetShopPackageAddedTime(sdzipPath);
            if (added == null) continue; // 尚未登记，跳过

            // 读取包的 abbreviation 以判断对应应用是否已安装
            ShopPackage? pkg = null;
            try { pkg = ReadPackageInfo(sdzipPath, GetCacheDirectory()); } catch { }
            if (pkg == null) continue;

            pkg.AddedTime = added.Value;
            pkg.KeepDays = keepDays;

            // 仅当该包对应应用未安装时才允许清理
            var existing = db.GetAppByAbbreviation(pkg.Abbreviation.ToUpperInvariant());
            var isInstalled = existing != null && !existing.IsUninstalled;

            if (!isInstalled && pkg.IsExpired)
            {
                try
                {
                    File.Delete(sdzipPath);
                    db.DeleteShopPackage(sdzipPath);
                    removed.Add(sdzipPath);
                }
                catch { /* Best-effort cleanup */ }
            }
        }

        return removed;
    }
}
