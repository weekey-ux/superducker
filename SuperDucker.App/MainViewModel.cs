using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SuperDucker.Shared.Data;
using SuperDucker.Shared.Models;

namespace SuperDucker.App;

public enum ViewMode { Grid, List }

/// <summary>
/// ViewModel for the main panel window.
/// </summary>
public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly DatabaseManager _db;

    /// <summary>Exposes the underlying database manager for dialogs that need it (e.g. PackDialog).</summary>
    public DatabaseManager Db => _db;
    private string _searchText = "";
    private bool _showFriendlyNames;
    private bool _defaultShowFriendly;
    private ViewMode _viewMode = ViewMode.Grid;
    private bool _isTabHorizontal = true;
    private bool _isDoubleClick = false;
    private TabViewModel? _selectedTab;
    private int? _selectedTabBeforeLoad;
    private int _windowPosition = 0;
    private bool _autoHideEnabled = false;
    private int _autoHideTimeout = 10;
    private bool _autoHideOnLaunch = true;
    private bool _autoHideOnMouseLeave = false;
    private bool _startMinimized = false;
    private bool _hideTrayIcon = false;
    private bool _hideAllTab = false;
    private bool _preventSleep = false;
    private bool _preventSleepActive = false;
    private int _themeMode = 0;
    private double _backgroundOpacity = 1.0;
    private double _iconOpacity = 1.0;
    private string _hotkeyToggle = "Ctrl+Shift+S";
    private string _hotkeySettings = "Ctrl+Shift+G";
    private string _hotkeyShop = "Alt+F4";
    private string _hotkeyPack = "Alt+F5";

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Fired after items are refreshed and the view should be rebuilt.</summary>
    public event Action? RebuildNeeded;

    /// <summary>Fired when any theme-related setting changes (theme mode, custom theme, opacity).</summary>
    public event Action? ThemeChanged;

    /// <summary>Fired when hotkey settings change.</summary>
    public event Action? HotkeyChanged;

    // ═══ Collections ═══
    public ObservableCollection<TabViewModel> Tabs { get; } = new();
    public ObservableCollection<PanelItemViewModel> Items { get; } = new();

    // All apps/urls cached for filtering by tab
    private readonly List<AppEntry> _allApps = new();
    private readonly List<UrlEntry> _allUrls = new();

    // PanelItemViewModel cache: avoid recreating on every tab switch / LoadData
    private readonly Dictionary<string, PanelItemViewModel> _itemCache = new();

    private string AppKey(int id) => $"a:{id}";
    private string UrlKey(int id) => $"u:{id}";

    // ═══ Properties ═══
    public TabViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (_selectedTab != value)
            {
                if (_selectedTab != null) _selectedTab.IsSelected = false;
                _selectedTab = value;
                if (_selectedTab != null) _selectedTab.IsSelected = true;
                PropertyChanged?.Invoke(this, new(nameof(SelectedTab)));
                RefreshItems();
                RebuildNeeded?.Invoke();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value;
                PropertyChanged?.Invoke(this, new(nameof(SearchText)));
                ApplyFilter();
            }
        }
    }

    public bool ShowFriendlyNames
    {
        get => _showFriendlyNames;
        set
        {
            if (_showFriendlyNames != value)
            {
                _showFriendlyNames = value;
                PropertyChanged?.Invoke(this, new(nameof(ShowFriendlyNames)));
                ApplyFriendlyDisplay();
            }
        }
    }

    /// <summary>
    /// When true, items show the friendly name by default and Ctrl swaps to the command.
    /// When false (default), items show the command by default and Ctrl swaps to the friendly name.
    /// </summary>
    public bool DefaultShowFriendly
    {
        get => _defaultShowFriendly;
        set
        {
            if (_defaultShowFriendly != value)
            {
                _defaultShowFriendly = value;
                PropertyChanged?.Invoke(this, new(nameof(DefaultShowFriendly)));
                PropertyChanged?.Invoke(this, new(nameof(FriendlyModeHint)));
                _db.SetSetting("default_friendly", value.ToString());
                ApplyFriendlyDisplay();
            }
        }
    }

    /// <summary>Hint text for the status bar describing the current Ctrl toggle behavior.</summary>
    public string FriendlyModeHint =>
        _defaultShowFriendly ? "按住 Ctrl 显示命令" : "按住 Ctrl 显示友好名称";

    /// <summary>Re-apply the display name to every item based on the current
    /// Ctrl state and the DefaultShowFriendly preference.</summary>
    private void ApplyFriendlyDisplay()
    {
        bool showFriendly = _defaultShowFriendly ? !_showFriendlyNames : _showFriendlyNames;
        foreach (var item in Items) item.UpdateDisplayName(showFriendly);
    }

    public ViewMode CurrentViewMode
    {
        get => _viewMode;
        set
        {
            if (_viewMode != value)
            {
                _viewMode = value;
                PropertyChanged?.Invoke(this, new(nameof(CurrentViewMode)));
                PropertyChanged?.Invoke(this, new(nameof(IsGridView)));
                PropertyChanged?.Invoke(this, new(nameof(IsListView)));
                _db.SetSetting("view_mode", value.ToString());
            }
        }
    }

    public bool IsGridView => _viewMode == ViewMode.Grid;
    public bool IsListView => _viewMode == ViewMode.List;

    public bool IsTabHorizontal
    {
        get => _isTabHorizontal;
        set
        {
            if (_isTabHorizontal != value)
            {
                _isTabHorizontal = value;
                PropertyChanged?.Invoke(this, new(nameof(IsTabHorizontal)));
                PropertyChanged?.Invoke(this, new(nameof(IsTabVertical)));
                _db.SetSetting("tab_horizontal", value.ToString());
            }
        }
    }

    public bool IsTabVertical => !_isTabHorizontal;

    public bool IsDoubleClick
    {
        get => _isDoubleClick;
        set
        {
            if (_isDoubleClick != value)
            {
                _isDoubleClick = value;
                PropertyChanged?.Invoke(this, new(nameof(IsDoubleClick)));
                _db.SetSetting("click_mode", value ? "double" : "single");
            }
        }
    }

    /// <summary>0=screen center, 1=follow mouse, 2=last position.</summary>
    public int WindowPosition
    {
        get => _windowPosition;
        set
        {
            if (_windowPosition != value)
            {
                _windowPosition = value;
                PropertyChanged?.Invoke(this, new(nameof(WindowPosition)));
                _db.SetSetting("window_position", value.ToString());
            }
        }
    }

    public bool AutoHideEnabled
    {
        get => _autoHideEnabled;
        set
        {
            if (_autoHideEnabled != value)
            {
                _autoHideEnabled = value;
                PropertyChanged?.Invoke(this, new(nameof(AutoHideEnabled)));
                _db.SetSetting("auto_hide_enabled", value.ToString());
            }
        }
    }

    /// <summary>Auto-hide timeout in seconds.</summary>
    public int AutoHideTimeout
    {
        get => _autoHideTimeout;
        set
        {
            if (_autoHideTimeout != value)
            {
                _autoHideTimeout = value;
                PropertyChanged?.Invoke(this, new(nameof(AutoHideTimeout)));
                _db.SetSetting("auto_hide_timeout", value.ToString());
            }
        }
    }

    public bool AutoHideOnLaunch
    {
        get => _autoHideOnLaunch;
        set
        {
            if (_autoHideOnLaunch != value)
            {
                _autoHideOnLaunch = value;
                PropertyChanged?.Invoke(this, new(nameof(AutoHideOnLaunch)));
                _db.SetSetting("auto_hide_on_launch", value.ToString());
            }
        }
    }

    public bool AutoHideOnMouseLeave
    {
        get => _autoHideOnMouseLeave;
        set
        {
            if (_autoHideOnMouseLeave != value)
            {
                _autoHideOnMouseLeave = value;
                PropertyChanged?.Invoke(this, new(nameof(AutoHideOnMouseLeave)));
                _db.SetSetting("auto_hide_on_mouse_leave", value.ToString());
            }
        }
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set
        {
            if (_startMinimized != value)
            {
                _startMinimized = value;
                PropertyChanged?.Invoke(this, new(nameof(StartMinimized)));
                _db.SetSetting("start_minimized", value.ToString());
            }
        }
    }

    public bool HideTrayIcon
    {
        get => _hideTrayIcon;
        set
        {
            if (_hideTrayIcon != value)
            {
                _hideTrayIcon = value;
                PropertyChanged?.Invoke(this, new(nameof(HideTrayIcon)));
                _db.SetSetting("hide_tray_icon", value.ToString());
            }
        }
    }

    /// <summary>Hide the built-in "全部" tab from the tab bar.</summary>
    public bool HideAllTab
    {
        get => _hideAllTab;
        set
        {
            if (_hideAllTab != value)
            {
                _hideAllTab = value;
                PropertyChanged?.Invoke(this, new(nameof(HideAllTab)));
                _db.SetSetting("hide_all_tab", value.ToString());
                LoadData();
            }
        }
    }

    /// <summary>When true, prevents the system from going to sleep / standby / hibernation.</summary>
    public bool PreventSleep
    {
        get => _preventSleep;
        set
        {
            if (_preventSleep != value)
            {
                _preventSleep = value;
                PropertyChanged?.Invoke(this, new(nameof(PreventSleep)));
                _db.SetSetting("prevent_sleep", value.ToString());
                if (value)
                    _preventSleepActive = SuperDucker.Shared.Native.PowerManager.PreventSleep(); // 全阻止：睡眠+熄屏+心跳续期
                else
                {
                    SuperDucker.Shared.Native.PowerManager.Restore();
                    _preventSleepActive = false;
                }
                PropertyChanged?.Invoke(this, new(nameof(PreventSleepActive)));
                UpdateSleepStatus();
            }
        }
    }

    /// <summary>阻止睡眠开关的当前状态文字（用于设置面板提示）。</summary>
    public string SleepStatus
    {
        get => _sleepStatus;
        private set
        {
            if (_sleepStatus != value)
            {
                _sleepStatus = value;
                PropertyChanged?.Invoke(this, new(nameof(SleepStatus)));
                PropertyChanged?.Invoke(this, new(nameof(SleepStatusBrush)));
            }
        }
    }
    private string _sleepStatus = "○ 未生效 / 已关闭";

    /// <summary>状态文字对应的画刷。直接产出 Brush 对象，免去 XAML 端 Converter。</summary>
    public System.Windows.Media.Brush SleepStatusBrush =>
        _preventSleepActive ? GreenBrush : GrayBrush;

    private static readonly System.Windows.Media.SolidColorBrush GreenBrush
        = new(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly System.Windows.Media.SolidColorBrush GrayBrush
        = new(System.Windows.Media.Color.FromRgb(0x90, 0x90, 0x90));

    private void UpdateSleepStatus()
    {
        if (!_preventSleep) SleepStatus = "○ 未生效 / 已关闭";
        else if (_preventSleepActive) SleepStatus = "● 已生效：系统睡眠已锁定";
        else SleepStatus = "× 调用失败：API 被系统拒绝";
    }

    /// <summary>
    /// 在窗口完全加载、UI 线程消息泵就绪后，重新应用持久化的"阻止睡眠"请求。
    /// 放在 LoadSettings 里太早（窗口尚未 Show），部分机型的 SetThreadExecutionState
    /// 需要活跃的消息泵才能稳定生效；此处作为二次保险。
    /// </summary>
    public void ApplyPersistedPowerState()
    {
        if (_preventSleep)
            _preventSleepActive = SuperDucker.Shared.Native.PowerManager.PreventSleep();
        PropertyChanged?.Invoke(this, new(nameof(PreventSleepActive)));
        UpdateSleepStatus(); // 内部会同时通知 SleepStatus 与 SleepStatusBrush
    }

    /// <summary>反映 PowerManager 最近一次保活请求的底层 API 是否成功（true=已生效）。</summary>
    public bool PreventSleepActive => _preventSleepActive;

    /// <summary>0=dark, 1=light, 2=follow system, 3=custom.</summary>
    public int ThemeMode
    {
        get => _themeMode;
        set
        {
            if (_themeMode != value)
            {
                _themeMode = value;
                PropertyChanged?.Invoke(this, new(nameof(ThemeMode)));
                _db.SetSetting("theme_mode", value.ToString());
                ThemeChanged?.Invoke();
            }
        }
    }

    // ─══ 主题预设（内建 + 自定义）══─
    public const string BuiltInDarkName = "Dark";
    public const string BuiltInLightName = "Light";

    private readonly ObservableCollection<ThemePreset> _themePresets = new();
    private ThemePreset? _selectedPreset;

    /// <summary>所有可选主题：内建深色/浅色 + 自定义（持久化在 settings.theme_list）。</summary>
    public ObservableCollection<ThemePreset> ThemePresets => _themePresets;

    /// <summary>当前选中的主题（在 ThemeMode==3 自定义模式下生效）。</summary>
    public ThemePreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (_selectedPreset != value)
            {
                _selectedPreset = value;
                PropertyChanged?.Invoke(this, new(nameof(SelectedPreset)));
                if (value != null)
                {
                    _db.SetSetting("selected_theme", value.Name);
                    if (_themeMode == 3)
                        ThemeChanged?.Invoke();
                }
            }
        }
    }

    /// <summary>内建深色主题的固定 6 色（与 ApplyDarkTheme 对齐）。</summary>
    public static ThemePreset BuiltInDark() => new(
        BuiltInDarkName,
        Color.FromRgb(0x12, 0x12, 0x18),
        Color.FromRgb(0x1C, 0x1C, 0x28),
        Color.FromRgb(0x24, 0x24, 0x34),
        Color.FromRgb(0x33, 0x33, 0x46),
        Color.FromRgb(0xF0, 0xF0, 0xF8),
        Color.FromRgb(0xAE, 0xB0, 0xC0),
        isBuiltIn: true);

    /// <summary>内建浅色主题的固定 6 色（与 ApplyLightTheme 对齐）。</summary>
    public static ThemePreset BuiltInLight() => new(
        BuiltInLightName,
        Color.FromRgb(0xF5, 0xF6, 0xFA),
        Color.FromRgb(0xE8, 0xEA, 0xF0),
        Color.FromRgb(0xFF, 0xFF, 0xFF),
        Color.FromRgb(0xE2, 0xE4, 0xEC),
        Color.FromRgb(0x20, 0x22, 0x2C),
        Color.FromRgb(0x60, 0x64, 0x74),
        isBuiltIn: true);

    /// <summary>初始化主题集合：内建两项 + 从 DB 反序列化的自定义项。</summary>
    private void InitializeThemePresets()
    {
        _themePresets.Clear();
        _themePresets.Add(BuiltInDark());
        _themePresets.Add(BuiltInLight());

        var raw = _db.GetSetting("theme_list");
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<ThemePreset.Dto>>(raw);
                if (list != null)
                    foreach (var d in list)
                        _themePresets.Add(ThemePreset.FromDto(d));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load theme_list failed: {ex.Message}");
            }
        }
    }

    /// <summary>把当前自定义主题集合（仅自定义项）序列化回 DB。</summary>
    private void PersistThemeList()
    {
        var custom = _themePresets.Where(p => !p.IsBuiltIn).Select(p => p.ToDto()).ToList();
        _db.SetSetting("theme_list", JsonSerializer.Serialize(custom));
    }

    /// <summary>新增/覆盖一个自定义主题（同名则替换）。会切到自定义模式并立即应用。</summary>
    public void SaveCustomTheme(ThemePreset preset)
    {
        var exist = _themePresets.FirstOrDefault(p => !p.IsBuiltIn && p.Name == preset.Name);
        if (exist != null)
            _themePresets.Remove(exist);
        _themePresets.Add(preset);
        PersistThemeList();
        SelectedPreset = preset;
        if (_themeMode != 3)
            ThemeMode = 3;
        else
            ThemeChanged?.Invoke();
    }

    /// <summary>删除一个自定义主题（内建不可删）。</summary>
    public void DeleteCustomTheme(ThemePreset preset)
    {
        if (preset.IsBuiltIn) return;
        _themePresets.Remove(preset);
        PersistThemeList();
        if (_selectedPreset == preset)
            SelectedPreset = _themePresets.First(p => p.IsBuiltIn);
    }

    public double BackgroundOpacity
    {
        get => _backgroundOpacity;
        set
        {
            if (Math.Abs(_backgroundOpacity - value) > 0.001)
            {
                _backgroundOpacity = value;
                PropertyChanged?.Invoke(this, new(nameof(BackgroundOpacity)));
                _db.SetSetting("bg_opacity", value.ToString("R"));
                ThemeChanged?.Invoke();
            }
        }
    }

    public double IconOpacity
    {
        get => _iconOpacity;
        set
        {
            if (Math.Abs(_iconOpacity - value) > 0.001)
            {
                _iconOpacity = value;
                PropertyChanged?.Invoke(this, new(nameof(IconOpacity)));
                _db.SetSetting("icon_opacity", value.ToString("R"));
                ThemeChanged?.Invoke();
            }
        }
    }

    /// <summary>Global hotkey string for show/hide window, e.g. "Ctrl+Shift+S".</summary>
    public string HotkeyToggle
    {
        get => _hotkeyToggle;
        set
        {
            if (_hotkeyToggle != value)
            {
                _hotkeyToggle = value;
                PropertyChanged?.Invoke(this, new(nameof(HotkeyToggle)));
                _db.SetSetting("hotkey_toggle", value);
                HotkeyChanged?.Invoke();
            }
        }
    }

    /// <summary>Global hotkey string for open settings, e.g. "Ctrl+Shift+G".</summary>
    public string HotkeySettings
    {
        get => _hotkeySettings;
        set
        {
            if (_hotkeySettings != value)
            {
                _hotkeySettings = value;
                PropertyChanged?.Invoke(this, new(nameof(HotkeySettings)));
                _db.SetSetting("hotkey_settings", value);
                HotkeyChanged?.Invoke();
            }
        }
    }

    /// <summary>Global hotkey string for open shop page, e.g. "Alt+F4".</summary>
    public string HotkeyShop
    {
        get => _hotkeyShop;
        set
        {
            if (_hotkeyShop != value)
            {
                _hotkeyShop = value;
                PropertyChanged?.Invoke(this, new(nameof(HotkeyShop)));
                _db.SetSetting("hotkey_shop", value);
                HotkeyChanged?.Invoke();
            }
        }
    }

    /// <summary>Global hotkey string for open pack dialog, e.g. "Alt+F5".</summary>
    public string HotkeyPack
    {
        get => _hotkeyPack;
        set
        {
            if (_hotkeyPack != value)
            {
                _hotkeyPack = value;
                PropertyChanged?.Invoke(this, new(nameof(HotkeyPack)));
                _db.SetSetting("hotkey_pack", value);
                HotkeyChanged?.Invoke();
            }
        }
    }

    public int TotalItems => Items.Count;
    public bool IsPathRegistered => ShortcutManager.IsLinkInPath();

    // ═══ Constructor ═══
    public MainViewModel()
    {
        _db = new DatabaseManager(DatabaseManager.GetDefaultDbPath());

        // Load settings
        var vm = _db.GetSetting("view_mode");
        if (vm != null && Enum.TryParse<ViewMode>(vm, out var mode)) _viewMode = mode;
        var th = _db.GetSetting("tab_horizontal");
        if (th != null) _isTabHorizontal = th == "True";
        var cm = _db.GetSetting("click_mode");
        if (cm != null) _isDoubleClick = cm == "double";

        // Window & behavior settings
        var wp = _db.GetSetting("window_position");
        if (wp != null && int.TryParse(wp, out var wpVal)) _windowPosition = wpVal;
        var ahe = _db.GetSetting("auto_hide_enabled");
        if (ahe != null) _autoHideEnabled = ahe == "True";
        var aht = _db.GetSetting("auto_hide_timeout");
        if (aht != null && int.TryParse(aht, out var ahtVal)) _autoHideTimeout = ahtVal;
        var ahl = _db.GetSetting("auto_hide_on_launch");
        if (ahl != null) _autoHideOnLaunch = ahl == "True";
        var ahm = _db.GetSetting("auto_hide_on_mouse_leave");
        if (ahm != null) _autoHideOnMouseLeave = ahm == "True";
        var sm = _db.GetSetting("start_minimized");
        if (sm != null) _startMinimized = sm == "True";
        var hti = _db.GetSetting("hide_tray_icon");
        if (hti != null) _hideTrayIcon = hti == "True";
        var hat = _db.GetSetting("hide_all_tab");
        if (hat != null) _hideAllTab = hat == "True";
        var psl = _db.GetSetting("prevent_sleep");
        if (psl != null) _preventSleep = psl == "True";

        // Theme settings
        var tm = _db.GetSetting("theme_mode");
        if (tm != null && int.TryParse(tm, out var tmVal)) _themeMode = tmVal;
        // 自定义主题：从 settings.theme_list 反序列化 + 选定项
        InitializeThemePresets();
        var sel = _db.GetSetting("selected_theme");
        if (sel != null)
            _selectedPreset = _themePresets.FirstOrDefault(p => p.Name == sel);
        if (_selectedPreset == null)
            _selectedPreset = _themeMode switch
            {
                1 => _themePresets.First(p => p.IsBuiltIn && p.Name == BuiltInLightName),
                _ => _themePresets.First(p => p.IsBuiltIn && p.Name == BuiltInDarkName)
            };
        var bgo = _db.GetSetting("bg_opacity");
        if (bgo != null && double.TryParse(bgo, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bgoVal)) _backgroundOpacity = bgoVal;
        var ico = _db.GetSetting("icon_opacity");
        if (ico != null && double.TryParse(ico, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var icoVal)) _iconOpacity = icoVal;

        // Hotkey settings
        var hkt = _db.GetSetting("hotkey_toggle");
        if (hkt != null) _hotkeyToggle = hkt;
        var hks = _db.GetSetting("hotkey_settings");
        if (hks != null) _hotkeySettings = hks;
        var hksh = _db.GetSetting("hotkey_shop");
        if (hksh != null) _hotkeyShop = hksh;
        var hkp = _db.GetSetting("hotkey_pack");
        if (hkp != null) _hotkeyPack = hkp;

        // Friendly-name display preference
        var dflt = _db.GetSetting("default_friendly");
        if (dflt != null) _defaultShowFriendly = dflt == "True";

        // Apply prevent-sleep request (already persisted) on startup
        if (_preventSleep)
            SuperDucker.Shared.Native.PowerManager.PreventSleep();

        LoadData();
    }

    // ═══ Data Loading ═══
    public void LoadData()
    {
        // Remember the currently selected tab so we can restore it after reload
        // (add/edit/delete/reorder operations shouldn't kick the user back to "全部").
        _selectedTabBeforeLoad = SelectedTab?.Id;

        _allApps.Clear();
        _allUrls.Clear();
        _itemCache.Clear();
        Tabs.Clear();

        _allApps.AddRange(_db.GetAllApps());
        _allUrls.AddRange(_db.GetAllUrls());

        // Build item cache
        foreach (var app in _allApps)
            _itemCache[AppKey(app.Id)] = new PanelItemViewModel(app);
        foreach (var url in _allUrls)
            _itemCache[UrlKey(url.Id)] = new PanelItemViewModel(url);

        // User tabs
        var userTabs = _db.GetAllTabs();

        // Built-in "全部" tab (hidden when requested, but always kept if no user tabs exist)
        if (!_hideAllTab || userTabs.Count == 0)
            Tabs.Add(new TabViewModel(-1, "全部", isBuiltIn: true));

        foreach (var tab in userTabs)
            Tabs.Add(new TabViewModel(tab.Id, tab.Name, isBuiltIn: false, tab.SortOrder));

        // Restore previously selected tab (by id) instead of always resetting to "全部".
        // This preserves the user's context after add/edit/delete/reorder operations.
        var previousTabId = _selectedTabBeforeLoad;
        SelectedTab = (previousTabId != null
            ? Tabs.FirstOrDefault(t => t.Id == previousTabId)
            : null) ?? Tabs.FirstOrDefault();

        NotifyCounts();
    }

    private void RefreshItems()
    {
        Items.Clear();

        bool isAll = SelectedTab?.Id == -1;

        // Add apps from cache
        var apps = isAll ? _allApps : _allApps.Where(a => a.TabId == SelectedTab?.Id).ToList();
        foreach (var app in apps)
        {
            if (_itemCache.TryGetValue(AppKey(app.Id), out var vm))
                Items.Add(vm);
        }

        // Add URLs from cache
        var urls = isAll ? _allUrls : _allUrls.Where(u => u.TabId == SelectedTab?.Id).ToList();
        foreach (var url in urls)
        {
            if (_itemCache.TryGetValue(UrlKey(url.Id), out var vm))
                Items.Add(vm);
        }

        ApplyFilter();
        ApplyFriendlyDisplay();
        NotifyCounts();
    }

    private void ApplyFilter()
    {
        var query = _searchText.Trim().ToUpperInvariant();
        foreach (var item in Items)
        {
            item.IsVisible = string.IsNullOrEmpty(query) ||
                item.Abbreviation.Contains(query) ||
                (item.FriendlyName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (item.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
        }
    }

    private void NotifyCounts()
    {
        PropertyChanged?.Invoke(this, new(nameof(TotalItems)));
        PropertyChanged?.Invoke(this, new(nameof(IsPathRegistered)));
    }

    // ═══ Launch ═══
    public void LaunchItem(PanelItemViewModel item) => LaunchItem(item, false);

    public void LaunchItem(PanelItemViewModel item, bool runAsAdmin)
    {
        if (item.IsUrl)
        {
            Process.Start(new ProcessStartInfo { FileName = item.Url!, UseShellExecute = true });
        }
        else
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = item.TargetPath!,
                    WorkingDirectory = item.WorkingDirectory ?? Path.GetDirectoryName(item.TargetPath!) ?? "",
                    UseShellExecute = true,
                    Verb = runAsAdmin ? "runas" : null
                });
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { } // user cancelled UAC
        }
    }

    /// <summary>Open the folder containing the target program in Explorer.</summary>
    public void OpenItemFolder(PanelItemViewModel item)
    {
        if (item.IsUrl || string.IsNullOrEmpty(item.TargetPath)) return;
        var dir = Path.GetDirectoryName(item.TargetPath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    // ═══ Add Entry ═══
    public bool AddAppEntry(string abbreviation, string targetPath, string? friendlyName, string? description, string? category)
    {
        abbreviation = abbreviation.ToUpperInvariant();
        var conflict = _db.FindAbbreviationConflict(abbreviation);
        if (conflict != null)
        {
            MessageBox.Show($"缩写 '{abbreviation}' 已被占用：{conflict}\n\n请选择其他缩写，或先删除已有项目。",
                "缩写冲突", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var entry = new AppEntry
        {
            Abbreviation = abbreviation,
            FriendlyName = string.IsNullOrWhiteSpace(friendlyName) ? null : friendlyName,
            TargetPath = Path.GetFullPath(targetPath),
            Description = description,
            Category = category,
            IsBuiltIn = false,
            TabId = (SelectedTab?.Id > 0) ? SelectedTab.Id : null
        };

        _db.AddApp(entry);
        ShortcutManager.CreateShortcut(entry);
        LoadData();
        return true;
    }

    public bool AddUrlEntry(string abbreviation, string url, string? friendlyName, string? description, string? category, string? iconPath = null)
    {
        abbreviation = abbreviation.ToUpperInvariant();
        var conflict = _db.FindAbbreviationConflict(abbreviation);
        if (conflict != null)
        {
            MessageBox.Show($"缩写 '{abbreviation}' 已被占用：{conflict}\n\n请选择其他缩写，或先删除已有项目。",
                "缩写冲突", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var entry = new UrlEntry
        {
            Abbreviation = abbreviation,
            FriendlyName = string.IsNullOrWhiteSpace(friendlyName) ? null : friendlyName,
            Url = url,
            Description = description,
            Category = category,
            IconPath = iconPath,
            TabId = (SelectedTab?.Id > 0) ? SelectedTab.Id : null
        };

        _db.AddUrl(entry);
        ShortcutManager.CreateUrlShortcut(entry);
        LoadData();
        return true;
    }

    public void UpdateAppEntry(string originalAbbr, string targetPath, string? friendlyName, string? description, string? category, string? iconPath = null)
    {
        var entry = _db.GetAppByAbbreviation(originalAbbr);
        if (entry == null) return;

        entry.TargetPath = Path.GetFullPath(targetPath);
        entry.FriendlyName = string.IsNullOrWhiteSpace(friendlyName) ? null : friendlyName;
        entry.Description = string.IsNullOrWhiteSpace(description) ? null : description;
        entry.Category = string.IsNullOrWhiteSpace(category) ? null : category;
        if (iconPath != null)
            entry.IconPath = iconPath;

        _db.UpdateApp(entry);
        ShortcutManager.CreateShortcut(entry); // Re-create .lnk in case path changed
        LoadData();
    }

    public void UpdateUrlEntry(string originalAbbr, string url, string? friendlyName, string? description, string? category, string? iconPath)
    {
        var entry = _db.GetUrlByAbbreviation(originalAbbr);
        if (entry == null) return;

        entry.Url = url;
        entry.FriendlyName = string.IsNullOrWhiteSpace(friendlyName) ? null : friendlyName;
        entry.Description = string.IsNullOrWhiteSpace(description) ? null : description;
        entry.Category = string.IsNullOrWhiteSpace(category) ? null : category;
        if (iconPath != null)
            entry.IconPath = iconPath;

        _db.UpdateUrl(entry);
        ShortcutManager.CreateUrlShortcut(entry); // Re-create .url in case URL/icon changed
        LoadData();
    }

    public void RemoveItem(PanelItemViewModel item)
    {
        if (item.IsUrl)
        {
            _db.DeleteUrl(item.Id);
            ShortcutManager.DeleteUrlShortcut(item.Abbreviation);
        }
        else
        {
            _db.DeleteApp(item.Id);
            ShortcutManager.DeleteShortcut(item.Abbreviation);
        }
        LoadData();
    }

    // ═══ Tab Management ═══
    public void AddTab(string name)
    {
        var tab = _db.AddTab(new TabEntry { Name = name, SortOrder = Tabs.Count });
        Tabs.Add(new TabViewModel(tab.Id, tab.Name, isBuiltIn: false));
    }

    public void RenameTab(TabViewModel tab, string newName)
    {
        _db.UpdateTab(new TabEntry { Id = tab.Id, Name = newName, SortOrder = tab.SortOrder });
        tab.Name = newName;
    }

    public void DeleteTab(TabViewModel tab)
    {
        if (tab.IsBuiltIn) return;
        _db.DeleteTab(tab.Id);
        Tabs.Remove(tab);
        if (SelectedTab == tab || SelectedTab == null)
            SelectedTab = Tabs.FirstOrDefault();
        else
            RefreshItems();
    }

    public void ToggleTabOrientation()
    {
        IsTabHorizontal = !IsTabHorizontal;
    }

    /// <summary>
    /// Moves the given item to a different tab.
    /// </summary>
    public void MoveItemToTab(PanelItemViewModel item, int? tabId)
    {
        var table = item.IsUrl ? "url_entries" : "app_entries";
        _db.SetEntryTab(table, item.Id, tabId);
        LoadData();
    }

    /// <summary>
    /// Reorders items within the current tab by moving <paramref name="movedItem"/>
    /// before <paramref name="targetItemBefore"/> (or to the end if null).
    /// </summary>
    public void ReorderItems(PanelItemViewModel movedItem, PanelItemViewModel? targetItemBefore)
    {
        var currentItems = Items.ToList();
        currentItems.Remove(movedItem);

        if (targetItemBefore != null)
        {
            var idx = currentItems.IndexOf(targetItemBefore);
            if (idx >= 0)
                currentItems.Insert(idx, movedItem);
            else
                currentItems.Add(movedItem);
        }
        else
        {
            currentItems.Add(movedItem);
        }

        for (int i = 0; i < currentItems.Count; i++)
        {
            var item = currentItems[i];
            var table = item.IsUrl ? "url_entries" : "app_entries";
            _db.SetEntrySortOrder(table, item.Id, i);
        }

        LoadData();
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}

// ═══════════════════════════════════════════
//  Tab ViewModel
// ═══════════════════════════════════════════
public class TabViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public int Id { get; }
    public bool IsBuiltIn { get; }

    private int _sortOrder;
    public int SortOrder
    {
        get => _sortOrder;
        set { _sortOrder = value; PropertyChanged?.Invoke(this, new(nameof(SortOrder))); }
    }

    private string _name;
    private bool _isSelected;

    public string Name
    {
        get => _name;
        set { _name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
    }

    public TabViewModel(int id, string name, bool isBuiltIn, int sortOrder = 0)
    {
        Id = id;
        _name = name;
        IsBuiltIn = isBuiltIn;
        _sortOrder = isBuiltIn ? -1 : sortOrder;
    }
}

// ═══════════════════════════════════════════
//  Panel Item ViewModel (mostly unchanged)
// ═══════════════════════════════════════════
public class PanelItemViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public int Id { get; }
    public string Abbreviation { get; }
    public string? FriendlyName { get; }
    public string? Description { get; }
    public string? Category { get; }
    public string? TargetPath { get; }
    public string? WorkingDirectory { get; }
    public string? Url { get; }
    public bool IsBuiltIn { get; }
    public bool IsUrl { get; }

    private string _displayName;
    private string _tooltipText;
    private bool _isVisible = true;

    public string DisplayName
    {
        get => _displayName;
        private set
        {
            if (_displayName != value)
            {
                _displayName = value;
                PropertyChanged?.Invoke(this, new(nameof(DisplayName)));
                UpdateTooltip();
            }
        }
    }

    public string TooltipText
    {
        get => _tooltipText;
        private set
        {
            if (_tooltipText != value)
            {
                _tooltipText = value;
                PropertyChanged?.Invoke(this, new(nameof(TooltipText)));
            }
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                PropertyChanged?.Invoke(this, new(nameof(IsVisible)));
                PropertyChanged?.Invoke(this, new(nameof(Visibility)));
            }
        }
    }

    public Visibility Visibility => _isVisible ? Visibility.Visible : Visibility.Collapsed;

    public ImageSource? Icon { get; }

    public Brush StatusBrush => IsUrl
        ? new SolidColorBrush(Color.FromRgb(0x6C, 0x8F, 0xFF))
        : IsBuiltIn
            ? new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80))
            : new SolidColorBrush(Color.FromRgb(0xFF, 0xB8, 0x6C));

    public string TypeLabel => IsUrl ? "URL" : (IsBuiltIn ? "内置" : "外部");

    public Visibility CategoryVisibility =>
        string.IsNullOrEmpty(Category) ? Visibility.Collapsed : Visibility.Visible;

    public PanelItemViewModel(AppEntry app)
    {
        Id = app.Id;
        Abbreviation = app.Abbreviation;
        FriendlyName = app.FriendlyName;
        Description = app.Description;
        Category = app.Category;
        TargetPath = app.TargetPath;
        WorkingDirectory = app.WorkingDirectory;
        IsBuiltIn = app.IsBuiltIn;
        IsUrl = false;
        _displayName = app.Abbreviation;
        _tooltipText = "";
        Icon = ExtractIcon(app.TargetPath, app.IconPath);
        UpdateTooltip();
    }

    public PanelItemViewModel(UrlEntry url)
    {
        Id = url.Id;
        Abbreviation = url.Abbreviation;
        FriendlyName = url.FriendlyName;
        Description = url.Description;
        Category = url.Category;
        Url = url.Url;
        IsBuiltIn = false;
        IsUrl = true;
        _displayName = url.Abbreviation;
        _tooltipText = "";
        Icon = LoadUrlIcon(url.IconPath);
        UpdateTooltip();
    }

    public void UpdateDisplayName(bool showFriendly)
    {
        DisplayName = showFriendly ? (FriendlyName ?? Abbreviation) : Abbreviation;
    }

    private void UpdateTooltip()
    {
        var parts = new List<string> { DisplayName };
        if (FriendlyName != null && DisplayName != FriendlyName) parts.Add(FriendlyName);
        if (Category != null) parts.Add($"分类: {Category}");
        if (Description != null) parts.Add(Description);
        if (!IsUrl && TargetPath != null) parts.Add(TargetPath);
        if (IsUrl && Url != null) parts.Add(Url);
        parts.Add($"[{TypeLabel}]");
        TooltipText = string.Join("\n", parts);
    }

    private static ImageSource LoadUrlIcon(string? iconPath)
    {
        if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
        {
            // Check cache first
            if (_iconCache.TryGetValue(iconPath, out var cached))
            {
                cached.LastAccessed = DateTime.UtcNow;
                return cached.Source;
            }

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 48;
                using var stream = new FileStream(iconPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                bmp.StreamSource = stream;
                bmp.EndInit();
                bmp.Freeze();
                var entry = new CachedIconEntry { Source = bmp, LastAccessed = DateTime.UtcNow };
                lock (_iconCacheLock)
                {
                    EvictIconCacheIfNeeded();
                    _iconCache[iconPath] = entry;
                }
                return bmp;
            }
            catch { }
        }
        return CreateDefaultUrlIcon();
    }

    // ═══ Icon Caches with LRU eviction and proper resource management ═══
    
    private class CachedIconEntry
    {
        public ImageSource Source { get; set; } = null!;
        public DateTime LastAccessed { get; set; }
    }
    
    private static readonly Dictionary<string, CachedIconEntry> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _iconCacheLock = new();
    private const int MaxCachedIcons = 100;
    
    private static ImageSource? _defaultAppIcon;
    private static ImageSource? _defaultUrlIcon;

    private static ImageSource? ExtractIcon(string targetPath, string? customIconPath)
    {
        var iconFile = !string.IsNullOrEmpty(customIconPath) && File.Exists(customIconPath)
            ? customIconPath : targetPath;
        
        // Validate icon file exists and is accessible
        if (!File.Exists(iconFile))
        {
            return CreateDefaultAppIcon();
        }
        
        // Special handling for icons stored in the shared icons directory
        // Check if this is a standalone image file (png/jpg/bmp) in the icons folder
        try
        {
            var ext = Path.GetExtension(iconFile).ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp")
            {
                // Load directly without trying SHDefExtractIcon
                using var stream = new FileStream(iconFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 64;
                bmp.StreamSource = stream;
                bmp.EndInit();
                bmp.Freeze();
                
                lock (_iconCacheLock)
                {
                    EvictIconCacheIfNeeded();
                    _iconCache[iconFile] = new CachedIconEntry { Source = bmp, LastAccessed = DateTime.UtcNow };
                }
                return bmp;
            }
        }
        catch (Exception ex)
        {
            // If loading image fails, fall back to default
            System.Diagnostics.Debug.WriteLine($"Failed to load icon from {iconFile}: {ex.Message}");
            return CreateDefaultAppIcon();
        }
        
        lock (_iconCacheLock)
        {
            // Check cache first
            if (_iconCache.TryGetValue(iconFile, out var cached))
            {
                cached.LastAccessed = DateTime.UtcNow;
                return cached.Source;
            }

            // LRU: evict oldest entries if at capacity
            EvictIconCacheIfNeeded();

            try
            {
                // Use SHDefExtractIcon for DPI-aware icon extraction at the right size
                var size = DpiHelper.GetIconExtractSize();
                var hIcon = ExtractIconFromFile(iconFile, size);
                
                if (hIcon != IntPtr.Zero)
                {
                    using var winning32Icon = System.Drawing.Icon.FromHandle(hIcon);
                    using var bmp = winning32Icon.ToBitmap();
                    using var ms = new MemoryStream();
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Seek(0, SeekOrigin.Begin);
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.StreamSource = ms;
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.EndInit();
                    img.Freeze();
                    DestroyIcon(hIcon);
                    _iconCache[iconFile] = new CachedIconEntry { Source = img, LastAccessed = DateTime.UtcNow };
                    return img;
                }
                
                // Fallback: Try creating a temporary ICO file first
                var tempIconPath = TryCreateTempIconFile(iconFile);
                if (tempIconPath != null)
                {
                    try
                    {
                        // Extract icon from temp ICO file
                        var extractedIcon = System.Drawing.Icon.ExtractAssociatedIcon(tempIconPath);
                        if (extractedIcon != null)
                        {
                            using var bmp = extractedIcon.ToBitmap();
                            using var ms = new MemoryStream();
                            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            ms.Seek(0, SeekOrigin.Begin);
                            var img = new BitmapImage();
                            img.BeginInit();
                            img.StreamSource = ms;
                            img.CacheOption = BitmapCacheOption.OnLoad;
                            img.EndInit();
                            img.Freeze();
                            
                            File.Delete(tempIconPath); // Clean up temp file
                            _iconCache[iconFile] = new CachedIconEntry { Source = img, LastAccessed = DateTime.UtcNow };
                            return img;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Temp ICO fallback failed for {iconFile}: {ex.Message}");
                    }
                }

                // Final fallback: direct extraction with error handling
                using var finalFallbackIcon = System.Drawing.Icon.ExtractAssociatedIcon(iconFile);
                if (finalFallbackIcon != null)
                {
                    using var bmp = finalFallbackIcon.ToBitmap();
                    using var ms = new MemoryStream();
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Seek(0, SeekOrigin.Begin);
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.StreamSource = ms;
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.EndInit();
                    img.Freeze();
                    _iconCache[iconFile] = new CachedIconEntry { Source = img, LastAccessed = DateTime.UtcNow };
                    return img;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to extract icon from {iconFile}: {ex.Message}");
            }
            
            return CreateDefaultAppIcon();
        }
    }

    private static void EvictIconCacheIfNeeded()
    {
        while (_iconCache.Count >= MaxCachedIcons)
        {
            var oldestEntry = _iconCache.Values.OrderBy(e => e.LastAccessed).First();
            var oldestKey = _iconCache.FirstOrDefault(kvp => kvp.Value == oldestEntry).Key;
            if (!string.IsNullOrEmpty(oldestKey))
                _iconCache.Remove(oldestKey);
        }
    }

    private static ImageSource CreateDefaultAppIcon()
    {
        return _defaultAppIcon ??= RenderDefaultIcon("App", new Point(8, 14));
    }

    private static ImageSource CreateDefaultUrlIcon()
    {
        return _defaultUrlIcon ??= RenderDefaultIcon("URL", new Point(6, 14));
    }

    private static ImageSource RenderDefaultIcon(string text, Point textPos)
    {
        var dpi = DpiHelper.GetDpi();
        var scale = dpi / 96.0;
        const double dipSize = 48;
        var pixelSize = (int)Math.Ceiling(dipSize * scale);

        var dv = new DrawingVisual();
        using (var ctx = dv.RenderOpen())
        {
            ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x6C, 0x8F, 0xFF)), null, new Rect(0, 0, dipSize, dipSize));
            ctx.DrawText(new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, Brushes.White, dpi), textPos);
        }
        var rtb = new RenderTargetBitmap(pixelSize, pixelSize, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    // ═══ Win32 Icon Extraction ═══

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern int SHDefExtractIcon(string pszIconFile, int iIndex, uint uFlags,
        out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIconSize);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static IntPtr ExtractIconFromFile(string filePath, int size)
    {
        var result = SHDefExtractIcon(filePath, 0, 0,
            out var hLarge, out var hSmall,
            (uint)((size << 16) | size));
        
        if (result == 0 && hLarge != IntPtr.Zero)
        {
            // Success - destroy small icon if valid but not zero
            if (hSmall != IntPtr.Zero && hSmall != hLarge)
                DestroyIcon(hSmall);
            return hLarge;
        }
        
        // If SHDefExtractIcon failed, try fallback method
        try
        {
            using var fallbackIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath);
            if (fallbackIcon != null)
            {
                // Convert to handle manually
                return fallbackIcon.ToBitmap().GetHicon();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Fallback icon extraction from {filePath} failed: {ex.Message}");
        }
        
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Creates a temporary ICO file from an executable's icon and returns the path.
    /// This is more reliable than direct extraction for some cases.
    /// </summary>
    public static string? TryCreateTempIconFile(string exePath)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon == null) return null;
            
            var tempPath = Path.Combine(Path.GetTempPath(), $"sd_icon_{Guid.NewGuid():N}.ico");
            using var fs = new FileStream(tempPath, FileMode.Create);
            icon.Save(fs);
            return tempPath;
        }
        catch
        {
            return null;
        }
    }
}
