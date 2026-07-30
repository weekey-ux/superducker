using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        try { InitializeComponent(); }
        catch (Exception ex) { throw new Exception($"[InitComponent] {ex.GetType().Name}: {ex.Message}", ex); }
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

            step = "DataDir";
            TxtDataDir.Text = DatabaseManager.GetRootDirectory();

            step = "Version";
            // 显示程序版本号（语义化版本，主.次.修订）
            TxtVersion.Text = $"v{SuperDucker.Shared.VersionHelper.GetVersion()}";

            step = "Hotkeys";
            BtnHotkeyToggle.Content = _vm.HotkeyToggle;
            BtnHotkeySettings.Content = _vm.HotkeySettings;
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

    private void BtnOpenDir_Click(object sender, RoutedEventArgs e)
    {
        var dir = DatabaseManager.GetRootDirectory();
        if (Directory.Exists(dir))
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
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
