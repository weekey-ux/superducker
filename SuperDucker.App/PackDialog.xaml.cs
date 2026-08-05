using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SuperDucker.Shared;
using SuperDucker.Shared.Data;
using SuperDucker.Shared.Models;
using SuperDucker.Shared.Native;

namespace SuperDucker.App;

public partial class PackDialog : Window
{
    private readonly DatabaseManager? _db;
    private string? _iconPath;
    private bool _isPacking;
    private List<AppEntry> _allApps = new();
    private IQueryable<AppEntry>? _filteredApps;

    public PackDialog(DatabaseManager? db)
    {
        InitializeComponent();
        _db = db;
        LoadExistingEntries();
    }

    private void LoadExistingEntries()
    {
        if (_db == null)
        {
            // No database available (e.g. DB failed to open in --pack mode) — nothing to load.
            _allApps = new List<AppEntry>();
            _filteredApps = _allApps.AsQueryable();
            UpdateComboBoxSource();
            return;
        }

        try
        {
            _allApps = _db.GetAllApps().OrderBy(e => e.FriendlyName).ToList();
            
            // Start with first 50 items (lazy loading)
            _filteredApps = _allApps.AsQueryable();
            UpdateComboBoxSource();
            ExistingEntryCombo.SelectedIndex = -1;
            
            // Load categories and tags for dropdowns
            LoadCategoriesAndTags();
        }
        catch { }
    }

    /// <summary>
    /// 加载分类下拉列表（从已有软件条目中聚合去重），并初始化标签框。
    /// 标签框为可编辑 ComboBox，支持用户手动输入逗号分隔的标签；
    /// 打包导入时这些标签会用于自动创建标签页（见 DoImport）。
    /// </summary>
    private void LoadCategoriesAndTags()
    {
        if (_db == null) return;

        try
        {
            // 从所有软件条目中聚合去重的分类名称，按字母序排序
            var categories = _db.GetAllApps()
                .Select(e => e.Category)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            // 从 localshop/ 中所有 .sdzip 的 manifest 聚合去重的标签
            var tags = ShopManager.GetAllTagsFromShop();

            // 填充分类与标签下拉框（均保持可编辑，允许用户自由输入）
            CatBox.ItemsSource = categories;
            TagsBox.ItemsSource = tags;
        }
        catch { /* 忽略加载失败，不影响主流程 */ }
    }

    private void UpdateComboBoxSource()
    {
        if (_filteredApps == null) return;
        
        var visibleItems = _filteredApps.Take(50).ToList();
        ExistingEntryCombo.ItemsSource = visibleItems;
        
        // Show count
        if (_allApps.Count > 50)
        {
            EntryCountText.Text = $"共 {_allApps.Count} 个软件 · 显示前 50 个";
        }
        else
        {
            EntryCountText.Text = $"共 {_allApps.Count} 个软件";
        }
    }

    private IEnumerable<AppEntry> FilterApps(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return _allApps.Take(50);
        
        var term = searchTerm.ToLowerInvariant();
        return _allApps
            .Where(app => app != null && (app.FriendlyName?.Contains(term) == true ||
                       app.Abbreviation.Contains(term) ||
                       (app.Category != null && app.Category.Contains(term))))
            .Take(50);
    }

    // ═══════════════════════════════════════════
    //  Event Handlers
    // ═══════════════════════════════════════════

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void SearchEntry_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = SearchEntryBox.Text.Trim();
        
