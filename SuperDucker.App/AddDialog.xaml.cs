using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SuperDucker.Shared.Data;
using SuperDucker.Shared.Helpers;

namespace SuperDucker.App;

/// <summary>
/// 添加 / 编辑 程序或网址的对话框。
/// 通过 <see cref="DialogMode"/> 区分两种模式：
///   - App：配置本地可执行程序（路径、缩写、分类、描述等）；
///   - Url：配置网址（含连通性测试、Favicon 图标获取/自定义图标）。
/// 编辑模式下缩写只读，其余字段预填并以「确定修改」形式保存。
/// </summary>
public partial class AddDialog : Window
{
    public enum DialogMode { App, Url }

    private readonly DialogMode _mode;
    private string? _customIconPath;
    private readonly bool _isEditMode;

    /// <summary>Original abbreviation before editing (for identifying which entry to update).</summary>
    public string? OriginalAbbreviation { get; }

    // Results for the caller to read
    public string Abbreviation => TxtAbbreviation.Text.Trim().ToUpperInvariant();
    public string TargetPath => TxtTargetPath.Text.Trim();
    public string? FriendlyName => string.IsNullOrWhiteSpace(TxtFriendlyName.Text) ? null : TxtFriendlyName.Text.Trim();
    public string? Description => string.IsNullOrWhiteSpace(TxtDescription.Text) ? null : TxtDescription.Text.Trim();
    public string? Category => string.IsNullOrWhiteSpace(TxtCategory.Text) ? null : TxtCategory.Text.Trim();

    /// <summary>
    /// Icon path set by the user (custom pick) or fetched from favicon.
    /// Null if no icon was set.
    /// </summary>
    public string? IconPath => _customIconPath;

    /// <summary>True if the user is editing an existing entry.</summary>
    public bool IsEditMode => _isEditMode;

    // ═══ Add mode constructor ═══
    public AddDialog(DialogMode mode)
    {
        _mode = mode;
        _isEditMode = false;
        InitializeComponent();
        ApplyMode();

    }

    // ═══ Edit mode constructor ═══
    public AddDialog(DialogMode mode, PanelItemViewModel existing)
    {
        _mode = mode;
        _isEditMode = true;
        OriginalAbbreviation = existing.Abbreviation;
        InitializeComponent();
        ApplyMode();

        // Pre-fill fields
        TxtAbbreviation.Text = existing.Abbreviation;
        TxtTargetPath.Text = mode == DialogMode.App ? (existing.TargetPath ?? "") : (existing.Url ?? "https://");
        TxtFriendlyName.Text = existing.FriendlyName ?? "";
        TxtDescription.Text = existing.Description ?? "";
        TxtCategory.Text = existing.Category ?? "";

        // In edit mode, abbreviation is read-only (changing it is complex with .lnk files)
        TxtAbbreviation.IsReadOnly = true;
        TxtAbbreviation.Opacity = 0.6;

        // Load existing icon preview
        if (_mode == DialogMode.App)
        {
            // 程序模式：优先用用户自定义图标（icons/{ABBR}_custom.*），
            // 没有再从目标 exe 提取内嵌图标；都没有则保持空。
            var customIcon = TryFindAppCustomIcon(existing.Abbreviation);
            if (customIcon != null)
            {
                _customIconPath = customIcon;
                ShowIconPreview(customIcon);
                TxtIconInfo.Text = Path.GetFileName(customIcon);
            }
            else if (existing.TargetPath != null && File.Exists(existing.TargetPath))
            {
                var bmp = IconHelper.ExtractToBitmapSource(existing.TargetPath);
                if (bmp != null)
                {
                    IconPreview.Source = bmp;
                    TxtIconInfo.Text = "已从程序提取";
                }
            }
        }
        else
        {
            // URL mode: try to load saved favicon first
            _customIconPath = GetExistingUrlIcon(existing.Abbreviation);
            if (_customIconPath != null)
                ShowIconPreview(_customIconPath);
        }

        // Change title and button
        DialogTitle.Text = mode == DialogMode.App ? "编辑程序" : "编辑网址";
        BtnConfirm.Content = "保存修改";
    }

