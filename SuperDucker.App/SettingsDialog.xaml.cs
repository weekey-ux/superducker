using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SuperDucker.Shared;
using SuperDucker.Shared.Data;
using SuperDucker.Shared.Native;

namespace SuperDucker.App;

public partial class SettingsDialog : UserControl
{
    private readonly MainViewModel _vm;
    private bool _isLoading;
    private Button? _capturingButton;

    /// <summary>
    /// Raised when the user clicks the "Back" button.
    /// </summary>
    public event EventHandler? BackRequested;

    public SettingsDialog(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        // 关键：必须在 InitializeComponent 之前设为 true。
        // XAML 加载过程中 SelectedItem 绑定会触发 SelectionChanged → UpdateThemeButtons()，
        // 此时命名控件（如 CmbThemePreset）尚未被 InitializeComponent 赋值，会 NRE。
        _isLoading = true;
        try { InitializeComponent(); }
        catch (Exception ex)
        {
            // 把原始堆栈/InnerException 一起抛出，便于定位真正的根因
            var inner = ex.InnerException?.Message ?? "(null)";
            throw new Exception(
                $"[InitComponent] {ex.GetType().Name}: {ex.Message}\nInner: {inner}\nStack: {ex.StackTrace}\nInnerStack: {ex.InnerException?.StackTrace}",
                ex);
        }
        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        _isLoading = true;
        var step = "";
        try
        {
            step = "ViewMode";
            if (_vm.IsGridView) RbGrid.IsChecked = true;
            else RbList.IsChecked = true;

            step = "TabOrientation";
            if (_vm.IsTabHorizontal) RbTabH.IsChecked = true;
            else RbTabV.IsChecked = true;

            step = "ClickMode";
            if (_vm.IsDoubleClick) RbDoubleClick.IsChecked = true;
            else RbSingleClick.IsChecked = true;

            step = "DefaultShowFriendly";
            if (_vm.DefaultShowFriendly) RbShowFriendly.IsChecked = true;
            else RbShowCmd.IsChecked = true;

            step = "AutoStart";
            CbAutoStart.IsChecked = IsAutoStartEnabled();

            step = "WindowPosition";
            switch (_vm.WindowPosition)
            {
                case 1: RbPosMouse.IsChecked = true; break;
                case 2: RbPosLast.IsChecked = true; break;
                default: RbPosCenter.IsChecked = true; break;
            }

            step = "AutoHide";
            CbAutoHide.IsChecked = _vm.AutoHideEnabled;
            TxtAutoHideTimeout.Text = _vm.AutoHideTimeout.ToString();
            CbAutoHideMouseLeave.IsChecked = _vm.AutoHideOnMouseLeave;
            CbAutoHideOnLaunch.IsChecked = _vm.AutoHideOnLaunch;

            step = "HideTray";
            CbHideTrayIcon.IsChecked = _vm.HideTrayIcon;

            step = "HideAllTab";
            CbHideAllTab.IsChecked = _vm.HideAllTab;

            step = "HideShowTab";
            CbPreventSleep.IsChecked = _vm.PreventSleep;

            step = "StartMinimized";
            CbStartMinimized.IsChecked = _vm.StartMinimized;

            step = "ThemeMode";
            switch (_vm.ThemeMode)
            {
                case 1: RbThemeLight.IsChecked = true; break;
                case 2: RbThemeSystem.IsChecked = true; break;
                case 3: RbThemeCustom.IsChecked = true; break;
                default: RbThemeDark.IsChecked = true; break;
            }

            step = "CustomTheme";
            CustomThemeRow.Visibility = _vm.ThemeMode == 3 ? Visibility.Visible : Visibility.Collapsed;

            step = "Opacity";
            TxtBgOpacity.Text = $"{(int)(_vm.BackgroundOpacity * 100)}";
            TxtIconOpacity.Text = $"{(int)(_vm.IconOpacity * 100)}";

            step = "PathStatus";
            UpdatePathStatus();

            step = "ShopKeepDays";
            using (var dbKeep = new DatabaseManager(DatabaseManager.GetDefaultDbPath()))
            {
                var rawKeep = dbKeep.GetSetting("shop_package_keep_days");
                TxtKeepDays.Text = int.TryParse(rawKeep, out var kd) && kd > 0 ? kd.ToString() : "30";
            }

            step = "DataDir";
            TxtDataDir.Text = DatabaseManager.GetRootDirectory();

            step = "Version";
            // 显示程序版本号（语义化版本，主.次.修订）
            TxtVersion.Text = $"v{SuperDucker.Shared.VersionHelper.GetVersion()}";

            step = "Hotkeys";
            BtnHotkeyToggle.Content = _vm.HotkeyToggle;
            BtnHotkeySettings.Content = _vm.HotkeySettings;
            BtnHotkeyShop.Content = _vm.HotkeyShop;
            BtnHotkeyPack.Content = _vm.HotkeyPack;
        }
        catch (Exception ex)
        {
            throw new Exception($"[Step:{step}] {ex.GetType().Name}: {ex.Message}", ex);
        }
        finally
        {
            _isLoading = false;
        }
    }