        if (string.IsNullOrWhiteSpace(searchText))
        {
            // Reset to show first 50 items
            _filteredApps = _allApps.AsQueryable();
            UpdateComboBoxSource();
        }
        else
        {
            var term = searchText.ToLowerInvariant();
            _filteredApps = _allApps
                .Where(app => app != null && (app.FriendlyName?.Contains(term) == true ||
                           app.Abbreviation.Contains(term) ||
                           (app.Category != null && app.Category.Contains(term))))
                .AsQueryable();
            UpdateComboBoxSource();
        }
    }
    
    private void SearchEntry_Clear(object sender, RoutedEventArgs e)
    {
        SearchEntryBox.Text = "";
        SearchEntryBox.Focus();
        _filteredApps = _allApps.AsQueryable();
        UpdateComboBoxSource();
    }
    private void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "选择软件源目录"
        };

        if (dlg.ShowDialog() == true)
        {
            SourceDirBox.Text = dlg.FolderName;
            AutoFillFromDirectory(dlg.FolderName);
        }
    }

    private void SourceDir_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        // 源目录文本变化时触发：清空旧信息，从新目录尽可能多地自动探测填充。
        // 无论来自手动输入或浏览按钮，行为一致。
        var text = SourceDirBox.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            ClearAllFields();
            return;
        }

        try
        {
            if (!Directory.Exists(text))
                return;

            AutoFillFromDirectory(text);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SourceDir_Changed] Error: {ex.Message}");
            StatusText.Text = $"检测失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 根据指定源目录自动探测并填充面板控件。
    /// 始终先清空旧数据（避免残留上一次选择的信息），
    /// 再从目录中的 exe 文件提取名称、版本等元数据。
    /// </summary>
    private void AutoFillFromDirectory(string dirPath)
    {
        // ── 1. 先清空所有字段（让用户知道哪些是自动填充的、哪些需手动填写）──
        ClearAllFields();

        // ── 2. 扫描目录中的 exe 文件 ──
        var exeFiles = Directory.GetFiles(dirPath, "*.exe", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                        !f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!exeFiles.Any())
        {
            StatusText.Text = "未找到可执行文件，请手动填写信息";
            UpdateExtractButton();
            return;
        }

        // 取第一个 exe 作为候选主程序（通常绿色软件只有一个主 exe）
        var mainExe = Path.GetFileName(exeFiles.First());
        var mainExeFullPath = exeFiles.First();
        MainExeBox.Text = mainExe;

        // ── 3. 尝试从 exe 的 VersionInfo 提取元数据 ──
        string? detectedName = null;
        string? detectedVersion = null;
        string? detectedDesc = null;

        try
        {
            var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(mainExeFullPath);
            if (!string.IsNullOrWhiteSpace(fvi.ProductName))
                detectedName = fvi.ProductName.Trim();
            if (!string.IsNullOrWhiteSpace(fvi.FileVersion) || !string.IsNullOrWhiteSpace(fvi.ProductVersion))
                detectedVersion = (!string.IsNullOrWhiteSpace(fvi.ProductVersion)) ? fvi.ProductVersion.Trim() : fvi.FileVersion?.Trim();
            if (!string.IsNullOrWhiteSpace(fvi.FileDescription) && !string.Equals(fvi.FileDescription, fvi.ProductName, StringComparison.Ordinal))
                detectedDesc = fvi.FileDescription.Trim();
            else if (!string.IsNullOrWhiteSpace(fvi.Comments))
                detectedDesc = fvi.Comments.Trim();
        }
        catch { /* 某些 exe 可能无法读取版本信息，静默忽略 */ }

        // ── 4. 填充包 ID 与缩写（基于 exe 文件名）──
        var baseName = Path.GetFileNameWithoutExtension(mainExe).ToLowerInvariant();
        PackageIdBox.Text = baseName;
        AbbrBox.Text = baseName.ToUpperInvariant();

        // ── 5. 填充显示名称与版本（优先用 VersionInfo，回退到文件夹名）──
        NameBox.Text = detectedName ?? Path.GetFileName(dirPath);
        VersionBox.Text = detectedVersion ?? "1.0.0";
        DescBox.Text = detectedDesc ?? "";

        // ── 6. 尝试查找图标文件 ──
        foreach (var ext in new[] { ".ico", ".png", ".jpg" })
        {
            var iconCandidate = Path.Combine(dirPath, $"{baseName}{ext}");
            if (File.Exists(iconCandidate)) { SetIcon(iconCandidate); break; }
        }
        // 也尝试以大写缩写命名的图标
        if (_iconPath == null)
        {
            var upperIcon = Path.Combine(dirPath, $"{baseName.ToUpperInvariant()}.ico");
            if (File.Exists(upperIcon)) SetIcon(upperIcon);
        }

        StatusText.Text = $"已检测目录，找到 {exeFiles.Count()} 个可执行文件";
        UpdateExtractButton();
    }

    /// <summary>
    /// 清空所有面板字段（除源目录本身外），确保切换软件时不残留旧数据。
    /// </summary>
    private void ClearAllFields()
    {
        MainExeBox.Text = "";
        PackageIdBox.Text = "";
        AbbrBox.Text = "";
        NameBox.Text = "";
        VersionBox.Text = "1.0.0";
        DescBox.Text = "";
        CatBox.Text = "";
        TagsBox.Text = "";
        AuthorBox.Text = "";
        HomepageBox.Text = "";
        OutputPathBox.Text = "";
        PreserveBox.Text = "";

        // 清除图标预览
        _iconPath = null;
        IconPreview.Source = null;
        IconPathText.Text = "(未选择图标)";
    }

    private void Category_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // If user selects a category from dropdown, copy it to text box
        if (CatBox.SelectedItem is string selectedCategory)
        {
            CatBox.Text = selectedCategory;
        }
    }

    private void TagItem_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 当用户从下拉列表中选择一个标签时，将其回填到文本框中
        if (TagsBox.SelectedItem is string selectedTag)
        {
            TagsBox.Text = selectedTag;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void ExistingEntry_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ExistingEntryCombo.SelectedItem is AppEntry app)
        {
            // Always update all fields when selecting from existing entry
            SourceDirBox.Text = Path.GetDirectoryName(app.TargetPath) ?? "";
            NameBox.Text = app.FriendlyName ?? "";
            AbbrBox.Text = app.Abbreviation;
            MainExeBox.Text = Path.GetFileName(app.TargetPath);
            DescBox.Text = app.Description ?? "";
            CatBox.Text = app.Category ?? "";

            // Auto-generate package ID from abbreviation
            PackageIdBox.Text = app.Abbreviation.ToLowerInvariant();

            // Use existing icon if available
            if (!string.IsNullOrEmpty(app.IconPath) && File.Exists(app.IconPath))
                SetIcon(app.IconPath);

            // Auto-fill abbreviation
            if (string.IsNullOrWhiteSpace(AbbrBox.Text))
                AbbrBox.Text = app.Abbreviation;
        }
    }

    private void PackageId_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        // Auto-fill abbreviation from package ID
        if (!string.IsNullOrWhiteSpace(PackageIdBox.Text) && string.IsNullOrWhiteSpace(AbbrBox.Text))
            AbbrBox.Text = PackageIdBox.Text.ToUpperInvariant();

        // Update default output path to localshop/ subfolder
        var pkgId = PackageIdBox.Text.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(pkgId))
        {
            var localShopDir = Path.Combine(AppContext.BaseDirectory, "localshop");
            Directory.CreateDirectory(localShopDir);
            var currentOutput = OutputPathBox.Text;
            if (string.IsNullOrEmpty(currentOutput) || currentOutput.EndsWith(".sdzip"))
                OutputPathBox.Text = Path.Combine(localShopDir, $"{pkgId}.sdzip");
        }
    }

    private void MainExe_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateExtractButton();
    }

    private void UpdateExtractButton()
    {
        var sourceDir = SourceDirBox.Text;
        var mainExe = MainExeBox.Text;
        ExtractIconBtn.IsEnabled = !string.IsNullOrWhiteSpace(sourceDir)
                                   && !string.IsNullOrWhiteSpace(mainExe)
                                   && Directory.Exists(sourceDir);
    }

    private void BrowseMainExe_Click(object sender, RoutedEventArgs e)
    {
        var dir = SourceDirBox.Text;
        if (!Directory.Exists(dir))
        {
            MessageBox.Show("请先选择源目录", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = "选择主程序",
            Filter = "可执行文件 (*.exe)|*.exe",
            InitialDirectory = dir,
            Multiselect = false
        };
        if (dlg.ShowDialog(this) == true)
        {
            MainExeBox.Text = Path.GetRelativePath(dir, dlg.FileName);
        }
    }

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择图标文件",
            Filter = "图标文件 (*.ico;*.png;*.jpg;*.bmp)|*.ico;*.png;*.jpg;*.jpeg;*.bmp",
            Multiselect = false
        };
        if (dlg.ShowDialog(this) == true)
        {
            SetIcon(dlg.FileName);
        }
    }

    private void ExtractIcon_Click(object sender, RoutedEventArgs e)
    {
        var sourceDir = SourceDirBox.Text;
        var mainExe = MainExeBox.Text;
        if (string.IsNullOrWhiteSpace(sourceDir) || string.IsNullOrWhiteSpace(mainExe)) return;

        var exePath = Path.Combine(sourceDir, mainExe);
        if (!File.Exists(exePath))
        {
            MessageBox.Show($"找不到主程序: {exePath}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"sd_pack_preview_{Guid.NewGuid():N}.ico");
        var result = IconHelper.ExtractAndSaveIcon(exePath, tempPath);
        if (result != null)
        {
            SetIcon(result);
            StatusText.Text = "已从 exe 提取图标";
        }
        else
        {
            MessageBox.Show("无法从该 exe 提取图标", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SetIcon(string path)
    {
        _iconPath = path;
        IconPathText.Text = path;

        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            bi.StreamSource = fs;
            bi.EndInit();
            bi.Freeze();
            IconPreview.Source = bi;
        }
        catch { }
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "保存打包文件",
            Filter = "SuperDucker 包 (*.sdzip)|*.sdzip",
            FileName = OutputPathBox.Text
        };
        if (dlg.ShowDialog(this) == true)
        {
            OutputPathBox.Text = dlg.FileName;
        }
    }

    // ═══════════════════════════════════════════
    //  Pack Logic
    // ═══════════════════════════════════════════

    private async void Pack_Click(object sender, RoutedEventArgs e)
    {
        if (_isPacking) return;

        // Validate
        var sourceDir = SourceDirBox.Text.Trim();
        var packageId = PackageIdBox.Text.Trim().ToLowerInvariant();
        var mainExe = MainExeBox.Text.Trim();
        var outputPath = OutputPathBox.Text.Trim();

        if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
        {
            MessageBox.Show("请选择有效的源目录", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (string.IsNullOrEmpty(packageId))
        {
            MessageBox.Show("请填写包 ID", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (string.IsNullOrEmpty(mainExe))
        {
            MessageBox.Show("请指定主程序", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (!File.Exists(Path.Combine(sourceDir, mainExe)))
        {
            MessageBox.Show($"主程序不存在: {mainExe}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (string.IsNullOrEmpty(outputPath))
        {
            MessageBox.Show("请指定输出路径", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Auto-extract icon from exe if none specified
        if (_iconPath == null)
        {
            var exeFullPath = Path.Combine(sourceDir, mainExe);
            var tempIconPath = Path.Combine(Path.GetTempPath(), $"sd_pack_{packageId}.ico");
            var extracted = IconHelper.ExtractAndSaveIcon(exeFullPath, tempIconPath);
            if (extracted != null)
            {
                _iconPath = extracted;
                SetIcon(extracted);
            }
        }

        _isPacking = true;
        PackButton.IsEnabled = false;
        PackButton.Content = "打包中...";
        StatusText.Text = "正在打包...";

        // Capture all UI values on the UI thread before dispatching to background
        var packParams = new PackParams
        {
            SourceDir = sourceDir,
            PackageId = packageId,
            MainExe = mainExe,
            OutputPath = outputPath,
            Abbreviation = string.IsNullOrWhiteSpace(AbbrBox.Text) ? packageId.ToUpperInvariant() : AbbrBox.Text.Trim().ToUpperInvariant(),
            Name = string.IsNullOrWhiteSpace(NameBox.Text) ? null : NameBox.Text.Trim(),
            Version = string.IsNullOrWhiteSpace(VersionBox.Text) ? "1.0.0" : VersionBox.Text.Trim(),
            Author = string.IsNullOrWhiteSpace(AuthorBox.Text) ? null : AuthorBox.Text.Trim(),
            Homepage = string.IsNullOrWhiteSpace(HomepageBox.Text) ? null : HomepageBox.Text.Trim(),
            Description = string.IsNullOrWhiteSpace(DescBox.Text) ? null : DescBox.Text.Trim(),
            Categories = ParseList(CatBox.Text),
            Tags = ParseList(TagsBox.Text),
            IconPath = _iconPath,
            Import = ImportCheckBox.IsChecked == true,
            PreserveUserData = ParsePreserveList(PreserveBox.Text)
        };

        try
        {
            var result = await Task.Run(() => DoPack(packParams));

            if (result.Success)
            {
                StatusText.Text = $"打包完成! {result.FileSize}  SHA-256: {result.Sha256[..16]}...";

                if (packParams.Import)
                {
                    StatusText.Text = "正在导入...";
                    try
                    {
                        await Task.Run(() => DoImport(outputPath, packageId));
                        StatusText.Text = "打包并导入成功！";
                        
                        // Notify parent window to refresh shop UI
                        await Dispatcher.InvokeAsync(async () =>
                        {
                            if (Owner is MainWindow mainWin)
                            {
                                await mainWin.RefreshShopUIAsync();
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        // Import failed but pack succeeded - inform user
                        MessageBox.Show($"打包成功！\n\n文件：{outputPath}\n大小：{result.FileSize}\n\n但导入时出错：\n{ex.Message}",
                            "部分成功", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                MessageBox.Show($"打包成功!\n\n文件: {outputPath}\n大小: {result.FileSize}\nSHA-256: {result.Sha256}",
                    "打包完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusText.Text = $"打包失败: {result.Error}";
                MessageBox.Show($"打包失败:\n{result.Error}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"打包失败: {ex.Message}";
            MessageBox.Show($"打包异常:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isPacking = false;
            PackButton.IsEnabled = true;
            PackButton.Content = "打包";
        }
    }

    private PackResult DoPack(PackParams p)
    {
        try
        {
            var packageId = p.PackageId.ToLowerInvariant();
            var abbreviation = p.Abbreviation;
            
            // Ensure abbreviation is not empty, fall back to packageId
            if (string.IsNullOrEmpty(abbreviation))
                abbreviation = packageId.ToUpperInvariant();

            var manifest = new PackageManifest
            {
                Id = p.PackageId,
                Abbreviation = abbreviation,
                Name = p.Name ?? Path.GetFileName(p.SourceDir),
                Version = p.Version,
                Author = p.Author,
                Homepage = p.Homepage,
                Description = p.Description,
                MainExe = p.MainExe,
                ExtractSubDir = "app",
                Categories = p.Categories,
                Tags = p.Tags,
                InstallActions = new InstallActions(),
                UninstallActions = new UninstallActions
                {
                    RemoveDir = true,
                    PreserveUserData = p.PreserveUserData
                },
                Requirements = new PackageRequirements
                {
                    MinWindows = "10",
                    Architecture = new List<string> { "x64" }
                }
            };

            var outputPath = Path.GetFullPath(p.OutputPath);
            
            // Ask for confirmation before deleting existing output file
            if (File.Exists(outputPath))
            {
                // Move to recycle bin instead of permanent delete
                try
                {
                    var tempPath = Path.Combine(Path.GetTempPath(), "SD_" + Path.GetFileName(outputPath) + "_" + DateTime.Now.Ticks);
                    File.Move(outputPath, tempPath);
                    RecycleBinHelper.MoveToRecycleBin(tempPath);
                }
                catch
                {
                    // If recycle bin fails, just overwrite the file
                }
            }

            var files = Directory.GetFiles(p.SourceDir, "*", SearchOption.AllDirectories);

            using (var zipStream = new FileStream(outputPath, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                // Write manifest
                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                using (var writer = new StreamWriter(manifestEntry.Open()))
                    writer.Write(manifest.ToJson());

                // Write icon with proper naming: {abbreviation}{ext}
                if (p.IconPath != null && File.Exists(p.IconPath))
                {
                    var iconExt = Path.GetExtension(p.IconPath).ToLowerInvariant();
                    var iconAbbr = abbreviation.ToUpperInvariant();
                    archive.CreateEntryFromFile(p.IconPath, $"{iconAbbr}{iconExt}", CompressionLevel.Optimal);
                }

                // Write app files
                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(p.SourceDir, file).Replace('\\', '/');

                    // Skip .pdb files (except python-related)
                    if (relativePath.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
                        && !relativePath.Contains("python", StringComparison.OrdinalIgnoreCase))
                        continue;

                    archive.CreateEntryFromFile(file, $"app/{relativePath}", CompressionLevel.Optimal);
                }
            }

            // Compute SHA-256
            string sha256;
            using (var stream = File.OpenRead(outputPath))
            {
                var hash = SHA256.HashData(stream);
                sha256 = Convert.ToHexString(hash).ToLowerInvariant();
            }

            var size = new FileInfo(outputPath).Length;
            return new PackResult
            {
                Success = true,
                Sha256 = sha256,
                FileSize = FileHelper.FormatSize(size)
            };
        }
        catch (Exception ex)
        {
            return new PackResult { Success = false, Error = ex.Message };
        }
    }

    private void DoImport(string outputPath, string packageId)
    {
        if (_db == null)
            throw new InvalidOperationException("数据库不可用，无法导入到本地库。");

        var errors = new List<string>();
        try
        {
            var appDir = DatabaseManager.GetAppDirectory();
            var targetDir = Path.Combine(appDir, packageId);

            // Check if directory already exists - this is a hard failure
            if (Directory.Exists(targetDir))
            {
                throw new Exception($"目录已存在：{targetDir}");
            }

            Directory.CreateDirectory(targetDir);
            ZipArchiveEntry? iconEntry = null;
            string? extractedIconPath = null;
            PackageManifest? manifest = null;

            using (var zipStream = File.OpenRead(outputPath))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                // First pass: read manifest and find icon entry
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName == "manifest.json")
                    {
                        using var reader = new StreamReader(entry.Open());
                        var json = reader.ReadToEnd();
                        manifest = PackageManifest.FromJson(json);
                        if (manifest == null)
                        {
                            throw new Exception("manifest.json 解析失败");
                        }
                        // Save copy to target dir for recovery
                        var manifestCopyPath = Path.Combine(targetDir, "manifest.json");
                        File.WriteAllText(manifestCopyPath, json);
                        continue;
                    }
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    // Detect icon files at zip root (icon.png, icon.ico, etc.)
                    if (!entry.FullName.Contains('/') && entry.Name.StartsWith("icon.", StringComparison.OrdinalIgnoreCase))
                    {
                        iconEntry = entry;
                        continue;
                    }

                    var entryPath = entry.FullName;
                    if (entryPath.StartsWith("app/"))
                        entryPath = entryPath[4..];

                    var targetPath = Path.Combine(targetDir, entryPath);
                    var targetFileDir = Path.GetDirectoryName(targetPath);
                    if (targetFileDir != null) Directory.CreateDirectory(targetFileDir);

                    // Zip slip protection
                    if (!Path.GetFullPath(targetPath).StartsWith(Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
                        continue;

                    entry.ExtractToFile(targetPath, true);
                }

                // Extract icon to icons/ directory (must be inside using block while archive is open)
                if (iconEntry != null)
                {
                    var iconsDir = WebHelper.GetIconsDirectory();
                    Directory.CreateDirectory(iconsDir);
                    var iconExt = Path.GetExtension(iconEntry.Name).ToLowerInvariant();
                    
                    // Try to get abbreviation from manifest first, fall back to packageId
                    var iconAbbr = manifest?.Abbreviation?.ToUpperInvariant() ?? packageId.ToUpperInvariant();
                    
                    // Ensure abbreviation is not empty
                    if (string.IsNullOrEmpty(iconAbbr))
                        iconAbbr = packageId.ToUpperInvariant();
                    
                    // Save with proper naming: {abbreviation}{ext}
                    extractedIconPath = Path.Combine(iconsDir, $"{iconAbbr}{iconExt}");
                    
                    // Verify we can extract the icon
                    try
                    {
                        iconEntry.ExtractToFile(extractedIconPath, true);
                        Console.WriteLine($"[图标] 已提取到：{extractedIconPath}");
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"无法提取图标文件：{ex.Message}");
                        extractedIconPath = null;
                    }
                }
            }

            if (manifest != null)
            {
                var mainExePath = Path.Combine(targetDir, manifest.MainExe);
                var abbreviation = manifest.Abbreviation?.ToUpperInvariant() ?? packageId.ToUpperInvariant();

                // Validate main exe was extracted correctly
                if (!File.Exists(mainExePath))
                {
                    throw new Exception($"主程序不存在：{mainExePath}");
                }

                // Ensure unique abbreviation
                if (_db.AbbreviationExists(abbreviation))
                {
                    var shortAbbr = new string(manifest.Name.Where(char.IsAsciiLetterOrDigit).Take(4).ToArray()).ToUpperInvariant();
                    if (!string.IsNullOrEmpty(shortAbbr) && !_db.AbbreviationExists(shortAbbr))
                    {
                        abbreviation = shortAbbr;
                        Console.WriteLine($"  缩写 '{abbreviation}' 已被占用，改用 '{shortAbbr}'");
                    }
                    else
                    {
                        throw new Exception($"缩写 '{abbreviation}' 已被占用，请手动使用 sd add 注册。");
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

                _db.AddApp(appEntry);
                
                // Create shortcut - validate before creating
                try
                {
                    ShortcutManager.CreateShortcut(appEntry);
                    Console.WriteLine($"[快捷方式] 已创建：link\\{abbreviation}.lnk");
                }
                catch (Exception ex)
                {
                    errors.Add($"创建快捷方式失败：{ex.Message}");
                }

                // Auto-create tab from first tag and assign app to it
                try
                {
                    if (manifest.Tags.Count > 0)
                    {
                        var tagName = manifest.Tags[0];
                        var tabs = _db.GetAllTabs();
                        var tab = tabs.FirstOrDefault(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
                        if (tab == null)
                        {
                            tab = _db.AddTab(new TabEntry { Name = tagName, SortOrder = tabs.Count });
                            Console.WriteLine($"[标签] 已创建标签页 '{tagName}'");
                        }
                        _db.SetEntryTab("app_entries", appEntry.Id, tab.Id);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"创建标签页失败：{ex.Message}");
                }
            }

            if (errors.Count > 0)
            {
                foreach (var err in errors)
                {
                    Console.Error.WriteLine($"警告：{err}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"导入失败：{ex.Message}");
            throw; // Re-throw so caller knows it failed
        }
    }

    // ═══════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════

    private static List<string> ParseList(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        return text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    /// <summary>
    /// 解析"保留文件"输入：逗号或换行分隔，去重后返回。
    /// </summary>
    private static List<string> ParsePreserveList(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        return text
            .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private class PackParams
    {
        public string SourceDir { get; set; } = "";
        public string PackageId { get; set; } = "";
        public string MainExe { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public string Abbreviation { get; set; } = "";
        public string? Name { get; set; }
        public string Version { get; set; } = "1.0.0";
        public string? Author { get; set; }
        public string? Homepage { get; set; }
        public string? Description { get; set; }
        public List<string> Categories { get; set; } = new();
        public List<string> Tags { get; set; } = new();
        public string? IconPath { get; set; }
        public bool Import { get; set; }
        public List<string>? PreserveUserData { get; set; }
    }

    private class PackResult
    {
        public bool Success { get; set; }
        public string Sha256 { get; set; } = "";
        public string FileSize { get; set; } = "";
        public string Error { get; set; } = "";
    }
}
