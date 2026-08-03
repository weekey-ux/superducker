using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SuperDucker.Shared.Data;
using SuperDucker.Shared.Models;

namespace SuperDucker.App;

public partial class ShopPanel : UserControl
{
    private enum ShopTab { Available, Installed, Uninstalled }

    private readonly MainViewModel _vm;
    private List<ShopPackage> _packages = new();
    private ShopTab _currentTab = ShopTab.Available;

    // Search / category filter state (controls built in code-behind)
    private TextBox _searchBox = null!;
    private ComboBox _categoryCombo = null!;
    private string _keyword = "";
    private string _category = "";

    public event EventHandler? BackRequested;
    public event EventHandler? Installed;

    public ShopPanel(MainViewModel vm)
    {
        _vm = vm;
        try { InitializeComponent(); }
        catch (Exception ex) { throw new Exception($"[InitComponent] {ex.GetType().Name}: {ex.Message}", ex); }
        BuildSearchBar();
        RefreshPackages();
    }

    private void BuildSearchBar()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // search box
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                     // category combo

        // Search box (left, fills remaining)
        _searchBox = new TextBox
        {
            MinWidth = 160,
            Height = 32,
            Padding = new Thickness(10, 0, 10, 0),
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = (SolidColorBrush)FindResource("BgDarkBrush"),
            Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush"),
            CaretBrush = (SolidColorBrush)FindResource("TextPrimaryBrush"),
            BorderBrush = (SolidColorBrush)FindResource("BgCardHoverBrush"),
            BorderThickness = new Thickness(1),
            ToolTip = "搜索名称 / 缩写 / 作者 / 描述…"
        };
        _searchBox.TextChanged += SearchBox_TextChanged;
        Grid.SetColumn(_searchBox, 0);
        grid.Children.Add(_searchBox);

        // Category combo (right)
        _categoryCombo = new ComboBox
        {
            MinWidth = 130,
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Style = (Style)FindResource("DarkComboBoxStyle"),
            ItemContainerStyle = (Style)FindResource("DarkComboBoxItemStyle")
        };
        _categoryCombo.SelectionChanged += CategoryCombo_SelectionChanged;
        Grid.SetColumn(_categoryCombo, 1);
        grid.Children.Add(_categoryCombo);