    // ═══ General ═══

    private void RbShowCmd_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.DefaultShowFriendly = false;
    }

    private void RbShowFriendly_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.DefaultShowFriendly = true;
    }

    private void CbAutoStart_Checked(object sender, RoutedEventArgs e)
    {
        SetAutoStart(true);
    }

    private void CbAutoStart_Unchecked(object sender, RoutedEventArgs e)
    {
        SetAutoStart(false);
    }

    private void RbPosCenter_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.WindowPosition = 0;
    }

    private void RbPosMouse_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.WindowPosition = 1;
    }

    private void RbPosLast_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.WindowPosition = 2;
    }

    private void CbAutoHide_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.AutoHideEnabled = true;
    }

    private void CbAutoHide_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.AutoHideEnabled = false;
    }

    private void TxtAutoHideTimeout_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading) return;
        if (int.TryParse(TxtAutoHideTimeout.Text, out var val) && val >= 3 && val <= 300)
            _vm.AutoHideTimeout = val;
    }

    private void CbAutoHideMouseLeave_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.AutoHideOnMouseLeave = true;
    }

    private void CbAutoHideMouseLeave_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.AutoHideOnMouseLeave = false;
    }

    private void CbAutoHideOnLaunch_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.AutoHideOnLaunch = true;
    }

    private void CbAutoHideOnLaunch_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.AutoHideOnLaunch = false;
    }

    // ═══ User Habits ═══

    private void RbGrid_Checked(object sender, RoutedEventArgs e)
    {
        if (_vm != null) _vm.CurrentViewMode = ViewMode.Grid;
    }

    private void RbList_Checked(object sender, RoutedEventArgs e)
    {
        if (_vm != null) _vm.CurrentViewMode = ViewMode.List;
    }

    private void RbTabH_Checked(object sender, RoutedEventArgs e)
    {
        if (_vm != null) _vm.IsTabHorizontal = true;
    }

    private void RbTabV_Checked(object sender, RoutedEventArgs e)
    {
        if (_vm != null) _vm.IsTabHorizontal = false;
    }

    private void RbSingleClick_Checked(object sender, RoutedEventArgs e)
    {
        if (_vm != null) _vm.IsDoubleClick = false;
    }

    private void RbDoubleClick_Checked(object sender, RoutedEventArgs e)
    {
        if (_vm != null) _vm.IsDoubleClick = true;
    }

    private void CbHideTrayIcon_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.HideTrayIcon = true;
    }

    private void CbHideTrayIcon_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.HideTrayIcon = false;
    }

    private void CbHideAllTab_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.HideAllTab = true;
    }

    private void CbHideAllTab_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.HideAllTab = false;
    }

    private void CbPreventSleep_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.PreventSleep = true;
    }

    private void CbPreventSleep_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.PreventSleep = false;
    }

    private void CbStartMinimized_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.StartMinimized = true;
    }

    private void CbStartMinimized_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.StartMinimized = false;
    }

    // ═══ Theme ═══

    private void RbThemeDark_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.ThemeMode = 0;
        CustomThemeRow.Visibility = Visibility.Collapsed;
    }

    private void RbThemeLight_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.ThemeMode = 1;
        CustomThemeRow.Visibility = Visibility.Collapsed;
    }

    private void RbThemeSystem_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.ThemeMode = 2;
        CustomThemeRow.Visibility = Visibility.Collapsed;
    }

    private void RbThemeCustom_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _vm.ThemeMode = 3;
        CustomThemeRow.Visibility = Visibility.Visible;
        UpdateThemeButtons();
    }

    private void TxtBgOpacity_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading) return;
        var text = TxtBgOpacity.Text.TrimEnd('%');
        if (int.TryParse(text, out var pct) && pct >= 30 && pct <= 100)
            _vm.BackgroundOpacity = pct / 100.0;
    }

    private void TxtIconOpacity_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading) return;
        var text = TxtIconOpacity.Text.TrimEnd('%');
        if (int.TryParse(text, out var pct) && pct >= 30 && pct <= 100)
            _vm.IconOpacity = pct / 100.0;
    }

    // ═══ Hotkey Capture ═══

    private void BtnHotkeyToggle_Click(object sender, RoutedEventArgs e)
    {
        StartCapture(BtnHotkeyToggle);
    }

    private void BtnHotkeySettings_Click(object sender, RoutedEventArgs e)
    {
        StartCapture(BtnHotkeySettings);
    }

    private void BtnHotkeyShop_Click(object sender, RoutedEventArgs e)
    {
        StartCapture(BtnHotkeyShop);
    }

    private void BtnHotkeyPack_Click(object sender, RoutedEventArgs e)
    {
        StartCapture(BtnHotkeyPack);
    }

    private void StartCapture(Button button)
    {
        // If already capturing on this button, cancel
        if (_capturingButton == button)
        {
            CancelCapture();
            return;
        }
        // Cancel any current capture
        CancelCapture();

        _capturingButton = button;
        button.Content = "按下新快捷键...";
        button.Focus();
    }

    private void CancelCapture()
    {
        if (_capturingButton != null)
        {
            // Restore original content
            if (_capturingButton == BtnHotkeyToggle)
                _capturingButton.Content = _vm.HotkeyToggle;
            else if (_capturingButton == BtnHotkeySettings)
                _capturingButton.Content = _vm.HotkeySettings;
            else if (_capturingButton == BtnHotkeyShop)
                _capturingButton.Content = _vm.HotkeyShop;
            else if (_capturingButton == BtnHotkeyPack)
                _capturingButton.Content = _vm.HotkeyPack;
            _capturingButton = null;
        }
    }

    private void HotkeyCapture_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingButton == null) return;

        e.Handled = true;

        // Ignore pure modifier keys — wait for the main key
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        // Build modifier flags
        int modifiers = 0;
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            modifiers |= GlobalHotkeyManager.MOD_CONTROL;
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
            modifiers |= GlobalHotkeyManager.MOD_ALT;
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            modifiers |= GlobalHotkeyManager.MOD_SHIFT;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
            modifiers |= GlobalHotkeyManager.MOD_WIN;

        // Escape cancels capture
        if (key == Key.Escape && modifiers == 0)
        {
            CancelCapture();
            return;
        }

        // Must have at least one modifier
        if (modifiers == 0)
        {
            _capturingButton.Content = "需要修饰键 (Ctrl/Alt/Shift)";
            return;
        }

        var vk = GlobalHotkeyManager.KeyToVk(key);
        var hotkeyStr = GlobalHotkeyManager.ModifiersToString(modifiers, vk);

        if (_capturingButton == BtnHotkeyToggle)
        {
            _vm.HotkeyToggle = hotkeyStr;
            BtnHotkeyToggle.Content = hotkeyStr;
        }
        else if (_capturingButton == BtnHotkeySettings)
        {
            _vm.HotkeySettings = hotkeyStr;
            BtnHotkeySettings.Content = hotkeyStr;
        }
        else if (_capturingButton == BtnHotkeyShop)
        {
            _vm.HotkeyShop = hotkeyStr;
            BtnHotkeyShop.Content = hotkeyStr;
        }
        else if (_capturingButton == BtnHotkeyPack)
        {
            _vm.HotkeyPack = hotkeyStr;
            BtnHotkeyPack.Content = hotkeyStr;
        }

        _capturingButton = null;
        TxtStatus.Text = $"快捷键已更新: {hotkeyStr}";
    }

    // ═══ Path & Maintenance ═══

    private void UpdatePathStatus()
    {
        var isInPath = ShortcutManager.IsLinkInPath();
        TxtPathStatus.Text = isInPath ? "已注册 — Win+R 可直接输入缩写启动" : "未注册 — Win+R 无法使用缩写";
        TxtPathStatus.Foreground = isInPath
            ? (System.Windows.Media.SolidColorBrush)FindResource("AccentGreenBrush")
            : (System.Windows.Media.SolidColorBrush)FindResource("AccentOrangeBrush");
        BtnPathToggle.Content = isInPath ? "取消注册" : "注册";
    }

    private void BtnPathToggle_Click(object sender, RoutedEventArgs e)
    {
        if (ShortcutManager.IsLinkInPath())
        {
            ShortcutManager.RemoveLinkFromPath();
            TxtStatus.Text = "已取消 PATH 注册";
        }
        else
        {
            ShortcutManager.EnsureLinkInPath();
            TxtStatus.Text = "已注册到 PATH";
        }
        UpdatePathStatus();
    }

    private void BtnRepair_Click(object sender, RoutedEventArgs e)
    {
        using var db = new DatabaseManager(DatabaseManager.GetDefaultDbPath());
        var count = ShortcutManager.RepairAllShortcuts(db);
        TxtStatus.Text = $"已修复 {count} 个快捷方式";
    }

    private void BtnRescan_Click(object sender, RoutedEventArgs e)
    {
        using var db = new DatabaseManager(DatabaseManager.GetDefaultDbPath());
        var result = RescanHelper.Rescan(db);
        if (result.Recovered > 0)
        {
            var names = string.Join("\n", result.RecoveredNames.Select(n => $"  • {n}"));
            TxtStatus.Text = $"已恢复 {result.Recovered} 个程序";
            MessageBox.Show($"扫描完成!\n\n恢复: {result.Recovered}\n跳过: {result.Skipped}\n失败: {result.Failed}\n\n已恢复:\n{names}",
                "扫描恢复", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            TxtStatus.Text = $"扫描完成 — 无需恢复 (跳过 {result.Skipped})";
        }
    }

    /// <summary>本地商店未安装 .sdzip 安装包的保留天数（默认 30 天，超过自动清理）。</summary>
    private void TxtKeepDays_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading) return;
        if (!int.TryParse(TxtKeepDays.Text, out var days) || days <= 0) return;
        try
        {
            using var db = new DatabaseManager(DatabaseManager.GetDefaultDbPath());
            db.SetSetting("shop_package_keep_days", days.ToString());
            TxtStatus.Text = $"安装包保留天数已设为 {days} 天";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"保存失败: {ex.Message}";
        }
    }

    private void BtnOpenDir_Click(object sender, RoutedEventArgs e)
    {
        var dir = DatabaseManager.GetRootDirectory();
        if (Directory.Exists(dir))
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    // ═══ About: GitHub link & Update check ═══

    /// <summary>点击"GitHub 主页"链接：使用系统默认浏览器打开。</summary>
    private void LinkGitHub_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = UpdateChecker.DefaultRepoUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"无法打开浏览器：{ex.Message}";
        }
    }

    /// <summary>点击"检查更新"：异步请求 GitHub Release API 并以弹窗反馈结果。</summary>
    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        // 防重复点击：检查中禁用按钮，避免用户连点造成多次请求
        BtnCheckUpdate.IsEnabled = false;
        TxtStatus.Text = "正在检查更新...";
        try
        {
            var current = SuperDucker.Shared.VersionHelper.GetVersion();
            var result = await UpdateChecker.CheckAsync(current);

            if (result.Failed)
            {
                // 网络/解析失败：静默文案 + 状态栏提示，不弹模态框打扰用户
                TxtStatus.Text = $"检查更新失败：{result.ErrorMessage}";
                MessageBox.Show(
                    $"无法连接到 GitHub 检查更新。\n\n{result.ErrorMessage}\n\n您可以稍后重试，或直接访问项目主页查看最新版本。",
                    "检查更新",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (result.HasUpdate)
            {
                var notes = string.IsNullOrWhiteSpace(result.ReleaseNotes)
                    ? "（无发行说明）"
                    : result.ReleaseNotes!.Trim();
                // 截断过长的 Markdown 避免弹窗被撑爆
                if (notes.Length > 1200) notes = notes.Substring(0, 1200) + "\n\n…（已截断，完整内容请查看 Release 页面）";

                var open = MessageBox.Show(
                    $"发现新版本 v{result.LatestVersion}（当前 v{result.CurrentVersion}）。\n\n{notes}\n\n是否前往 GitHub Release 页面下载？\n（绿色软件，覆盖程序目录的 exe 即可完成升级，数据无迁移）",
                    "发现新版本",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (open == MessageBoxResult.Yes)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = result.ReleaseUrl,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        TxtStatus.Text = $"无法打开浏览器：{ex.Message}";
                    }
                }
                else
                {
                    TxtStatus.Text = $"已跳过更新（最新 v{result.LatestVersion}）";
                }
            }
            else
            {
                MessageBox.Show(
                    $"已是最新版本（v{result.CurrentVersion}）。",
                    "检查更新",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                TxtStatus.Text = "已是最新版本";
            }
        }
        catch (Exception ex)
        {
            // 任何意外都被吞掉，更新检查绝不能让程序崩溃
            TxtStatus.Text = $"检查更新异常：{ex.Message}";
        }
        finally
        {
            BtnCheckUpdate.IsEnabled = true;
        }
    }

    // ═══ Theme Preset Management ═══

    private void CmbThemePreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        // SelectedPreset 已通过 TwoWay 绑定更新，这里仅刷新删除/编辑按钮状态
        UpdateThemeButtons();
    }

    private void UpdateThemeButtons()
    {
        // 防御：InitializeComponent 阶段 XAML 绑定可能触发 SelectionChanged，
        // 此时命名控件字段尚未赋值（CmbThemePreset == null），直接访问会 NRE。
        if (CmbThemePreset == null || BtnEditTheme == null || BtnDeleteTheme == null) return;
        var preset = CmbThemePreset.SelectedItem as ThemePreset;
        BtnEditTheme.IsEnabled = preset != null && !preset.IsBuiltIn;
        BtnDeleteTheme.IsEnabled = preset != null && !preset.IsBuiltIn;
    }

    /// <summary>新建主题：以当前选中的预设（内建或自定义均可）作为起点派生。</summary>
    private void BtnNewTheme_Click(object sender, RoutedEventArgs e)
    {
        var basePreset = _vm.SelectedPreset ?? _vm.ThemePresets.First(p => p.IsBuiltIn);
        var editor = new ThemeEditorDialog(
            basePreset,
            _vm.ThemePresets.Where(p => !p.IsBuiltIn).ToList(),
            isEdit: false)
        {
            Owner = Window.GetWindow(this)
        };
        editor.OnSaved += preset =>
        {
            _vm.SaveCustomTheme(preset);
            UpdateThemeButtons();
        };
        editor.ShowDialog();
    }

    /// <summary>编辑当前选中的自定义主题（内建不可编辑，按钮已禁用）。</summary>
    private void BtnEditTheme_Click(object sender, RoutedEventArgs e)
    {
        var preset = CmbThemePreset.SelectedItem as ThemePreset;
        if (preset == null || preset.IsBuiltIn) return;

        var editor = new ThemeEditorDialog(
            preset,
            _vm.ThemePresets.Where(p => !p.IsBuiltIn).ToList(),
            isEdit: true)
        {
            Owner = Window.GetWindow(this)
        };
        editor.OnSaved += edited =>
        {
            // 编辑时保持原名（editor 内部已锁名），覆盖原色板
            _vm.SaveCustomTheme(edited.Clone(preset.Name));
            UpdateThemeButtons();
        };
        editor.ShowDialog();
    }

    /// <summary>删除当前选中的自定义主题（内建不可删）。</summary>
    private void BtnDeleteTheme_Click(object sender, RoutedEventArgs e)
    {
        var preset = CmbThemePreset.SelectedItem as ThemePreset;
        if (preset == null || preset.IsBuiltIn) return;

        var result = MessageBox.Show(
            $"确定删除主题「{preset.Name}」吗？此操作不可撤销。",
            "删除主题",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        _vm.DeleteCustomTheme(preset);
        UpdateThemeButtons();
    }

    // ═══ Auto-start helpers ═══

    private static string GetAutoStartShortcutPath()
    {
        var startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        return Path.Combine(startupDir, "SuperDucker.lnk");
    }

    private static bool IsAutoStartEnabled()
    {
        return File.Exists(GetAutoStartShortcutPath());
    }

    private static void SetAutoStart(bool enable)
    {
        var lnkPath = GetAutoStartShortcutPath();
        if (enable)
        {
            var exePath = Environment.ProcessPath;
            if (exePath == null) return;

            var exeName = Path.GetFileName(exePath);
            if (exeName.StartsWith("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                exePath = Path.Combine(AppContext.BaseDirectory, "SuperDucker.App.exe");
                if (!File.Exists(exePath)) return;
            }

            ShortcutManager.CreateRawShortcut(exePath, lnkPath, "SuperDucker");
        }
        else
        {
            if (File.Exists(lnkPath))
            {
                // Move to recycle bin instead of permanent delete
                try
                {
                    var tempPath = Path.Combine(Path.GetTempPath(), "SD_" + Path.GetFileName(lnkPath));
                    File.Move(lnkPath, tempPath);
                    // Try to move to recycle bin using Windows API
                    RecycleBinHelper.MoveToRecycleBin(tempPath);
                }
                catch
                {
                    // If all else fails, just leave the file - user can manually clean up
                }
            }
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }
}
