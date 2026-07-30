using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SuperDucker.Shared;
using SuperDucker.Shared.Data;
using SuperDucker.Shared.Models;

namespace SuperDucker.Cli;

class Program
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0)
        {
            ShowHelp();
            return 0;
        }

        using var db = new DatabaseManager(DatabaseManager.GetDefaultDbPath());

        var command = args[0].ToLowerInvariant();

        return command switch
        {
            "add" => HandleAdd(db, args[1..]),
            "remove" or "rm" => HandleRemove(db, args[1..]),
            "list" or "ls" => HandleList(db, args[1..]),
            "s" => HandleRunAsAdmin(db, args[1..]),
            "d" => HandleOpenDirectory(db, args[1..]),
            "e" => HandleShowDescription(db, args[1..]),
            "edit" => HandleEdit(db, args[1..]),
            "icon" => HandleIcon(db, args[1..]),
            "import" => HandleImport(db, args[1..]),
            "pack" => HandlePack(db, args[1..]),
            "pack-gui" => HandlePackGui(),
            "rescan" => HandleRescan(db),
            "setup" => HandleSetup(),
            "repair" => HandleRepair(db),
            "url" => HandleUrl(db, args[1..]),
            "help" or "-h" or "--help" => ShowHelp(),
            _ => HandleRun(db, args) // Default: try to launch by abbreviation
        };
    }

    // ═══════════════════════════════════════════
    //  sd add <abbr> <path> [--name X] [--desc X] [--cat X] [--tab X]
    // ═══════════════════════════════════════════
    static int HandleAdd(DatabaseManager db, string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("用法: sd add <缩写> <目标路径> [--name <友好名称>] [--desc <描述>] [--cat <分类>] [--tab <标签页>]");
            return 1;
        }

        var abbreviation = args[0].ToUpperInvariant();
        var targetPath = args[1];

        // Parse optional parameters
        string? friendlyName = null, description = null, category = null, tabName = null;
        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--name" when i + 1 < args.Length:
                    friendlyName = args[++i]; break;
                case "--desc" when i + 1 < args.Length:
                    description = args[++i]; break;
                case "--cat" when i + 1 < args.Length:
                    category = args[++i]; break;
                case "--tab" when i + 1 < args.Length:
                    tabName = args[++i]; break;
            }
        }

        // Resolve tab
        int? tabId = null;
        if (tabName != null)
        {
            tabId = ResolveTabId(db, tabName);
            if (tabId == null)
            {
                Console.Error.WriteLine($"错误: 找不到标签页 '{tabName}'。请先在面板中创建标签页。");
                return 1;
            }
        }

        // Validate target path
        if (!File.Exists(targetPath))
        {
            Console.Error.WriteLine($"错误: 找不到目标文件: {targetPath}");
            return 1;
        }

        // Check abbreviation uniqueness
        if (db.AbbreviationExists(abbreviation))
        {
            var conflict = db.FindAbbreviationConflict(abbreviation);
            Console.Error.WriteLine($"错误: 缩写 '{abbreviation}' 已被占用{(conflict != null ? $" ({conflict})" : "")}。");
            return 1;
        }

        // Resolve to absolute path
        targetPath = Path.GetFullPath(targetPath);

        var entry = new AppEntry
        {
            Abbreviation = abbreviation,
            FriendlyName = friendlyName,
            TargetPath = targetPath,
            Description = description,
            Category = category,
            TabId = tabId,
            IsBuiltIn = false
        };

        db.AddApp(entry);
        ShortcutManager.CreateShortcut(entry);

        Console.WriteLine($"[OK] 已添加 '{abbreviation}' -> {targetPath}");
        Console.WriteLine($"     快捷方式已创建: link\\{abbreviation}.lnk");
        if (tabName != null) Console.WriteLine($"     标签页: {tabName}");
        return 0;
    }

    // ═══════════════════════════════════════════
    //  sd remove <abbr>
    // ═══════════════════════════════════════════
    static int HandleRemove(DatabaseManager db, string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("用法: sd remove <缩写>");
            return 1;
        }

        var abbreviation = args[0].ToUpperInvariant();
        var entry = db.GetAppByAbbreviation(abbreviation);
        if (entry == null)
        {
            // Also check URLs
            var url = db.GetUrlByAbbreviation(abbreviation);
            if (url != null)
            {
                db.DeleteUrl(url.Id);
                ShortcutManager.DeleteUrlShortcut(abbreviation);
                Console.WriteLine($"[OK] 已删除 URL '{abbreviation}'");
                return 0;
            }
            Console.Error.WriteLine($"错误: 找不到 '{abbreviation}'。");
            return 1;
        }

        db.DeleteApp(entry.Id);
        ShortcutManager.DeleteShortcut(abbreviation);

        Console.WriteLine($"[OK] 已删除 '{abbreviation}' ({entry.DisplayName})");
        return 0;
    }

    // ═══════════════════════════════════════════
    //  sd list [--cat X] [--tab X]
    // ═══════════════════════════════════════════
    static int HandleList(DatabaseManager db, string[] args)
    {
        string? filterCategory = null;
        string? filterTab = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].ToLowerInvariant() == "--cat" && i + 1 < args.Length)
                filterCategory = args[++i];
            else if (args[i].ToLowerInvariant() == "--tab" && i + 1 < args.Length)
                filterTab = args[++i];
        }

        // Build tab lookup for display
        var tabs = db.GetAllTabs();
        var tabNames = tabs.ToDictionary(t => t.Id, t => t.Name);

        // Filter by tab if specified
        int? filterTabId = null;
        if (filterTab != null)
        {
            filterTabId = ResolveTabId(db, filterTab);
            if (filterTabId == null)
            {
                Console.Error.WriteLine($"错误: 找不到标签页 '{filterTab}'。");
                return 1;
            }
        }

        Console.WriteLine("=== 程序 ===");
        var apps = filterCategory != null
            ? db.GetAppsByCategory(filterCategory)
            : filterTabId != null
                ? db.GetAppsByTab(filterTabId.Value)
                : db.GetAllApps();
        if (apps.Count == 0)
        {
            Console.WriteLine("  (无)");
        }
        foreach (var app in apps)
        {
            var friendly = app.FriendlyName != null ? $" ({app.FriendlyName})" : "";
            var cat = app.Category != null ? $" [{app.Category}]" : "";
            var tab = app.TabId != null && tabNames.TryGetValue(app.TabId.Value, out var tn) ? $" <{tn}>" : "";
            var builtin = app.IsBuiltIn ? " *" : "";
            Console.WriteLine($"  {app.Abbreviation,-12}{friendly}{cat}{tab}{builtin}");
            Console.WriteLine($"    -> {app.TargetPath}");
        }

        Console.WriteLine();
        Console.WriteLine("=== 网址 ===");
        var urls = filterTabId != null
            ? db.GetUrlsByTab(filterTabId.Value)
            : db.GetAllUrls();
        if (urls.Count == 0)
        {
            Console.WriteLine("  (无)");
        }
        foreach (var url in urls)
        {
            var friendly = url.FriendlyName != null ? $" ({url.FriendlyName})" : "";
            var cat = url.Category != null ? $" [{url.Category}]" : "";
            var tab = url.TabId != null && tabNames.TryGetValue(url.TabId.Value, out var tn) ? $" <{tn}>" : "";
            Console.WriteLine($"  {url.Abbreviation,-12}{friendly}{cat}{tab}");
            Console.WriteLine($"    -> {url.Url}");
        }

        // Show PATH status
        Console.WriteLine();
        var inPath = ShortcutManager.IsLinkInPath();
        Console.WriteLine($"PATH 已注册: {(inPath ? "是" : "否 (运行 'sd setup' 注册)")}");

        return 0;
    }

    // ═══════════════════════════════════════════
    //  sd <abbr> (default: launch)
    // ═══════════════════════════════════════════
    static int HandleRun(DatabaseManager db, string[] args)
    {
        var abbreviation = args[0].ToUpperInvariant();

        var app = db.GetAppByAbbreviation(abbreviation);
        if (app != null)
        {
            LaunchApp(app, false);
            return 0;
        }

        var url = db.GetUrlByAbbreviation(abbreviation);
        if (url != null)
        {
            OpenUrl(url.Url);
            return 0;
        }

        Console.Error.WriteLine($"错误: 找不到 '{abbreviation}'。使用 'sd list' 查看已注册条目。");
        return 1;
    }

    // ═══════════════════════════════════════════
    //  sd s <abbr> (run as admin)
    // ═══════════════════════════════════════════
    static int HandleRunAsAdmin(DatabaseManager db, string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("用法: sd s <缩写>");
            return 1;
        }

        var abbreviation = args[0].ToUpperInvariant();
        var app = db.GetAppByAbbreviation(abbreviation);
        if (app == null)
        {
            Console.Error.WriteLine($"错误: 找不到 '{abbreviation}'。");
            return 1;
        }

        LaunchApp(app, true);
        return 0;
    }

    // ═══════════════════════════════════════════
    //  sd d <abbr> (open directory)
    // ═══════════════════════════════════════════
    static int HandleOpenDirectory(DatabaseManager db, string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("用法: sd d <缩写>");
            return 1;
        }

        var abbreviation = args[0].ToUpperInvariant();
        var app = db.GetAppByAbbreviation(abbreviation);
        if (app == null)
        {
            Console.Error.WriteLine($"错误: 找不到 '{abbreviation}'。");
            return 1;
        }

        var dir = app.WorkingDirectory ?? Path.GetDirectoryName(app.TargetPath);
        if (dir != null && Directory.Exists(dir))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{app.TargetPath}\"",
                UseShellExecute = true
            });
            Console.WriteLine($"[OK] 已打开: {dir}");
        }
        else
        {
            Console.Error.WriteLine($"错误: 目录不存在: {dir}");
            return 1;
        }
        return 0;
    }

    // ═══════════════════════════════════════════
    //  sd e <abbr> (show description via balloon tip)
    // ═══════════════════════════════════════════
    static int HandleShowDescription(DatabaseManager db, string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("用法: sd e <缩写>");
            return 1;
        }

        var abbreviation = args[0].ToUpperInvariant();

        var app = db.GetAppByAbbreviation(abbreviation);
        if (app != null)
        {
            ShowBalloonTip(app.Abbreviation, app.Description ?? "(无描述)", 5000);
            return 0;
        }

        var url = db.GetUrlByAbbreviation(abbreviation);
        if (url != null)
        {
            ShowBalloonTip(url.Abbreviation, url.Description ?? url.Url, 5000);
            return 0;
        }

        Console.Error.WriteLine($"错误: 找不到 '{abbreviation}'。");
        return 1;
    }

    // ═══════════════════════════════════════════
    //  sd edit <abbr> [--name X] [--desc X] [--cat X] [--tab X]
    //               [--path X] [--url X] [--abbr X]
    // ═══════════════════════════════════════════
    static int HandleEdit(DatabaseManager db, string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("用法: sd edit <缩写> [--name <名称>] [--desc <描述>] [--cat <分类>] [--tab <标签页>]");
            Console.Error.WriteLine("                                          [--path <新路径>] [--url <新网址>] [--abbr <新缩写>]");
            return 1;
        }

        var abbreviation = args[0].ToUpperInvariant();

        string? friendlyName = null, description = null, category = null,
                tabName = null, newPath = null, newUrl = null, newAbbr = null;
        bool clearTab = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--name" when i + 1 < args.Length:
                    friendlyName = args[++i]; break;
                case "--desc" when i + 1 < args.Length:
                    description = args[++i]; break;
                case "--cat" when i + 1 < args.Length:
                    category = args[++i]; break;
                case "--tab" when i + 1 < args.Length:
                    tabName = args[++i];
                    if (tabName == "none" || tabName == "-" || tabName == "") clearTab = true;
                    break;
                case "--path" when i + 1 < args.Length:
                    newPath = args[++i]; break;
                case "--url" when i + 1 < args.Length:
                    newUrl = args[++i]; break;
                case "--abbr" when i + 1 < args.Length:
                    newAbbr = args[++i].ToUpperInvariant(); break;
            }
        }

        // Try app entry first
        var app = db.GetAppByAbbreviation(abbreviation);
        if (app != null)
        {
            if (friendlyName != null) app.FriendlyName = friendlyName;
            if (description != null) app.Description = description;
            if (category != null) app.Category = category;
            if (newPath != null)
            {
                if (!File.Exists(newPath))
                {
                    Console.Error.WriteLine($"错误: 找不到目标文件: {newPath}");
                    return 1;
                }
                app.TargetPath = Path.GetFullPath(newPath);
            }

            // Handle tab change
            if (clearTab)
            {
                app.TabId = null;
            }
            else if (tabName != null)
            {
                var tabId = ResolveTabId(db, tabName);
                if (tabId == null)
                {
                    Console.Error.WriteLine($"错误: 找不到标签页 '{tabName}'。");
                    return 1;
                }
                app.TabId = tabId;
            }

            // Handle abbreviation change
            string oldAbbr = app.Abbreviation;
            if (newAbbr != null && newAbbr != abbreviation)
            {
                if (db.AbbreviationExists(newAbbr))
                {
                    var conflict = db.FindAbbreviationConflict(newAbbr);
                    Console.Error.WriteLine($"错误: 缩写 '{newAbbr}' 已被占用{(conflict != null ? $" ({conflict})" : "")}。");
                    return 1;
                }
                app.Abbreviation = newAbbr;
            }

            db.UpdateApp(app);

            // Rename shortcut if abbreviation changed
            if (newAbbr != null && newAbbr != abbreviation)
            {
                ShortcutManager.RenameShortcut(abbreviation, newAbbr);
            }
            // Regenerate shortcut if target path changed
            if (newPath != null || newAbbr != null)
            {
                ShortcutManager.CreateShortcut(app);
            }

            Console.WriteLine($"[OK] 已更新 '{app.Abbreviation}' ({app.DisplayName})");
            return 0;
        }

        // Try URL entry
        var url = db.GetUrlByAbbreviation(abbreviation);
        if (url != null)
        {
            if (friendlyName != null) url.FriendlyName = friendlyName;
            if (description != null) url.Description = description;
            if (category != null) url.Category = category;
            if (newUrl != null) url.Url = newUrl;

            if (clearTab)
            {
                url.TabId = null;
            }
            else if (tabName != null)
            {
                var tabId = ResolveTabId(db, tabName);
                if (tabId == null)
                {
                    Console.Error.WriteLine($"错误: 找不到标签页 '{tabName}'。");
                    return 1;
                }
                url.TabId = tabId;
            }

            string oldAbbr = url.Abbreviation;
            if (newAbbr != null && newAbbr != abbreviation)
            {
                if (db.AbbreviationExists(newAbbr))
                {
                    var conflict = db.FindAbbreviationConflict(newAbbr);
                    Console.Error.WriteLine($"错误: 缩写 '{newAbbr}' 已被占用{(conflict != null ? $" ({conflict})" : "")}。");
                    return 1;
                }
                url.Abbreviation = newAbbr;
            }

            db.UpdateUrl(url);
            Console.WriteLine($"[OK] 已更新 URL '{url.Abbreviation}' ({url.DisplayName})");
            return 0;
        }

        Console.Error.WriteLine($"错误: 找不到 '{abbreviation}'。");
        return 1;
    }

    // ═══════════════════════════════════════════
    //  sd icon <abbr> <icon-path>        设置自定义图标
    //  sd icon <abbr> --fetch             抓取网站图标
    // ═══════════════════════════════════════════
    static int HandleIcon(DatabaseManager db, string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("用法: sd icon <缩写> <图标文件路径>");
            Console.Error.WriteLine("      sd icon <缩写> --fetch          (仅 URL 条目: 抓取网站图标)");
            return 1;
        }

        var abbreviation = args[0].ToUpperInvariant();
        var iconArg = args[1];
        bool fetch = iconArg.Equals("--fetch", StringComparison.OrdinalIgnoreCase);

        if (!fetch)
        {
            // Set custom icon file for app or URL entry
            if (!File.Exists(iconArg))
            {
                Console.Error.WriteLine($"错误: 找不到图标文件: {iconArg}");
                return 1;
            }

            var iconPath = Path.GetFullPath(iconArg);

            var app = db.GetAppByAbbreviation(abbreviation);
            if (app != null)
            {
                app.IconPath = iconPath;
                db.UpdateApp(app);
                ShortcutManager.CreateShortcut(app); // regenerate with new icon
                Console.WriteLine($"[OK] 已更新 '{abbreviation}' 的图标: {iconPath}");
                return 0;
            }

            var url = db.GetUrlByAbbreviation(abbreviation);
            if (url != null)
            {
                url.IconPath = iconPath;
                db.UpdateUrl(url);
                Console.WriteLine($"[OK] 已更新 URL '{abbreviation}' 的图标: {iconPath}");
                return 0;
            }

            Console.Error.WriteLine($"错误: 找不到 '{abbreviation}'。");
            return 1;
        }
        else
        {
            // Fetch favicon for URL entry
            var url = db.GetUrlByAbbreviation(abbreviation);
            if (url == null)
            {
                Console.Error.WriteLine("错误: --fetch 仅适用于 URL 条目。");
                return 1;
            }

            Console.Write("正在抓取图标...");
            var savePath = WebHelper.GetIconPath(abbreviation);
            var result = WebHelper.FetchFaviconAsync(url.Url, savePath).GetAwaiter().GetResult();

            if (result != null)
            {
                url.IconPath = result;
                db.UpdateUrl(url);
                Console.WriteLine($" 完成");
                Console.WriteLine($"[OK] 已保存图标: {result}");
            }
            else
            {
                Console.WriteLine(" 失败");
                Console.Error.WriteLine("错误: 无法抓取图标。");
                return 1;
            }
            return 0;
        }
    }

    // ═══════════════════════════════════════════
    //  sd import <file.sdzip>
    // ═══════════════════════════════════════════
    static int HandleImport(DatabaseManager db, string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("用法: sd import <包文件.sdzip>");
            Console.Error.WriteLine();
            Console.Error.WriteLine("从 .sdzip 包导入程序到 SuperDucker。");
            Console.Error.WriteLine();
            Console.Error.WriteLine("示例:");
            Console.Error.WriteLine("  sd import localshop/drawio.sdzip");
            return 1;
        }

        var sdzipPath = Path.GetFullPath(args[0]);
        if (!File.Exists(sdzipPath))
        {
            Console.Error.WriteLine($"错误: 文件不存在: {sdzipPath}");
            return 1;
        }

        Console.WriteLine($"正在导入: {sdzipPath}");

        // Open zip and read manifest
        PackageManifest? manifest = null;
        ZipArchiveEntry? iconEntry = null;
        string? extractedIconPath = null;
        string packageId;
        string abbreviation;
        string targetDir;
        string mainExe;

        using (var zipStream = File.OpenRead(sdzipPath))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
        {
            // Read manifest.json
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry == null)
            {
                Console.Error.WriteLine("错误: 包中缺少 manifest.json");
                return 1;
            }

            using (var reader = new StreamReader(manifestEntry.Open()))
            {
                var json = reader.ReadToEnd();
                manifest = PackageManifest.FromJson(json);
            }

            if (manifest == null)
            {
                Console.Error.WriteLine("错误: manifest.json 解析失败");
                return 1;
            }

            packageId = manifest.Id.ToLowerInvariant();
            abbreviation = manifest.Abbreviation?.ToUpperInvariant() ?? packageId.ToUpperInvariant();
            mainExe = manifest.MainExe;

            var appDir = DatabaseManager.GetAppDirectory();
            targetDir = Path.Combine(appDir, packageId);

            if (Directory.Exists(targetDir))
            {
                Console.Error.WriteLine($"错误: 目录已存在: {targetDir}");
                Console.Error.WriteLine("      请先删除或使用其他包 ID。");
                return 1;
            }

            Directory.CreateDirectory(targetDir);

            // Extract all entries
            foreach (var entry in archive.Entries)
            {
                // Save manifest.json copy to target dir for disaster recovery
                if (entry.FullName == "manifest.json")
                {
                    var manifestCopyPath = Path.Combine(targetDir, "manifest.json");
                    entry.ExtractToFile(manifestCopyPath, true);
                    continue;
                }

                // Skip directory entries
                if (string.IsNullOrEmpty(entry.Name)) continue;

                // Detect icon files at zip root (icon.png, icon.ico, etc.)
                if (!entry.FullName.Contains('/') && entry.Name.StartsWith("icon.", StringComparison.OrdinalIgnoreCase))
                {
                    iconEntry = entry;
                    continue;
                }

                // Strip "app/" prefix for extraction into app/{packageId}/
                var entryPath = entry.FullName;
                if (entryPath.StartsWith("app/"))
                    entryPath = entryPath[4..];

                var targetPath = Path.Combine(targetDir, entryPath);
                var targetFileDir = Path.GetDirectoryName(targetPath);
                if (targetFileDir != null) Directory.CreateDirectory(targetFileDir);

                // Zip slip protection
                if (!Path.GetFullPath(targetPath).StartsWith(Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"警告: 跳过不安全路径: {entry.FullName}");
                    continue;
                }

                entry.ExtractToFile(targetPath, true);
            }

            // Extract icon to icons/ directory (must be inside using block while archive is open)
            if (iconEntry != null)
            {
                var iconsDir = WebHelper.GetIconsDirectory();
                Directory.CreateDirectory(iconsDir);
                var iconExt = Path.GetExtension(iconEntry.Name);
                extractedIconPath = Path.Combine(iconsDir, $"{abbreviation}{iconExt}");
                iconEntry.ExtractToFile(extractedIconPath, true);
                Console.WriteLine($"  [图标] 已提取到: {extractedIconPath}");
            }
        }

        // Ensure unique abbreviation
        if (db.AbbreviationExists(abbreviation))
        {
            var shortAbbr = new string(manifest.Name.Where(char.IsAsciiLetterOrDigit).Take(4).ToArray()).ToUpperInvariant();
            if (!string.IsNullOrEmpty(shortAbbr) && !db.AbbreviationExists(shortAbbr))
            {
                Console.WriteLine($"  缩写 '{abbreviation}' 已被占用，改用 '{shortAbbr}'");
                abbreviation = shortAbbr;
            }
            else
            {
                Console.Error.WriteLine($"警告: 缩写 '{abbreviation}' 已被占用，请手动使用 sd add 注册。");
                return 0;
            }
        }

        var mainExePath = Path.Combine(targetDir, mainExe);
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

        // Auto-create tab from first tag and assign app to it
        if (manifest.Tags.Count > 0)
        {
            var tagName = manifest.Tags[0];
            var tabs = db.GetAllTabs();
            var tab = tabs.FirstOrDefault(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
            if (tab == null)
            {
                tab = db.AddTab(new TabEntry { Name = tagName, SortOrder = tabs.Count });
                Console.WriteLine($"  [标签] 已创建标签页 '{tagName}'");
            }
            db.SetEntryTab("app_entries", appEntry.Id, tab.Id);
        }

        Console.WriteLine();
        Console.WriteLine($"[OK] 已导入并注册 '{abbreviation}' ({manifest.Name})");
        Console.WriteLine($"     包 ID: {packageId}");
        Console.WriteLine($"     路径:  {mainExePath}");
        Console.WriteLine($"     版本:  {manifest.Version}");
        if (!string.IsNullOrEmpty(manifest.Description))
            Console.WriteLine($"     描述:  {manifest.Description}");

        return 0;
    }

    // ═══════════════════════════════════════════
    //  sd pack <source-dir> <package-id> [options]
    // ═══════════════════════════════════════════
    static int HandlePack(DatabaseManager db, string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("用法: sd pack <源目录> <包ID> [选项]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("选项:");
            Console.Error.WriteLine("  --name <名称>          显示名称");
            Console.Error.WriteLine("  --abbr <缩写>          Win+R 启动缩写 (默认从包ID生成)");
            Console.Error.WriteLine("  --version <版本>       版本号 (默认 1.0.0)");
            Console.Error.WriteLine("  --main <主程序.exe>    主程序相对路径 (不指定则自动检测)");
            Console.Error.WriteLine("  --icon <图标路径>      自定义图标文件 (.ico/.png)");
            Console.Error.WriteLine("  --author <作者>        作者");
            Console.Error.WriteLine("  --homepage <网址>      官方网站");
            Console.Error.WriteLine("  --desc <描述>          软件描述");
            Console.Error.WriteLine("  --cat <分类>           分类 (逗号分隔)");
            Console.Error.WriteLine("  --tags <标签>          标签 (逗号分隔)");
            Console.Error.WriteLine("  --from <缩写>          从已注册条目拉取元数据");
            Console.Error.WriteLine("  -o <输出路径>          输出文件路径");
            Console.Error.WriteLine("  --import               打包后导入到 SuperDucker");
            Console.Error.WriteLine();
            Console.Error.WriteLine("示例:");
            Console.Error.WriteLine("  sd pack C:\\Tools\\NotepadPP notepad-plus-plus --name \"Notepad++\" --version 8.6.9");
            return 1;
        }

        var sourceDir = Path.GetFullPath(args[0]);
        var packageId = args[1].ToLowerInvariant();
        var localShopDir = Path.Combine(Directory.GetCurrentDirectory(), "localshop");
        Directory.CreateDirectory(localShopDir);
        var outputPath = Path.Combine(localShopDir, $"{packageId}.sdzip");

        string? name = null, version = "1.0.0", mainExe = null,
                author = null, homepage = null, description = null,
                categories = null, tags = null, fromAbbr = null,
                explicitAbbr = null, explicitIcon = null;
        bool import = false;

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--name" when i + 1 < args.Length:
                    name = args[++i]; break;
                case "--abbr" when i + 1 < args.Length:
                    explicitAbbr = args[++i].ToUpperInvariant(); break;
                case "--version" when i + 1 < args.Length:
                    version = args[++i]; break;
                case "--main" when i + 1 < args.Length:
                    mainExe = args[++i]; break;
                case "--icon" when i + 1 < args.Length:
                    explicitIcon = args[++i]; break;
                case "--author" when i + 1 < args.Length:
                    author = args[++i]; break;
                case "--homepage" when i + 1 < args.Length:
                    homepage = args[++i]; break;
                case "--desc" when i + 1 < args.Length:
                    description = args[++i]; break;
                case "--cat" when i + 1 < args.Length:
                    categories = args[++i]; break;
                case "--tags" when i + 1 < args.Length:
                    tags = args[++i]; break;
                case "-o" when i + 1 < args.Length:
                    outputPath = args[++i]; break;
                case "--from" when i + 1 < args.Length:
                    fromAbbr = args[++i].ToUpperInvariant(); break;
                case "--import":
                    import = true; break;
            }
        }

        // If --from is specified, pull metadata from an existing registered entry
        string? customIconPath = explicitIcon;
        if (fromAbbr != null)
        {
            var app = db.GetAppByAbbreviation(fromAbbr);
            if (app != null)
            {
                // Auto-fill source directory from the registered exe's parent dir
                if (args[0] == ".") // allow "." as placeholder when using --from
                    sourceDir = Path.GetDirectoryName(app.TargetPath) ?? sourceDir;

                if (name == null) name = app.FriendlyName;
                if (description == null) description = app.Description;
                if (categories == null && app.Category != null) categories = app.Category;
                if (mainExe == null) mainExe = Path.GetFileName(app.TargetPath);
                if (customIconPath == null && !string.IsNullOrEmpty(app.IconPath) && File.Exists(app.IconPath))
                    customIconPath = app.IconPath;

                // Use the abbreviation from the entry (unless --abbr overrides)
                fromAbbr = app.Abbreviation;

                Console.WriteLine($"从已注册条目 '{app.Abbreviation}' 拉取元数据:");
                Console.WriteLine($"  名称: {name ?? app.DisplayName}");
                Console.WriteLine($"  描述: {description ?? "(无)"}");
                Console.WriteLine($"  分类: {categories ?? "(无)"}");
                if (customIconPath != null) Console.WriteLine($"  图标: {customIconPath}");
            }
            else
            {
                Console.Error.WriteLine($"警告: 找不到已注册条目 '{fromAbbr}'，将仅使用命令行参数。");
                fromAbbr = null;
            }
        }

        // Validate source directory
        if (!Directory.Exists(sourceDir))
        {
            Console.Error.WriteLine($"错误: 源目录不存在: {sourceDir}");
            return 1;
        }

        // Find main executable
        if (mainExe == null)
        {
            var exeFiles = Directory.GetFiles(sourceDir, "*.exe", SearchOption.TopDirectoryOnly);
            if (exeFiles.Length == 0)
            {
                // Search one level deeper
                exeFiles = Directory.GetFiles(sourceDir, "*.exe", SearchOption.AllDirectories)
                    .Where(f => !f.Contains(Path.DirectorySeparatorChar + "unins", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            if (exeFiles.Length == 0)
            {
                Console.Error.WriteLine("错误: 未找到可执行文件，请使用 --main 指定主程序路径。");
                return 1;
            }
            else if (exeFiles.Length == 1)
            {
                mainExe = Path.GetRelativePath(sourceDir, exeFiles[0]);
            }
            else
            {
                Console.Error.WriteLine($"找到多个可执行文件，请使用 --main 指定:");
                foreach (var f in exeFiles.Take(5))
                    Console.Error.WriteLine($"  {Path.GetRelativePath(sourceDir, f)}");
                return 1;
            }
        }
        else
        {
            // Validate specified exe exists
            var exePath = Path.Combine(sourceDir, mainExe);
            if (!File.Exists(exePath))
            {
                Console.Error.WriteLine($"错误: 指定的主程序不存在: {exePath}");
                return 1;
            }
        }

        // Auto-extract icon from main exe if no custom icon was specified
        if (customIconPath == null)
        {
            var exeFullPath = Path.Combine(sourceDir, mainExe);
            var tempIconPath = Path.Combine(Path.GetTempPath(), $"sd_pack_{packageId}.ico");
            var extracted = IconHelper.ExtractAndSaveIcon(exeFullPath, tempIconPath);
            if (extracted != null)
            {
                customIconPath = extracted;
                Console.WriteLine($"[图标] 已从 {mainExe} 提取图标");
            }
        }

        // Determine abbreviation: explicit --abbr > --from entry > package ID
        var abbreviation = explicitAbbr ?? fromAbbr ?? packageId.ToUpperInvariant();

        // Build manifest
        var manifest = new PackageManifest
        {
            Id = packageId,
            Abbreviation = abbreviation,
            Name = name ?? Path.GetFileName(sourceDir),
            Version = version,
            Author = author,
            Homepage = homepage,
            Description = description,
            MainExe = mainExe,
            ExtractSubDir = "app",
            Categories = categories?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList() ?? new List<string>(),
            Tags = tags?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList() ?? new List<string>(),
            InstallActions = new InstallActions(),
            UninstallActions = new UninstallActions { RemoveDir = true },
            Requirements = new PackageRequirements
            {
                MinWindows = "10",
                Architecture = new List<string> { "x64" }
            }
        };

        // Collect files
        var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        Console.WriteLine($"正在打包 '{manifest.Name}' v{manifest.Version}...");
        Console.WriteLine($"  源目录: {sourceDir}");
        Console.WriteLine($"  主程序: {mainExe}");
        Console.WriteLine($"  文件数: {files.Length}");

        // Create zip
        outputPath = Path.GetFullPath(outputPath);
        if (File.Exists(outputPath)) File.Delete(outputPath);

        using (var zipStream = new FileStream(outputPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            // Write manifest.json at root
            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(manifestEntry.Open()))
            {
                writer.Write(manifest.ToJson());
            }

            // Package custom icon at root (if available)
            if (customIconPath != null && File.Exists(customIconPath))
            {
                var iconExt = Path.GetExtension(customIconPath);
                archive.CreateEntryFromFile(customIconPath, $"icon{iconExt}", CompressionLevel.Optimal);
                Console.WriteLine($"  图标已打包: {customIconPath}");
            }

            // Add all source files under app/ prefix
            int count = 0;
            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
                var entryPath = $"app/{relativePath}";

                // Skip common junk files
                if (relativePath.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) &&
                    !relativePath.Contains("python", StringComparison.OrdinalIgnoreCase))
                    continue;

                archive.CreateEntryFromFile(file, entryPath, CompressionLevel.Optimal);
                count++;

                // Progress indicator
                if (count % 100 == 0)
                    Console.Write($"\r  已添加 {count}/{files.Length} 个文件...");
            }
            Console.WriteLine($"\r  已打包 {count} 个文件                    ");
        }

        // Clean up temp icon file if we extracted one from exe
        if (customIconPath != null && customIconPath.StartsWith(Path.GetTempPath()))
        {
            try { File.Delete(customIconPath); } catch { }
        }

        // Compute SHA-256
        var fileInfo = new FileInfo(outputPath);
        string sha256;
        using (var stream = File.OpenRead(outputPath))
        {
            var hash = SHA256.HashData(stream);
            sha256 = Convert.ToHexString(hash).ToLowerInvariant();
        }

        Console.WriteLine();
        Console.WriteLine($"[OK] 打包完成");
        Console.WriteLine($"     包 ID:   {packageId}");
        Console.WriteLine($"     文件:    {outputPath}");
        Console.WriteLine($"     大小:    {FileHelper.FormatSize(fileInfo.Length)}");
        Console.WriteLine($"     SHA-256: {sha256}");

        // Import to SuperDucker if requested
        if (import)
        {
            Console.WriteLine();
            Console.WriteLine("正在导入到 SuperDucker...");

            var appDir = DatabaseManager.GetAppDirectory();
            var targetDir = Path.Combine(appDir, packageId);

            if (Directory.Exists(targetDir))
            {
                Console.Error.WriteLine($"错误: 目录已存在: {targetDir}");
                Console.Error.WriteLine("      请先删除或使用其他包 ID。");
                return 1;
            }

            // Extract to app/{packageId}/
            Directory.CreateDirectory(targetDir);
            ZipArchiveEntry? iconEntry = null;
            string? extractedIconPath = null;
            using (var zipStream = File.OpenRead(outputPath))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    // Save manifest.json copy to target dir for recovery
                    if (entry.FullName == "manifest.json")
                    {
                        var manifestCopyPath = Path.Combine(targetDir, "manifest.json");
                        entry.ExtractToFile(manifestCopyPath, true);
                        continue;
                    }
                    // Skip directory entries
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    // Detect icon files at zip root (icon.png, icon.ico, etc.)
                    if (!entry.FullName.Contains('/') && entry.Name.StartsWith("icon.", StringComparison.OrdinalIgnoreCase))
                    {
                        iconEntry = entry;
                        continue;
                    }

                    var entryPath = entry.FullName;
                    // Strip "app/" prefix for extraction into app/{packageId}/
                    if (entryPath.StartsWith("app/"))
                        entryPath = entryPath[4..];

                    var targetPath = Path.Combine(targetDir, entryPath);
                    var targetFileDir = Path.GetDirectoryName(targetPath);
                    if (targetFileDir != null) Directory.CreateDirectory(targetFileDir);

                    // Zip slip protection
                    if (!Path.GetFullPath(targetPath).StartsWith(Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Error.WriteLine($"警告: 跳过不安全路径: {entry.FullName}");
                        continue;
                    }

                    entry.ExtractToFile(targetPath, true);
                }

                // Extract icon to icons/ directory (must be inside using block while archive is open)
                if (iconEntry != null)
                {
                    var iconsDir = WebHelper.GetIconsDirectory();
                    Directory.CreateDirectory(iconsDir);
                    var iconExt = Path.GetExtension(iconEntry.Name);
                    var iconAbbr = manifest.Abbreviation?.ToUpperInvariant() ?? packageId.ToUpperInvariant();
                    extractedIconPath = Path.Combine(iconsDir, $"{iconAbbr}{iconExt}");
                    iconEntry.ExtractToFile(extractedIconPath, true);
                    Console.WriteLine($"[图标] 已提取到: {extractedIconPath}");
                }
            }

            // Register in SuperDucker
            var mainExePath = Path.Combine(targetDir, mainExe);
            abbreviation = manifest.Abbreviation?.ToUpperInvariant() ?? packageId.ToUpperInvariant();

            // Ensure unique abbreviation
            if (db.AbbreviationExists(abbreviation))
            {
                // Try shorter abbreviation from package name
                var shortAbbr = new string(manifest.Name.Where(char.IsAsciiLetterOrDigit).Take(4).ToArray()).ToUpperInvariant();
                if (!string.IsNullOrEmpty(shortAbbr) && !db.AbbreviationExists(shortAbbr))
                    abbreviation = shortAbbr;
                else
                {
                    Console.Error.WriteLine($"警告: 缩写 '{abbreviation}' 已被占用，请手动使用 sd add 注册。");
                    return 0;
                }
            }

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

            // Auto-create tab from first tag and assign app to it
            if (manifest.Tags.Count > 0)
            {
                var tagName = manifest.Tags[0];
                var tabs = db.GetAllTabs();
                var tab = tabs.FirstOrDefault(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
                if (tab == null)
                {
                    tab = db.AddTab(new TabEntry { Name = tagName, SortOrder = tabs.Count });
                    Console.WriteLine($"  [标签] 已创建标签页 '{tagName}'");
                }
                db.SetEntryTab("app_entries", appEntry.Id, tab.Id);
            }

            Console.WriteLine($"[OK] 已导入并注册 '{abbreviation}' ({manifest.Name})");
            Console.WriteLine($"     路径: {mainExePath}");
        }

        return 0;
    }

    // ═══════════════════════════════════════════
    //  sd pack-gui (launch GUI pack dialog)
    // ═══════════════════════════════════════════
    static int HandlePackGui()
    {
        var rootDir = AppContext.BaseDirectory;
        var panelExe = Path.Combine(rootDir, "superducker.exe");

        if (!File.Exists(panelExe))
        {
            Console.Error.WriteLine($"错误: 找不到 superducker.exe");
            Console.Error.WriteLine($"      期望路径: {panelExe}");
            Console.Error.WriteLine($"      请确保 sd.exe 和 superducker.exe 在同一目录。");
            return 1;
        }

        var psi = new ProcessStartInfo
        {
            FileName = panelExe,
            Arguments = "--pack",
            UseShellExecute = true
        };
        Process.Start(psi);
        Console.WriteLine("[OK] 已启动打包工具");
        return 0;
    }

    // ═══════════════════════════════════════════
    //  sd rescan (recover apps from app/ directory)
    // ═══════════════════════════════════════════
    static int HandleRescan(DatabaseManager db)
    {
        Console.WriteLine("正在扫描 app/ 目录...");
        var result = RescanHelper.Rescan(db);

        Console.WriteLine();
        Console.WriteLine($"扫描完成: 共 {result.TotalScanned} 个目录");
        Console.WriteLine($"  恢复: {result.Recovered}");
        Console.WriteLine($"  跳过: {result.Skipped} (已注册)");
        Console.WriteLine($"  失败: {result.Failed}");

        if (result.RecoveredNames.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("已恢复:");
            foreach (var name in result.RecoveredNames)
                Console.WriteLine($"  [OK] {name}");
        }

        if (result.Errors.Count > 0)
        {
            Console.WriteLine();
            Console.Error.WriteLine("错误:");
            foreach (var err in result.Errors)
                Console.Error.WriteLine($"  [!] {err}");
        }

        if (result.Recovered == 0 && result.Failed == 0)
            Console.WriteLine("所有程序均已注册，无需恢复。");

        return result.Failed > 0 ? 1 : 0;
    }

    // ═══════════════════════════════════════════
    //  sd setup (register link/ in PATH)
    // ═══════════════════════════════════════════
    static int HandleSetup()
    {
        // Ensure directories exist
        Directory.CreateDirectory(DatabaseManager.GetLinkDirectory());
        Directory.CreateDirectory(DatabaseManager.GetAppDirectory());

        // Register link/ in PATH
        var added = ShortcutManager.EnsureLinkInPath();
        if (added)
        {
            ShortcutManager.BroadcastPathChange();
            Console.WriteLine($"[OK] 已添加到 PATH: {DatabaseManager.GetLinkDirectory()}");
            Console.WriteLine("     现在可以直接在 Win+R 中使用你的快捷方式。");
        }
        else
        {
            Console.WriteLine("[OK] PATH 已配置，无需修改。");
        }

        // Regenerate all .lnk files
        using var db = new DatabaseManager(DatabaseManager.GetDefaultDbPath());
        var apps = db.GetAllApps();
        foreach (var app in apps)
        {
            ShortcutManager.CreateShortcut(app);
        }

        // Regenerate all .url files for URL entries
        var urls = db.GetAllUrls();
        foreach (var url in urls)
        {
            ShortcutManager.CreateUrlShortcut(url);
        }

        Console.WriteLine($"[OK] 已重新生成 {apps.Count} 个程序快捷方式 + {urls.Count} 个网址快捷方式。");

        // Create sd.lnk self-shortcut so Win+R can run "sd <args>"
        var rootDir = DatabaseManager.GetRootDirectory();
        var sdExePath = Environment.ProcessPath ?? Path.Combine(rootDir, "sd.exe");
        if (File.Exists(sdExePath))
        {
            var sdLnkPath = Path.Combine(DatabaseManager.GetLinkDirectory(), "sd.lnk");
            ShortcutManager.CreateRawShortcut(sdExePath, sdLnkPath, "SuperDucker CLI");
            Console.WriteLine($"[OK] 已创建 sd.lnk -> Win+R 可直接使用 sd 命令。");
        }

        // Create superducker.lnk for the panel
        var panelExePath = Path.Combine(rootDir, "superducker.exe");
        if (File.Exists(panelExePath))
        {
            var panelLnkPath = Path.Combine(DatabaseManager.GetLinkDirectory(), "superducker.lnk");
            ShortcutManager.CreateRawShortcut(panelExePath, panelLnkPath, "SuperDucker Panel");
            Console.WriteLine($"[OK] 已创建 superducker.lnk -> Win+R 可打开面板。");
        }

        return 0;
    }

    // ═══════════════════════════════════════════
    //  sd repair (manual path repair)
    // ═══════════════════════════════════════════
    static int HandleRepair(DatabaseManager db)
    {
        var repaired = ShortcutManager.RepairAllShortcuts(db);
        Console.WriteLine($"[OK] 已修复 {repaired} 个快捷方式。");
        return 0;
    }

    // ═══════════════════════════════════════════
    //  sd url add|remove|list
    // ═══════════════════════════════════════════
    static int HandleUrl(DatabaseManager db, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("用法: sd url <add|remove|list> ...");
            return 1;
        }

        var sub = args[0].ToLowerInvariant();

        if (sub == "list")
        {
            var urls = db.GetAllUrls();
            if (urls.Count == 0)
            {
                Console.WriteLine("  (无已注册 URL)");
            }
            foreach (var url in urls)
            {
                var friendly = url.FriendlyName != null ? $" ({url.FriendlyName})" : "";
                var cat = url.Category != null ? $" [{url.Category}]" : "";
                Console.WriteLine($"  {url.Abbreviation,-12}{friendly}{cat}");
                Console.WriteLine($"    -> {url.Url}");
            }
            return 0;
        }

        if (sub == "add")
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("用法: sd url add <缩写> <网址> [--name <友好名称>] [--desc <描述>] [--tab <标签页>]");
                return 1;
            }
            var abbreviation = args[1].ToUpperInvariant();
            var url = args[2];

            string? friendlyName = null, description = null, tabName = null;
            for (int i = 3; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--name" when i + 1 < args.Length:
                        friendlyName = args[++i]; break;
                    case "--desc" when i + 1 < args.Length:
                        description = args[++i]; break;
                    case "--tab" when i + 1 < args.Length:
                        tabName = args[++i]; break;
                }
            }

            // Resolve tab
            int? tabId = null;
            if (tabName != null)
            {
                tabId = ResolveTabId(db, tabName);
                if (tabId == null)
                {
                    Console.Error.WriteLine($"错误: 找不到标签页 '{tabName}'。");
                    return 1;
                }
            }

            if (db.AbbreviationExists(abbreviation))
            {
                var conflict = db.FindAbbreviationConflict(abbreviation);
                Console.Error.WriteLine($"错误: 缩写 '{abbreviation}' 已被占用{(conflict != null ? $" ({conflict})" : "")}。");
                return 1;
            }

            var entry = new UrlEntry
            {
                Abbreviation = abbreviation,
                FriendlyName = friendlyName,
                Url = url,
                Description = description,
                TabId = tabId,
            };
            db.AddUrl(entry);
            ShortcutManager.CreateUrlShortcut(entry);
            Console.WriteLine($"[OK] 已添加 URL '{abbreviation}' -> {url}");
            if (tabName != null) Console.WriteLine($"     标签页: {tabName}");
            return 0;
        }

        if (sub == "remove" || sub == "rm")
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("用法: sd url remove <缩写>");
                return 1;
            }
            var abbreviation = args[1].ToUpperInvariant();
            var entry = db.GetUrlByAbbreviation(abbreviation);
            if (entry == null)
            {
                Console.Error.WriteLine($"错误: 找不到 URL '{abbreviation}'。");
                return 1;
            }
            db.DeleteUrl(entry.Id);
            ShortcutManager.DeleteUrlShortcut(abbreviation);
            Console.WriteLine($"[OK] 已删除 URL '{abbreviation}'");
            return 0;
        }

        Console.Error.WriteLine($"未知 url 子命令: {sub}");
        return 1;
    }

    // ═══════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════

    static void LaunchApp(AppEntry app, bool asAdmin)
    {
        var psi = new ProcessStartInfo
        {
            FileName = app.TargetPath,
            WorkingDirectory = app.WorkingDirectory ?? Path.GetDirectoryName(app.TargetPath) ?? "",
            UseShellExecute = true
        };

        if (asAdmin)
        {
            psi.Verb = "runas";
        }

        try
        {
            Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User cancelled UAC prompt
            Console.Error.WriteLine("UAC 提示已被取消。");
        }
    }

    static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    /// <summary>
    /// Resolves a tab name to its ID. Returns null if not found.
    /// </summary>
    static int? ResolveTabId(DatabaseManager db, string tabName)
    {
        var tabs = db.GetAllTabs();
        var match = tabs.FirstOrDefault(t =>
            t.Name.Equals(tabName, StringComparison.OrdinalIgnoreCase));
        return match?.Id;
    }

    /// <summary>
    /// Shows a Windows balloon notification via PowerShell (avoids WinForms dependency in CLI).
    /// </summary>
    static void ShowBalloonTip(string title, string message, int durationMs)
    {
        var escapedTitle = title.Replace("'", "''");
        var escapedMsg = message.Replace("'", "''");

        var psScript = $@"
Add-Type -AssemblyName System.Windows.Forms
$notify = New-Object System.Windows.Forms.NotifyIcon
$notify.Icon = [System.Drawing.SystemIcons]::Information
$notify.Visible = $true
$notify.BalloonTipTitle = '{escapedTitle}'
$notify.BalloonTipText = '{escapedMsg}'
$notify.ShowBalloonTip({durationMs})
Start-Sleep -Milliseconds {durationMs + 500}
$notify.Dispose()
";
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{psScript.Replace("\"", "\\\"")}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    static int ShowHelp()
    {
        Console.WriteLine(@"
SuperDucker CLI (sd) — 绿色软件快速启动器
══════════════════════════════════════════

启动:
  sd <缩写>                    启动程序或打开网址
  sd s <缩写>                  以管理员权限运行
  sd d <缩写>                  打开程序所在目录
  sd e <缩写>                  显示程序描述 (气泡通知)

管理:
  sd add <缩写> <路径>         注册新程序
      [--name <友好名称>]        (中文显示名)
      [--desc <描述>]            (描述信息)
      [--cat <分类>]             (面板分类)
      [--tab <标签页>]           (归属标签页)

  sd edit <缩写>               修改已注册条目
      [--name <名称>] [--desc <描述>] [--cat <分类>]
      [--tab <标签页>]           (传 none 清除归属)
      [--path <新路径>]          (修改程序路径, 仅程序)
      [--url <新网址>]           (修改网址, 仅 URL)
      [--abbr <新缩写>]          (修改缩写)

  sd remove <缩写>             删除程序或 URL
  sd list [--cat <分类>]       列出所有已注册条目
       [--tab <标签页>]          (按标签页筛选)

图标:
  sd icon <缩写> <图标路径>    设置自定义图标
  sd icon <缩写> --fetch       抓取网站图标 (仅 URL)

网址:
  sd url add <缩写> <网址>     注册网址书签
      [--name <友好名称>] [--desc <描述>] [--tab <标签页>]
  sd url remove <缩写>         删除网址
  sd url list                  列出所有网址

打包:
  sd pack <源目录> <包ID>      将程序打包为 .sdzip 绿软包
      [--name <名称>] [--abbr <缩写>] [--version <版本>]
      [--main <主程序.exe>] [--icon <图标路径>]
      [--author <作者>] [--desc <描述>] [--cat <分类>]
      [--from <缩写>]           从已注册条目拉取元数据 + 图标
      [-o <输出路径>] [--import]  (导入到面板)
  sd pack-gui                  启动图形化打包工具
  sd import <包文件.sdzip>     从 .sdzip 包导入程序到面板

维护:
  sd setup                     注册 link/ 到 PATH (Win+R 可用)
  sd repair                    修复快捷方式路径
  sd rescan                    扫描 app/ 目录，恢复丢失的注册
  sd help / -h / --help        显示此帮助
");
        return 0;
    }
}