        SearchBar.Child = grid;
        SearchBar.Visibility = Visibility.Visible;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _keyword = _searchBox.Text.Trim().ToLowerInvariant();
        RenderList();
    }

    private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _category = (_categoryCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        RenderList();
    }

    private void LoadCategories()
    {
        _categoryCombo.Items.Clear();

        var all = new ComboBoxItem { Content = "全部分类", Tag = "" };
        _categoryCombo.Items.Add(all);

        var cats = _packages
            .Select(p => p.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        foreach (var c in cats)
        {
            _categoryCombo.Items.Add(new ComboBoxItem { Content = c, Tag = c });
        }

        _categoryCombo.SelectedIndex = 0;
        _category = "";
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshPackages();

    private void TabAvailable_Click(object sender, RoutedEventArgs e)
    {
        _currentTab = ShopTab.Available;
        UpdateTabWeights();
        RenderList();
    }

    private void TabInstalled_Click(object sender, RoutedEventArgs e)
    {
        _currentTab = ShopTab.Installed;
        UpdateTabWeights();
        RenderList();
    }

    private void TabUninstalled_Click(object sender, RoutedEventArgs e)
    {
        _currentTab = ShopTab.Uninstalled;
        UpdateTabWeights();
        RenderList();
    }

    private void UpdateTabWeights()
    {
        TabAvailable.FontWeight = _currentTab == ShopTab.Available ? FontWeights.SemiBold : FontWeights.Normal;
        TabInstalled.FontWeight = _currentTab == ShopTab.Installed ? FontWeights.SemiBold : FontWeights.Normal;
        TabUninstalled.FontWeight = _currentTab == ShopTab.Uninstalled ? FontWeights.SemiBold : FontWeights.Normal;
    }

    public void RefreshPackages()
    {
        try
        {
            using var db = new DatabaseManager(DatabaseManager.GetDefaultDbPath());
            // 面板打开/刷新时先清理过期的未安装包（保留天数取自设置，默认 30 天）
            var keepDays = GetKeepDays(db);
            var removed = ShopManager.CleanupExpiredPackages(db, keepDays);
            if (removed.Count > 0)
                System.Diagnostics.Debug.WriteLine($"[Shop] 自动清理 {removed.Count} 个过期安装包");

            _packages = ShopManager.ScanPackages(db);
        }
        catch (Exception ex)
        {
            _packages = new List<ShopPackage>();
            TxtStatus.Text = $"扫描失败: {ex.Message}";
            return;
        }

        UpdateTabCounts();
        LoadCategories();

        var shopDir = ShopManager.GetShopDirectory();
        TxtStatus.Text = $"共 {_packages.Count} 个包  ·  目录: {shopDir}";

        RenderList();
    }

    private void UpdateTabCounts()
    {
        var available = _packages.Count(p => !p.IsInstalled && !p.IsUninstalled);
        var installed = _packages.Count(p => p.IsInstalled);
        var uninstalled = _packages.Count(p => p.IsUninstalled);

        TabAvailable.Content = $"可安装 ({available})";
        TabInstalled.Content = $"已安装 ({installed})";
        TabUninstalled.Content = $"已卸载 ({uninstalled})";
    }

    private void RenderList()
    {
        PackageList.Children.Clear();

        var filtered = _currentTab switch
        {
            ShopTab.Available => _packages.Where(p => !p.IsInstalled && !p.IsUninstalled).ToList(),
            ShopTab.Installed => _packages.Where(p => p.IsInstalled).ToList(),
            ShopTab.Uninstalled => _packages.Where(p => p.IsUninstalled).ToList(),
            _ => _packages.ToList()
        };

        // Apply keyword + category filters
        if (!string.IsNullOrEmpty(_keyword))
        {
            filtered = filtered.Where(p =>
                (p.Name ?? "").ToLowerInvariant().Contains(_keyword) ||
                (p.Abbreviation ?? "").ToLowerInvariant().Contains(_keyword) ||
                (p.Author ?? "").ToLowerInvariant().Contains(_keyword) ||
                (p.Description ?? "").ToLowerInvariant().Contains(_keyword)).ToList();
        }

        if (!string.IsNullOrEmpty(_category))
        {
            filtered = filtered.Where(p => (p.Category ?? "") == _category).ToList();
        }

        if (filtered.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            EmptyHint.Visibility = Visibility.Visible;

            switch (_currentTab)
            {
                case ShopTab.Available:
                    EmptyText.Text = "暂无可安装的软件包";
                    EmptyHint.Text = $"将 .sdzip 文件放入 {ShopManager.GetShopDirectory()} 目录";
                    break;
                case ShopTab.Installed:
                    EmptyText.Text = "暂无已安装的软件包";
                    EmptyHint.Text = "从「可安装」标签页安装软件";
                    break;
                case ShopTab.Uninstalled:
                    EmptyText.Text = "暂无已卸载的软件包";
                    EmptyHint.Text = "在「已安装」标签页卸载的软件会显示在这里";
                    break;
            }
        }
        else
        {
            EmptyState.Visibility = Visibility.Collapsed;
        }

        foreach (var pkg in filtered)
        {
            PackageList.Children.Add(BuildPackageCard(pkg));
        }
    }

    private Border BuildPackageCard(ShopPackage pkg)
    {
        var card = new Border
        {
            Background = (SolidColorBrush)FindResource("BgCardBrush"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var dock = new DockPanel();

        // Icon (left side)
        var iconContainer = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 12, 0),
            Background = (SolidColorBrush)FindResource("BgMediumBrush")
        };
        DockPanel.SetDock(iconContainer, Dock.Left);

        if (!string.IsNullOrEmpty(pkg.IconPath) && File.Exists(pkg.IconPath))
        {
            try
            {
                var ext = Path.GetExtension(pkg.IconPath).ToLowerInvariant();
                if (ext == ".ico")
                {
                    // Load ICO file - need to keep stream alive during decode
                    var iconBytes = File.ReadAllBytes(pkg.IconPath);
                    using var iconStream = new MemoryStream(iconBytes);
                    var decoder = BitmapDecoder.Create(iconStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    
                    // Find the best frame (closest to 40px, prefer larger if no exact match)
                    var frame = decoder.Frames
                        .OrderBy(f => Math.Abs(f.PixelWidth - 40))
                        .ThenByDescending(f => f.PixelWidth)
                        .First();
                    
                    // Create a transformed bitmap if needed
                    BitmapSource source = frame;
                    if (frame.PixelWidth != 40 || frame.PixelHeight != 40)
                    {
                        source = new TransformedBitmap(frame, new ScaleTransform(40.0 / frame.PixelWidth, 40.0 / frame.PixelHeight));
                    }
                    
                    var img = new System.Windows.Controls.Image
                    {
                        Source = source,
                        Width = 40,
                        Height = 40,
                        Stretch = Stretch.Uniform
                    };
                    iconContainer.Child = img;
                }
                else
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(pkg.IconPath, UriKind.Absolute);
                    bi.DecodePixelWidth = 40;
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();
                    iconContainer.Child = new System.Windows.Controls.Image
                    {
                        Source = bi,
                        Width = 40,
                        Height = 40,
                        Stretch = Stretch.Uniform
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load icon from {pkg.IconPath}: {ex.Message}");
                iconContainer.Child = CreateFallbackIcon();
            }
        }
        else
        {
            iconContainer.Child = CreateFallbackIcon();
        }
        dock.Children.Add(iconContainer);

        // Action buttons (right side)
        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 0, 0, 0)
        };
        DockPanel.SetDock(actionPanel, Dock.Right);

        switch (_currentTab)
        {
            case ShopTab.Available:
                var installBtn = new Button
                {
                    Content = "安装",
                    MinWidth = 72,
                    Margin = new Thickness(0, 0, 8, 0),
                    Style = (Style)FindResource("FlatButton")
                };
                installBtn.Click += (_, _) => InstallPackage(pkg, installBtn);

                var delLocalBtn = new Button
                {
                    Content = "删除安装包",
                    MinWidth = 84,
                    Style = (Style)FindResource("FlatButton")
                };
                delLocalBtn.Click += (_, _) => DeleteLocalPackage(pkg);
                actionPanel.Children.Add(installBtn);
                actionPanel.Children.Add(delLocalBtn);
                break;

            case ShopTab.Installed:
                // 升级 / 重装：当本地包版本 ≥ 已安装版本时放行（高于→升级，等于/低于→重装）
                if (pkg.UpgradeState != ShopUpgradeState.None)
                {
                    var upgradeLabel = pkg.UpgradeState == ShopUpgradeState.Upgrade
                        ? $"升级 → v{pkg.Version}"
                        : $"重装 v{pkg.Version}";
                    var upgradeBtn = new Button
                    {
                        Content = upgradeLabel,
                        MinWidth = 96,
                        Margin = new Thickness(0, 0, 8, 0),
                        Style = (Style)FindResource("FlatButton")
                    };
                    upgradeBtn.Click += (_, _) => UpgradeOrReinstallPackage(pkg, upgradeBtn);
                    actionPanel.Children.Add(upgradeBtn);
                }

                var uninstallBtn = new Button
                {
                    Content = "卸载",
                    MinWidth = 60,
                    Margin = new Thickness(0, 0, 8, 0),
                    Style = (Style)FindResource("FlatButton")
                };
                uninstallBtn.Click += (_, _) => UninstallPackage(pkg, uninstallBtn);

                var deleteBtn = new Button
                {
                    Content = "删除",
                    MinWidth = 60,
                    Style = (Style)FindResource("FlatButton")
                };
                deleteBtn.Click += (_, _) => DeletePackage(pkg);

                actionPanel.Children.Add(uninstallBtn);
                actionPanel.Children.Add(deleteBtn);
                break;

            case ShopTab.Uninstalled:
                var reinstallBtn = new Button
                {
                    Content = "重新安装",
                    MinWidth = 84,
                    Margin = new Thickness(0, 0, 8, 0),
                    Style = (Style)FindResource("FlatButton")
                };
                reinstallBtn.Click += (_, _) => ReinstallPackage(pkg, reinstallBtn);

                var delLocalBtn2 = new Button
                {
                    Content = "删除安装包",
                    MinWidth = 84,
                    Style = (Style)FindResource("FlatButton")
                };
                delLocalBtn2.Click += (_, _) => DeleteLocalPackage(pkg);
                actionPanel.Children.Add(reinstallBtn);
                actionPanel.Children.Add(delLocalBtn2);
                break;
        }

        dock.Children.Add(actionPanel);

        // Info (fills remaining space)
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(new TextBlock
        {
            Text = pkg.Name,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        nameRow.Children.Add(new TextBlock
        {
            Text = $"  v{pkg.Version}",
            FontSize = 11,
            Foreground = (SolidColorBrush)FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        info.Children.Add(nameRow);

        if (!string.IsNullOrEmpty(pkg.Description))
        {
            info.Children.Add(new TextBlock
            {
                Text = pkg.Description,
                FontSize = 11,
                Foreground = (SolidColorBrush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 500
            });
        }

        dock.Children.Add(info);
        card.Child = dock;
        return card;
    }

    private static TextBlock CreateFallbackIcon()
    {
        return new TextBlock
        {
            Text = "\u2B1B",
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.3
        };
    }

    private void InstallPackage(ShopPackage pkg, Button btn)
    {
        try
        {
            btn.IsEnabled = false;
            btn.Content = "安装中...";

            using var db = new DatabaseManager(DatabaseManager.GetDefaultDbPath());
            var entry = ShopManager.InstallPackage(pkg, db);

            if (entry != null)
            {
                _vm.LoadData();
                TxtStatus.Text = $"已安装: {pkg.Name} v{pkg.Version}";
                UpdateTabCounts();
                Installed?.Invoke(this, EventArgs.Empty);
                RenderList();
            }
            else
            {
                btn.Content = "安装";
                btn.IsEnabled = true;
                TxtStatus.Text = $"安装失败: 目录已存在或缩写冲突";
            }
        }
        catch (Exception ex)
        {
            btn.Content = "安装";
            btn.IsEnabled = true;
            TxtStatus.Text = $"安装失败: {ex.Message}";
        }
    }

    private void UninstallPackage(ShopPackage pkg, Button btn)
    {
        try
        {
            btn.IsEnabled = false;
            btn.Content = "卸载中...";

            using var db = new DatabaseManager(DatabaseManager.GetDefaultDbPath());
            if (ShopManager.UninstallPackage(pkg, db))
            {
                _vm.LoadData();
                TxtStatus.Text = $"已卸载: {pkg.Name}";
                UpdateTabCounts();
                RenderList();
            }
            else
            {
                btn.Content = "卸载";
                btn.IsEnabled = true;
                TxtStatus.Text = $"卸载失败: {pkg.Name}";
            }
        }
        catch (Exception ex)
        {
            btn.Content = "卸载";
            btn.IsEnabled = true;
            TxtStatus.Text = $"卸载失败: {ex.Message}";
        }
    }

    private void DeletePackage(ShopPackage pkg)
    {
        var result = MessageBox.Show(
            $"确定要彻底删除「{pkg.Name}」吗？\n\n这将删除软件配置、数据库记录以及 app 目录下的所有文件。localshop 中的安装包不会被删除。",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            using var db = new DatabaseManager(DatabaseManager.GetDefaultDbPath());
            if (ShopManager.DeletePackage(pkg, db))
            {
                _vm.LoadData();
                TxtStatus.Text = $"已删除: {pkg.Name}";
                UpdateTabCounts();
                RenderList();
            }
            else
            {
                TxtStatus.Text = $"删除失败: {pkg.Name}";
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"删除失败: {ex.Message}";
        }
    }

    private void ReinstallPackage(ShopPackage pkg, Button btn)
    {
        try
        {
            btn.IsEnabled = false;
            btn.Content = "安装中...";

            using var db = new DatabaseManager(DatabaseManager.GetDefaultDbPath());
            var entry = ShopManager.InstallPackage(pkg, db);

            if (entry != null)
            {
                _vm.LoadData();
                TxtStatus.Text = $"已重新安装: {pkg.Name} v{pkg.Version}";
                UpdateTabCounts();

                // Switch to the Installed tab as requested
                _currentTab = ShopTab.Installed;
                UpdateTabWeights();
                RenderList();

                Installed?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                btn.Content = "重新安装";
                btn.IsEnabled = true;
                TxtStatus.Text = $"重新安装失败: {pkg.Name}";
            }
        }
        catch (Exception ex)
        {
            btn.Content = "重新安装";
            btn.IsEnabled = true;
            TxtStatus.Text = $"重新安装失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 升级或覆盖重装已安装应用：高于已装版本→升级；等于/低于→重装（覆盖）。
    /// </summary>
    private void UpgradeOrReinstallPackage(ShopPackage pkg, Button btn)
    {
        var isUpgrade = pkg.UpgradeState == ShopUpgradeState.Upgrade;
        try
        {
            btn.IsEnabled = false;
            btn.Content = isUpgrade ? "升级中..." : "重装中...";

            using var db = new DatabaseManager(DatabaseManager.GetDefaultDbPath());
            var entry = ShopManager.UpgradePackage(pkg, db);

            if (entry != null)
            {
                _vm.LoadData();
                TxtStatus.Text = isUpgrade
                    ? $"已升级: {pkg.Name} → v{pkg.Version}"
                    : $"已重装: {pkg.Name} v{pkg.Version}";
                UpdateTabCounts();
                Installed?.Invoke(this, EventArgs.Empty);
                RenderList();
            }
            else
            {
                btn.Content = isUpgrade ? $"升级 → v{pkg.Version}" : $"重装 v{pkg.Version}";
                btn.IsEnabled = true;
                TxtStatus.Text = $"{(isUpgrade ? "升级" : "重装")}失败: {pkg.Name}";
            }
        }
        catch (Exception ex)
        {
            btn.Content = isUpgrade ? $"升级 → v{pkg.Version}" : $"重装 v{pkg.Version}";
            btn.IsEnabled = true;
            TxtStatus.Text = $"{(isUpgrade ? "升级" : "重装")}失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 手动删除本地商店中的 .sdzip 安装包（不影响已安装应用）。
    /// </summary>
    private void DeleteLocalPackage(ShopPackage pkg)
    {
        var warn = pkg.IsInstalled
            ? $"确定要删除 localshop 中的安装包「{pkg.Name}」吗？\n\n已安装的应用不受影响，但将失去此版本的升级/重装来源。"
            : $"确定要删除安装包「{pkg.Name}」吗？此文件将被永久删除。";

        var result = MessageBox.Show(warn, "确认删除安装包", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            using var db = new DatabaseManager(DatabaseManager.GetDefaultDbPath());
            if (ShopManager.DeleteLocalPackage(pkg, db))
            {
                TxtStatus.Text = $"已删除安装包: {pkg.Name}";
                RefreshPackages();
                UpdateTabCounts();
                RenderList();
            }
            else
            {
                TxtStatus.Text = $"删除安装包失败: {pkg.Name}";
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"删除安装包失败: {ex.Message}";
        }
    }

    /// <summary>从设置表读取安装包保留天数（默认 30 天）。</summary>
    private static int GetKeepDays(DatabaseManager db)
    {
        var raw = db.GetSetting("shop_package_keep_days");
        if (int.TryParse(raw, out var days) && days > 0)
            return days;
        return 30;
    }
}