    private string? GetExistingUrlIcon(string abbreviation)
    {
        var iconPath = WebHelper.GetIconPath(abbreviation);
        if (File.Exists(iconPath)) return iconPath;

        // Use exact filename matching (case-insensitive on Windows) instead of wildcards
        // to avoid matching a different abbreviation with the same prefix.
        var iconsDir = WebHelper.GetIconsDirectory();
        if (!Directory.Exists(iconsDir)) return null;

        var exactName = $"{abbreviation.ToUpperInvariant()}_custom";
        return Directory.EnumerateFiles(iconsDir)
            .FirstOrDefault(f =>
            {
                var fileName = Path.GetFileNameWithoutExtension(f);
                return fileName.Equals(exactName, StringComparison.OrdinalIgnoreCase);
            });
    }

    /// <summary>
    /// 在 icons/ 目录查找 ABBR_custom.* 自定义图标文件。
    /// APP 模式编辑时复用 URL 模式的自定义图标存储路径。
    /// </summary>
    private string? TryFindAppCustomIcon(string abbreviation)
    {
        var iconsDir = WebHelper.GetIconsDirectory();
        if (!Directory.Exists(iconsDir)) return null;

        var exactName = $"{abbreviation.ToUpperInvariant()}_custom";
        return Directory.EnumerateFiles(iconsDir)
            .FirstOrDefault(f =>
            {
                var fileName = Path.GetFileNameWithoutExtension(f);
                return fileName.Equals(exactName, StringComparison.OrdinalIgnoreCase);
            });
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            e.Handled = true;
            DragMove();
        }
    }

    private void ApplyMode()
    {
        if (_mode == DialogMode.App)
        {
            DialogTitle.Text = _isEditMode ? "编辑程序" : "添加程序";
            LblTarget.Text = "程序路径 *";
            BtnBrowse.Visibility = Visibility.Visible;
            UrlActions.Visibility = Visibility.Collapsed;
            
            // Show icon section only in edit mode for App
            IconSection.Visibility = _isEditMode ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            DialogTitle.Text = "添加网址";
            LblTarget.Text = "网址 *";
            BtnBrowse.Visibility = Visibility.Collapsed;
            if (!_isEditMode) TxtTargetPath.Text = "https://";
            UrlActions.Visibility = Visibility.Visible;
            IconSection.Visibility = Visibility.Visible;
        }
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择可执行文件",
            Filter = "可执行文件 (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            TxtTargetPath.Text = dialog.FileName;

            if (!_isEditMode)
            {
                // Auto-fill abbreviation from filename if empty
                if (string.IsNullOrWhiteSpace(TxtAbbreviation.Text))
                {
                    var name = Path.GetFileNameWithoutExtension(dialog.FileName);
                    TxtAbbreviation.Text = GenerateAbbreviation(name);
                }

                // Auto-fill friendly name from filename if empty
                if (string.IsNullOrWhiteSpace(TxtFriendlyName.Text))
                {
                    TxtFriendlyName.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
                }
            }
        }
    }

    // ═══ URL Actions ═══

    private async void BtnTestUrl_Click(object sender, RoutedEventArgs e)
    {
        var url = TxtTargetPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(url) || url == "https://")
        {
            TxtUrlStatus.Text = "请先输入网址";
            TxtUrlStatus.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("AccentOrangeBrush");
            return;
        }

        BtnTestUrl.IsEnabled = false;
        TxtUrlStatus.Text = "正在测试...";
        TxtUrlStatus.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("TextSecondaryBrush");

        var (isValid, error) = await WebHelper.ValidateUrlAsync(url);

        BtnTestUrl.IsEnabled = true;
        if (isValid)
        {
            TxtUrlStatus.Text = "连接成功";
            TxtUrlStatus.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("AccentGreenBrush");
        }
        else
        {
            TxtUrlStatus.Text = error ?? "连接失败";
            TxtUrlStatus.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("AccentOrangeBrush");
        }
    }

    private async void BtnFetchIcon_Click(object sender, RoutedEventArgs e)
    {
        var url = TxtTargetPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(url) || url == "https://")
        {
            TxtUrlStatus.Text = "请先输入网址";
            TxtUrlStatus.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("AccentOrangeBrush");
            return;
        }

        var abbr = TxtAbbreviation.Text.Trim();
        if (string.IsNullOrWhiteSpace(abbr))
        {
            TxtUrlStatus.Text = "请先输入缩写";
            TxtUrlStatus.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("AccentOrangeBrush");
            return;
        }

        BtnFetchIcon.IsEnabled = false;
        TxtUrlStatus.Text = "正在获取图标...";
        TxtUrlStatus.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("TextSecondaryBrush");

        var savePath = WebHelper.GetIconPath(abbr);
        var result = await WebHelper.FetchFaviconAsync(url, savePath);

        BtnFetchIcon.IsEnabled = true;
        if (result != null)
        {
            _customIconPath = result;
            ShowIconPreview(result);
            TxtUrlStatus.Text = "图标已获取";
            TxtUrlStatus.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("AccentGreenBrush");
        }
        else
        {
            TxtUrlStatus.Text = "未能获取图标";
            TxtUrlStatus.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("AccentOrangeBrush");
        }
    }

    private void BtnPickIcon_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择图标图片",
            Filter = "图片文件 (*.png;*.ico;*.jpg;*.jpeg;*.bmp)|*.png;*.ico;*.jpg;*.jpeg;*.bmp|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            var sourcePath = dialog.FileName;
            var abbr = TxtAbbreviation.Text.Trim();
            if (string.IsNullOrWhiteSpace(abbr))
            {
                TxtIconInfo.Text = "请先输入缩写";
                return;
            }

            // Copy to icons directory
            var iconsDir = WebHelper.GetIconsDirectory();
            if (!Directory.Exists(iconsDir))
                Directory.CreateDirectory(iconsDir);

            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            var destPath = Path.Combine(iconsDir, $"{abbr.ToUpperInvariant()}_custom{ext}");
            File.Copy(sourcePath, destPath, overwrite: true);

            _customIconPath = destPath;
            ShowIconPreview(destPath);
            TxtIconInfo.Text = Path.GetFileName(destPath);
        }
    }

    private void ShowIconPreview(string path)
    {
        try
        {
            if (!File.Exists(path)) return;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 32;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            bmp.StreamSource = stream;
            bmp.EndInit();
            bmp.Freeze();
            IconPreview.Source = bmp;
        }
        catch
        {
            // Ignore icon load errors
        }
    }

    // ═══ Abbreviation & Validation ═══

    private static string GenerateAbbreviation(string name) => AbbreviationGenerator.Generate(name);

    private void TxtAbbreviation_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb && !_isEditMode)
        {
            var caret = tb.CaretIndex;
            tb.Text = tb.Text.ToUpperInvariant();
            tb.CaretIndex = caret;
        }
    }

    private bool Validate()
    {
        TxtError.Text = "";

        if (string.IsNullOrWhiteSpace(TxtAbbreviation.Text))
        {
            TxtError.Text = "请输入缩写";
            TxtAbbreviation.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(TxtTargetPath.Text))
        {
            TxtError.Text = _mode == DialogMode.App ? "请选择程序路径" : "请输入网址";
            TxtTargetPath.Focus();
            return false;
        }

        if (_mode == DialogMode.App && !File.Exists(TxtTargetPath.Text.Trim()))
        {
            TxtError.Text = "文件不存在，请检查路径";
            TxtTargetPath.Focus();
            return false;
        }

        if (_mode == DialogMode.Url)
        {
            var url = TxtTargetPath.Text.Trim();
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                TxtError.Text = "网址必须以 http:// 或 https:// 开头";
                TxtTargetPath.Focus();
                return false;
            }
        }

        return true;
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate()) return;

        // For URL mode (add only): auto-fetch favicon if no custom icon was set
        if (_mode == DialogMode.Url && _customIconPath == null && !_isEditMode)
        {
            var url = TxtTargetPath.Text.Trim();
            var abbr = TxtAbbreviation.Text.Trim().ToUpperInvariant();
            var savePath = WebHelper.GetIconPath(abbr);

            BtnConfirm.IsEnabled = false;
            BtnConfirm.Content = "获取图标中...";

            var result = await WebHelper.FetchFaviconAsync(url, savePath);
            if (result != null)
                _customIconPath = result;

            BtnConfirm.IsEnabled = true;
            BtnConfirm.Content = "确定添加";
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
