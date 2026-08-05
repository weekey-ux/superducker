using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SuperDucker.App;

/// <summary>
/// 主窗口：承载命令栏、应用面板、设置面板、商店面板，并协调系统托盘、
/// 全局快捷键、主题切换与自动隐藏等行为。
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel VM { get; }
    private SettingsDialog? _settingsPanel;
    private ShopPanel? _shopPanel;
    private SystemTrayManager? _trayManager;
    private bool _isExiting;

    // 自动隐藏相关
    private DispatcherTimer? _autoHideTimer;
    private bool _isMouseOverWindow = true;

    /// <summary>
    /// 当本窗口拥有的对话框（如添加对话框、关闭选择对话框）当前处于打开状态时返回 true。
    /// 用于在用户编辑或确认期间阻止主窗口自动隐藏。
    /// </summary>
    private bool IsOwnedDialogOpen => OwnedWindows.OfType<Window>().Any(w => w.IsVisible);

    // 全局快捷键相关
    private GlobalHotkeyManager? _hotkeyManager;

    public MainWindow()
    {
        VM = new MainViewModel();
        DataContext = VM;
        InitializeComponent();

        _trayManager = new SystemTrayManager(this);
        _trayManager.IsVisible = !VM.HideTrayIcon;
        _trayManager.OpenSettingsRequested += OnOpenSettings;
        _trayManager.OpenShopRequested += OnOpenShop;

        VM.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentViewMode) ||
                e.PropertyName == nameof(MainViewModel.IsTabHorizontal))
                RebuildContent();
            if (e.PropertyName == nameof(MainViewModel.HideTrayIcon))
            {
                if (_trayManager != null)
                    _trayManager.IsVisible = !VM.HideTrayIcon;
            }
        };

        // Rebuild after items are refreshed (tab switch) — avoids race where
        // PropertyChanged fires before RefreshItems() populates the collection
        VM.RebuildNeeded += RebuildContent;

        // Theme changes
        VM.ThemeChanged += ApplyTheme;

        // Hotkeys
        _hotkeyManager = new GlobalHotkeyManager(this);
        _hotkeyManager.ToggleWindowRequested += OnToggleWindow;
        _hotkeyManager.OpenSettingsRequested += OnOpenSettings;
        _hotkeyManager.OpenShopRequested += OnOpenShop;
        _hotkeyManager.OpenPackRequested += OnOpenPack;
        VM.HotkeyChanged += RegisterHotkeys;
        RegisterHotkeys();

        // 在标题栏显示当前程序版本号（取自程序集版本，格式 v主.次.修订）
        if (VersionText != null)
            VersionText.Text = $"v{SuperDucker.Shared.VersionHelper.GetVersion()}";
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyWindowPosition();
        RebuildContent();
        ApplyTheme();

        // Wire up IsVisibleChanged after window is fully loaded
        IsVisibleChanged += Window_IsVisibleChanged;

        // 应用持久化的"阻止系统睡眠"请求（UI 线程已就绪，作为二次保险）
        VM.ApplyPersistedPowerState();

        // Cold start minimized: hide to tray immediately after window is fully loaded
        if (VM.StartMinimized)
        {
            Hide();
            return;
        }

        // Start auto-hide timer if enabled
        if (VM.AutoHideEnabled)
            StartAutoHideTimer();
    }

    private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            // Window just became visible — restart auto-hide timer if enabled
            if (VM.AutoHideEnabled)
            {
                _isMouseOverWindow = true;
                ResetAutoHideTimer();
            }
        }
        else
        {
            // Window just became hidden — stop auto-hide timer
            StopAutoHideTimer();
        }
    }

    private bool _isClosing = false;

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isClosing || _isExiting)
        {
            // Already closing or exiting - allow normal exit
            _trayManager?.Dispose();
            return;
        }

        _isClosing = true;
        e.Cancel = true;

        try
        {
            // Save position if using last-position mode
            if (VM.WindowPosition == 2)
                SaveWindowPosition();

            // Alt+F4 or system close — ask the user
            CloseButton_Click(sender!, new RoutedEventArgs());
        }
        finally
        {
            _isClosing = false;
        }
    }

    // ═══════════════════════════════════════════
    //  Theme Management
    // ═══════════════════════════════════════════

    private void ApplyTheme()
    {
        switch (VM.ThemeMode)
        {
            case 0: ApplyDarkTheme(); break;
            case 1: ApplyLightTheme(); break;
            case 2: ApplySystemTheme(); break;
            case 3: ApplyCustomTheme(); break;
        }

        // Apply opacity
        Opacity = VM.BackgroundOpacity;
    }

    private void ApplyDarkTheme()
    {
        ApplyPreset(MainViewModel.BuiltInDark());
    }

    private void ApplyLightTheme()
    {
        ApplyPreset(MainViewModel.BuiltInLight());
    }

    /// <summary>自定义模式：应用当前选中的主题预设；若为空则回退内建深色。</summary>
    private void ApplyCustomTheme()
    {
        var preset = VM.SelectedPreset;
        if (preset == null)
            ApplyDarkTheme();
        else
            ApplyPreset(preset);
    }

    /// <summary>把一个主题预设的 6 色写入资源字典（供所有 DynamicResource 控件即时套用）。</summary>
    private void ApplyPreset(ThemePreset preset)
    {
        SetThemeColor("BgDark", preset.BgDark);
        SetThemeColor("BgMedium", preset.BgMedium);
        SetThemeColor("BgCard", preset.BgCard);
        SetThemeColor("BgCardHover", preset.BgCardHover);
        SetThemeColor("TextPrimary", preset.TextPrimary);
        SetThemeColor("TextSecondary", preset.TextSecondary);
    }

    private void ApplySystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var useLight = key?.GetValue("AppsUseLightTheme");
            if (useLight is int val && val == 0)
                ApplyDarkTheme();
            else
                ApplyLightTheme();
        }
        catch
        {
            ApplyDarkTheme();
        }
    }

    private static void SetThemeColor(string keyName, Color color)
    {
        var resources = Application.Current.Resources;
        if (resources.Contains(keyName))
            resources[keyName] = color;
        var brushKey = keyName + "Brush";
        if (resources.Contains(brushKey))
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[brushKey] = brush;
        }
    }

    // ═══════════════════════════════════════════
    //  Window Positioning
    // ═══════════════════════════════════════════

    private void ApplyWindowPosition()
    {
        switch (VM.WindowPosition)
        {
            case 0: // Screen center
                CenterOnScreen();
                break;
            case 1: // Follow mouse
                PositionAtMouse();
                break;
            case 2: // Last position
                if (!RestoreWindowPosition())
                    CenterOnScreen();
                break;
        }
    }

    private void CenterOnScreen()
    {
        var screen = SystemParameters.WorkArea;
        Left = (screen.Width - ActualWidth) / 2 + screen.Left;
        Top = (screen.Height - ActualHeight) / 2 + screen.Top;
    }

    private void PositionAtMouse()
    {
        var pos = GetMousePosition();
        var screen = SystemParameters.WorkArea;

        Left = pos.X;
        Top = pos.Y;

        // Clamp to screen bounds
        if (Left + ActualWidth > screen.Right)
            Left = screen.Right - ActualWidth;
        if (Top + ActualHeight > screen.Bottom)
            Top = screen.Bottom - ActualHeight;
        if (Left < screen.Left) Left = screen.Left;
        if (Top < screen.Top) Top = screen.Top;
    }

    private static Point GetMousePosition()
    {
        var point = System.Windows.Forms.Cursor.Position;
        return new Point(point.X, point.Y);
    }

    private void SaveWindowPosition()
    {
        using var db = new SuperDucker.Shared.Data.DatabaseManager(SuperDucker.Shared.Data.DatabaseManager.GetDefaultDbPath());
        var posStr = FormattableString.Invariant($"{Left},{Top},{ActualWidth},{ActualHeight}");
        db.SetSetting("last_window_pos", posStr);
    }

    private bool RestoreWindowPosition()
    {
        using var db = new SuperDucker.Shared.Data.DatabaseManager(SuperDucker.Shared.Data.DatabaseManager.GetDefaultDbPath());
        var posStr = db.GetSetting("last_window_pos");
        if (string.IsNullOrEmpty(posStr)) return false;

        var parts = posStr.Split(',');
        if (parts.Length != 4) return false;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var w)) return false;
        if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var h)) return false;

        // Validate against any screen
        bool onScreen = false;
        foreach (System.Windows.Forms.Screen s in System.Windows.Forms.Screen.AllScreens)
        {
            var area = s.WorkingArea;
            if (x + w > area.Left && x < area.Right && y + h > area.Top && y < area.Bottom)
            {
                onScreen = true;
                break;
            }
        }
        if (!onScreen) return false;

        Left = x;
        Top = y;
        if (w >= MinWidth) Width = w;
        if (h >= MinHeight) Height = h;
        return true;
    }

    // ═══════════════════════════════════════════
    //  Auto-Hide
    // ═══════════════════════════════════════════

    private void StartAutoHideTimer()
    {
        StopAutoHideTimer();
        _autoHideTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromSeconds(VM.AutoHideTimeout)
        };
        _autoHideTimer.Tick += AutoHideTimer_Tick;
        _autoHideTimer.Start();
    }

    private void StopAutoHideTimer()
    {
        _autoHideTimer?.Stop();
        _autoHideTimer = null;
    }

    private void ResetAutoHideTimer()
    {
        if (!VM.AutoHideEnabled || !IsVisible) return;
        if (_autoHideTimer == null)
        {
            StartAutoHideTimer();
        }
        else
        {
            _autoHideTimer.Stop();
            _autoHideTimer.Interval = TimeSpan.FromSeconds(VM.AutoHideTimeout);
            _autoHideTimer.Start();
        }
    }

    private void AutoHideTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsVisible)
        {
            StopAutoHideTimer();
            return;
        }
        if (!_isMouseOverWindow)
        {
            HideToTray();
        }
        else
        {
            // Mouse is still over the window, reset timer
            _autoHideTimer?.Stop();
            _autoHideTimer?.Start();
        }
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        _isMouseOverWindow = true;
        ResetAutoHideTimer();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        _isMouseOverWindow = false;
        if (VM.AutoHideOnMouseLeave && IsVisible)
        {
            // Use a short one-shot timer to verify mouse really left
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (!_isMouseOverWindow && IsVisible)
                    HideToTray();
            };
            t.Start();
        }
        else
        {
            ResetAutoHideTimer();
        }
    }

    private void HideToTray()
    {
        if (!IsVisible) return;

        // Don't hide if settings panel is open — user is actively configuring
        if (SettingsPanel.Visibility == Visibility.Visible) return;

        // Don't hide if shop panel is open — user is browsing packages
        if (ShopPanel.Visibility == Visibility.Visible) return;

        // Don't hide if a ContextMenu is open — right-click popup causes MouseLeave
        if (Mouse.Captured != null) return;

        // Don't hide if a modal/owned dialog is open (e.g. edit dialog)
        if (IsOwnedDialogOpen) return;

        // Don't hide while the user is dragging an item — keep the drop target visible
        if (_isItemDragging) return;

        StopAutoHideTimer();
        Hide();
    }

    private void CheckAutoHideAfterLaunch()
    {
        if (VM.AutoHideOnLaunch && IsVisible)
        {
            HideToTray();
        }
    }

    // ═══════════════════════════════════════════
    //  Dynamic Content Builder
    // ═══════════════════════════════════════════

    private void RebuildContent()
    {
        var content = VM.IsGridView ? BuildGridView() : BuildListView();

        HContentArea.Children.Clear();
        VContentArea.Children.Clear();

        if (VM.IsTabHorizontal)
            HContentArea.Children.Add(content);
        else
            VContentArea.Children.Add(content);
    }

    private UIElement BuildGridView()
    {
        var container = new DockPanel();
        container.ContextMenu = BuildContentContextMenu();

        // Grid items
        var items = new ItemsControl
        {
            ItemsSource = VM.Items,
            AllowDrop = true,
            Background = Brushes.Transparent
        };
        items.ItemsPanel = (ItemsPanelTemplate)System.Windows.Markup.XamlReader.Parse(
            "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><WrapPanel Orientation='Horizontal'/></ItemsPanelTemplate>");
        items.ItemTemplate = (DataTemplate)FindResource("GridItemTemplate");
        items.DragOver += Content_DragOver;
        items.Drop += Content_Drop;

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(10, 8, 10, 10),
            Content = items
        };
        container.Children.Add(scroll);

        // Empty state
        if (VM.TotalItems == 0)
        {
            scroll.Content = BuildEmptyState();
        }

        return container;
    }

    private UIElement BuildListView()
    {
        var container = new DockPanel();
        container.ContextMenu = BuildContentContextMenu();

        // Search bar
        var searchBox = new TextBox
        {
            Style = (Style)FindResource("SearchBoxStyle"),
            Margin = new Thickness(12, 8, 12, 0)
        };
        searchBox.SetBinding(TextBox.TextProperty,
            new Binding("SearchText") { Source = VM, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });

        // Placeholder
        var searchPanel = new Grid { Margin = new Thickness(12, 8, 12, 0) };
        searchPanel.Children.Add(searchBox);
        var placeholder = new TextBlock
        {
            Text = "搜索缩写、名称或描述...",
            Foreground = (SolidColorBrush)FindResource("TextSecondaryBrush"),
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            FontSize = 13,
            Opacity = 0.6
        };
        searchPanel.Children.Add(placeholder);
        DockPanel.SetDock(searchPanel, Dock.Top);
        container.Children.Add(searchPanel);

        searchBox.TextChanged += (s, e) =>
            placeholder.Visibility = string.IsNullOrEmpty(searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        // List items
        var listView = new ListView
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ItemsSource = VM.Items,
            Padding = new Thickness(8, 4, 8, 8),
            AllowDrop = true
        };
        listView.DragOver += Content_DragOver;
        listView.Drop += Content_Drop;
        listView.ItemContainerStyle = new Style(typeof(ListViewItem));
        listView.ItemContainerStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, new Binding("Visibility")));
        listView.ItemContainerStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        listView.ItemContainerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        listView.ItemContainerStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1)));
        listView.ItemContainerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        listView.ItemTemplate = BuildListItemTemplate();
        listView.MouseRightButtonUp += ListItem_RightClick;
        listView.MouseDoubleClick += ListItem_DoubleClick;
        listView.PreviewMouseLeftButtonUp += ListItem_SingleClick;

        container.Children.Add(listView);
        return container;
    }

    private ContextMenu BuildContentContextMenu()
    {
        var cm = new ContextMenu();

        var addApp = new MenuItem { Header = "添加程序..." };
        addApp.Click += AddApp_Click;
        cm.Items.Add(addApp);

        var addUrl = new MenuItem { Header = "添加网址..." };
        addUrl.Click += AddUrl_Click;
        cm.Items.Add(addUrl);

        cm.Items.Add(new Separator());

        var viewText = VM.IsGridView ? "切换到列表模式" : "切换到网格模式";
        var viewToggle = new MenuItem { Header = viewText };
        viewToggle.Click += ToggleView_Click;
        cm.Items.Add(viewToggle);

        cm.Items.Add(new Separator());

        var newTab = new MenuItem { Header = "新建 Tab..." };
        newTab.Click += NewTab_Click;
        cm.Items.Add(newTab);

        cm.Items.Add(new Separator());

        var settings = new MenuItem { Header = "设置" };
        settings.Click += Settings_Click;
        cm.Items.Add(settings);

        return cm;
    }

    private DataTemplate BuildListItemTemplate()
    {
        // Use the predefined template from resources that supports DynamicResource theme updates
        return (DataTemplate)FindResource("ListItemTemplate");
    }

    private UIElement BuildEmptyState()
    {
        var border = new Border
        {
            Background = (SolidColorBrush)FindResource("BgMediumBrush"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20, 40, 20, 40),
            Margin = new Thickness(12, 20, 12, 20)
        };

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = "还没有注册任何程序或网址",
            Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush"),
            FontSize = 15, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        var hint = new TextBlock
        {
            Foreground = (SolidColorBrush)FindResource("TextSecondaryBrush"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center
        };
        hint.Inlines.Add(new Run("点击右上角 "));
        hint.Inlines.Add(new Run("+ 添加程序") { Foreground = (SolidColorBrush)FindResource("AccentBlueBrush"), FontWeight = FontWeights.SemiBold });
        hint.Inlines.Add(new Run(" 或在命令行使用 "));
        hint.Inlines.Add(new Run("sd add") { Foreground = (SolidColorBrush)FindResource("AccentGreenBrush"), FontWeight = FontWeights.SemiBold });
        stack.Children.Add(hint);

        border.Child = stack;
        return border;
    }

    // ═══════════════════════════════════════════
    //  Tab Handlers
    // ═══════════════════════════════════════════

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TabViewModel tab)
            VM.SelectedTab = tab;
    }

    private void SaveTabOrder()
    {
        try
        {
            for (int i = 0; i < VM.Tabs.Count; i++)
            {
                var tab = VM.Tabs[i];
                if (tab.IsBuiltIn) continue;
                tab.SortOrder = i;
                VM.RenameTab(tab, tab.Name); // Update database
            }
        }
        catch { }
    }

    /// <summary>
    /// Tab 右键菜单的「移动到」子菜单：动态列出其他所有标签，
    /// 每个标签提供「移动到此之前 / 之后」两项，供用户重排顺序。
    /// </summary>
    private void TabMoveToMenu_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem moveMenu || moveMenu.Tag is not TabViewModel sourceTab)
            return;

        moveMenu.Items.Clear();

        var otherTabs = VM.Tabs.Where(t => t != sourceTab).ToList();
        if (otherTabs.Count == 0)
        {
            var none = new MenuItem { Header = "(无其他标签)", IsEnabled = false };
            moveMenu.Items.Add(none);
            return;
        }

        foreach (var target in otherTabs)
        {
            // 为每个目标标签创建「之前 / 之后」分组
            var group = new MenuItem { Header = target.Name };
            var before = new MenuItem { Header = "移动到此之前", Tag = new TabMoveArgs(sourceTab, target, false) };
            before.Click += TabMoveToTarget_Click;
            var after = new MenuItem { Header = "移动到此之后", Tag = new TabMoveArgs(sourceTab, target, true) };
            after.Click += TabMoveToTarget_Click;
            group.Items.Add(before);
            group.Items.Add(after);
            moveMenu.Items.Add(group);
        }
    }

    /// <summary>
    /// 「移动到」子项点击：将 sourceTab 移动到 targetTab 之前或之后。
    /// </summary>
    private void TabMoveToTarget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not TabMoveArgs args)
            return;

        var source = args.SourceTab;
        var target = args.TargetTab;

        int sourceIdx = VM.Tabs.IndexOf(source);
        int targetIdx = VM.Tabs.IndexOf(target);
        if (sourceIdx < 0 || targetIdx < 0 || sourceIdx == targetIdx)
            return;

        // ObservableCollection.Move(oldIndex, newIndex)：
        // 先移除 oldIndex，再插入到 newIndex（基于移除后的集合）
        int newIdx = args.After ? targetIdx + 1 : targetIdx;
        // 若源在目标之后，"移除源"会让目标索引减 1，这里补偿
        if (sourceIdx < targetIdx)
            newIdx--;

        // 保持内置「全部」标签固定在最前：不允许把非内置标签移到它之前
        if (target.IsBuiltIn && !args.After)
            newIdx = Math.Max(newIdx, 1);

        if (newIdx < 0) newIdx = 0;
        if (newIdx >= VM.Tabs.Count) newIdx = VM.Tabs.Count - 1;

        VM.Tabs.Move(sourceIdx, newIdx);
        SaveTabOrder();
    }

    /// <summary>
    /// 「移动到」参数载体：记录源标签、目标标签及插入方向。
    /// </summary>
    private sealed class TabMoveArgs
    {
        public TabMoveArgs(TabViewModel source, TabViewModel target, bool after)
        {
            SourceTab = source;
            TargetTab = target;
            After = after;
        }
        public TabViewModel SourceTab { get; }
        public TabViewModel TargetTab { get; }
        public bool After { get; }
    }

    // Drag-and-drop for moving items to tabs
    private Point _itemDragStartPoint;
    private bool _isItemDragging;

    private void Item_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _itemDragStartPoint = e.GetPosition(this);
        _isItemDragging = false;
    }

    private void Item_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _isItemDragging = false;
            return;
        }

        if (sender is FrameworkElement fe && fe.Tag is PanelItemViewModel item)
        {
            var currentPos = e.GetPosition(this);

            if (!_isItemDragging)
            {
                var diff = currentPos - _itemDragStartPoint;
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isItemDragging = true;
                    DragDrop.DoDragDrop(fe, item, DragDropEffects.Move);
                    _isItemDragging = false;
                }
            }
        }
    }

    private void Tab_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(PanelItemViewModel)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void Tab_Drop(object sender, DragEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TabViewModel targetTab)
        {
            if (e.Data.GetDataPresent(typeof(PanelItemViewModel)))
            {
                var item = e.Data.GetData(typeof(PanelItemViewModel)) as PanelItemViewModel;
                if (item != null)
                {
                    var tabId = targetTab.IsBuiltIn ? (int?)null : targetTab.Id;
                    VM.MoveItemToTab(item, tabId);
                }
            }
        }

        e.Handled = true;
    }

    private void Content_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(PanelItemViewModel)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void Content_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(PanelItemViewModel))) return;
        var draggedItem = e.Data.GetData(typeof(PanelItemViewModel)) as PanelItemViewModel;
        if (draggedItem == null) return;

        PanelItemViewModel? targetItem = null;
        if (sender is ItemsControl ic)
        {
            targetItem = FindItemAtPosition(ic, e.GetPosition(ic));
        }
        else if (sender is ListView lv)
        {
            targetItem = FindItemAtPosition(lv, e.GetPosition(lv));
        }

        VM.ReorderItems(draggedItem, targetItem);
    }

    private static PanelItemViewModel? FindItemAtPosition(ItemsControl itemsControl, Point position)
    {
        var hit = VisualTreeHelper.HitTest(itemsControl, position);
        if (hit == null) return null;

        var dep = hit.VisualHit as DependencyObject;
        while (dep != null)
        {
            if (dep is FrameworkElement fe && fe.DataContext is PanelItemViewModel item)
                return item;
            dep = VisualTreeHelper.GetParent(dep);
        }
        return null;
    }

    private void NewTab_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptInput("新建 Tab", "请输入 Tab 名称：", "");
        if (!string.IsNullOrWhiteSpace(name))
            VM.AddTab(name);
    }

    private void RenameTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is TabViewModel tab && !tab.IsBuiltIn)
        {
            var name = PromptInput("重命名 Tab", "请输入新名称：", tab.Name);
            if (!string.IsNullOrWhiteSpace(name))
                VM.RenameTab(tab, name);
        }
    }

    private void DeleteTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is TabViewModel tab && !tab.IsBuiltIn)
        {
            var result = MessageBox.Show(
                $"确定删除 Tab \"{tab.Name}\" 吗？\n（Tab 内的程序和网址不会被删除，只会移回\"全部\"）",
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
                VM.DeleteTab(tab);
        }
    }

    private void ToggleOrientation_Click(object sender, RoutedEventArgs e)
    {
        VM.ToggleTabOrientation();
    }

    // ═══════════════════════════════════════════
    //  View & Add Handlers
    // ═══════════════════════════════════════════

    private void ToggleView_Click(object sender, RoutedEventArgs e)
    {
        VM.CurrentViewMode = VM.IsGridView ? ViewMode.List : ViewMode.Grid;
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddDialog(AddDialog.DialogMode.App) { Owner = this };
        if (dialog.ShowDialog() == true)
            VM.AddAppEntry(dialog.Abbreviation, dialog.TargetPath, dialog.FriendlyName, dialog.Description, dialog.Category);
    }

    private void AddUrl_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddDialog(AddDialog.DialogMode.Url) { Owner = this };
        if (dialog.ShowDialog() == true)
            VM.AddUrlEntry(dialog.Abbreviation, dialog.TargetPath, dialog.FriendlyName, dialog.Description, dialog.Category, dialog.IconPath);
    }

    private void AppItem_Click(object sender, RoutedEventArgs e)
    {
        if (!VM.IsDoubleClick && sender is Button btn && btn.Tag is PanelItemViewModel item)
        {
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            VM.LaunchItem(item, runAsAdmin: alt);
            CheckAutoHideAfterLaunch();
        }
    }

    private void AppItem_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (VM.IsDoubleClick && sender is Button btn && btn.Tag is PanelItemViewModel item)
        {
            VM.LaunchItem(item);
            CheckAutoHideAfterLaunch();
        }
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is PanelItemViewModel item)
        {
            var result = MessageBox.Show($"确定删除 \"{item.DisplayName}\" 吗？",
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
                VM.RemoveItem(item);
        }
    }

    private void RunAsAdminItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is PanelItemViewModel item)
        {
            VM.LaunchItem(item, runAsAdmin: true);
            CheckAutoHideAfterLaunch();
        }
    }

    private void OpenFolderItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is PanelItemViewModel item)
            VM.OpenItemFolder(item);
    }

    private void EditItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is PanelItemViewModel item)
        {
            var mode = item.IsUrl ? AddDialog.DialogMode.Url : AddDialog.DialogMode.App;
            var dialog = new AddDialog(mode, item) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                if (item.IsUrl)
                    VM.UpdateUrlEntry(dialog.OriginalAbbreviation!, dialog.TargetPath, dialog.FriendlyName, dialog.Description, dialog.Category, dialog.IconPath);
                else
                    VM.UpdateAppEntry(dialog.OriginalAbbreviation!, dialog.TargetPath, dialog.FriendlyName, dialog.Description, dialog.Category, dialog.IconPath);
            }
        }
    }

    private void BuildMoveToMenu(MenuItem moveToMenu, PanelItemViewModel item)
    {
        moveToMenu.Items.Clear();

        // "All" tab (tabId = null)
        var allItem = new MenuItem { Header = "全部" };
        allItem.Click += (_, _) => VM.MoveItemToTab(item, null);
        moveToMenu.Items.Add(allItem);

        // User tabs
        foreach (var tab in VM.Tabs)
        {
            if (tab.IsBuiltIn) continue;
            var mi = new MenuItem { Header = tab.Name, Tag = tab.Id };
            mi.Click += (_, _) =>
            {
                if (mi.Tag is int tabId)
                    VM.MoveItemToTab(item, tabId);
            };
            moveToMenu.Items.Add(mi);
        }
    }

    private void MoveToMenu_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem moveToMenu && moveToMenu.Tag is PanelItemViewModel item)
        {
            BuildMoveToMenu(moveToMenu, item);
        }
    }

    private void ListItem_RightClick(object sender, MouseButtonEventArgs e)
    {
        // Find the ListViewItem that was right-clicked
        var dep = (DependencyObject)e.OriginalSource;
        while (dep != null && dep is not ListViewItem)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is ListViewItem lvi && lvi.DataContext is PanelItemViewModel item)
        {
            var cm = new ContextMenu();

            var runAdmin = new MenuItem { Header = "以管理员身份运行", Tag = item };
            runAdmin.Click += RunAsAdminItem_Click;
            cm.Items.Add(runAdmin);

            var openFolder = new MenuItem { Header = "打开所在文件夹", Tag = item };
            openFolder.Click += OpenFolderItem_Click;
            cm.Items.Add(openFolder);

            cm.Items.Add(new Separator());

            var edit = new MenuItem { Header = "编辑...", Tag = item };
            edit.Click += EditItem_Click;
            cm.Items.Add(edit);

            var moveTo = new MenuItem { Header = "移动到" };
            BuildMoveToMenu(moveTo, item);
            cm.Items.Add(moveTo);

            cm.Items.Add(new Separator());

            var delete = new MenuItem { Header = "删除", Tag = item };
            delete.Click += RemoveItem_Click;
            cm.Items.Add(delete);

            cm.IsOpen = true;
        }
    }

    private void ListItem_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!VM.IsDoubleClick) return;
        var dep = (DependencyObject)e.OriginalSource;
        while (dep != null && dep is not ListViewItem)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is ListViewItem lvi && lvi.DataContext is PanelItemViewModel item)
        {
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            VM.LaunchItem(item, runAsAdmin: alt);
            CheckAutoHideAfterLaunch();
        }
    }

    private void ListItem_SingleClick(object sender, MouseButtonEventArgs e)
    {
        if (VM.IsDoubleClick) return;
        var dep = (DependencyObject)e.OriginalSource;
        while (dep != null && dep is not ListViewItem)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is ListViewItem lvi && lvi.DataContext is PanelItemViewModel item)
        {
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            VM.LaunchItem(item, runAsAdmin: alt);
            CheckAutoHideAfterLaunch();
        }
    }

    // ═══════════════════════════════════════════
    //  Ctrl Key Toggle
    // ═══════════════════════════════════════════

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
            VM.ShowFriendlyNames = true;
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
            VM.ShowFriendlyNames = false;
    }

    // ═══════════════════════════════════════════
    //  Window Controls
    // ═══════════════════════════════════════════

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else
                DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        // If tray icon is hidden, always exit directly (no tray to minimize to)
        if (VM.HideTrayIcon)
        {
            ForceClose();
            return;
        }

        // Check if user previously chose to remember their choice
        using var db = new SuperDucker.Shared.Data.DatabaseManager(SuperDucker.Shared.Data.DatabaseManager.GetDefaultDbPath());
        var savedChoice = db.GetSetting("close_action");

        if (savedChoice == "tray")
        {
            Hide();
            return;
        }
        else if (savedChoice == "exit")
        {
            ForceClose();
            return;
        }

        // Show custom dialog with "remember" checkbox
        var dialog = new CloseChoiceDialog { Owner = this };
        var result = dialog.ShowDialog();

        if (result == true)
        {
            // Minimize to tray
            if (dialog.RememberChoice)
                db.SetSetting("close_action", "tray");
            Hide();
        }
        else if (result == false)
        {
            // Exit
            if (dialog.RememberChoice)
                db.SetSetting("close_action", "exit");
            ForceClose();
        }
        // result == null means dialog was closed via X — do nothing
    }

    /// <summary>
    /// Force close the window, bypassing the close-choice dialog.
    ///
    /// 退出流程分成多个独立步骤，每一步用 try/catch 隔离。任何子模块释放失败
    /// 都不能阻断主退出流程，否则会触发 .NET 终结器挂起、CRT abort，最终弹出
    /// 0xE0434352（CLR 托管异常）的"未知软件异常"红框。
    /// </summary>
    public void ForceClose()
    {
        _isExiting = true;

        try { SuperDucker.Shared.Native.PowerManager.Restore(); }
        catch (Exception ex) { LogShutdownError("PowerManager.Restore", ex); }

        try { _hotkeyManager?.Dispose(); }
        catch (Exception ex) { LogShutdownError("HotkeyManager.Dispose", ex); }

        try { _trayManager?.Dispose(); }
        catch (Exception ex) { LogShutdownError("TrayManager.Dispose", ex); }

        try { VM?.Dispose(); }
        catch (Exception ex) { LogShutdownError("ViewModel.Dispose", ex); }

        // 关闭并退出所有窗口。最后调用 Close() 触发正常的 Window_Closing / Closed 流程，
        // 让 WPF 清理 dispatcher、渲染线程、Win32 资源。
        try
        {
            Close();
        }
        catch (Exception ex)
        {
            // 即便 Close 失败，也要确保进程能结束。否则只会留下"幽灵进程"。
            LogShutdownError("MainWindow.Close", ex);
            Environment.Exit(0);
        }

        // 兜底：若 Close() 之后 WPF 仍因为某种原因没有退出（罕见，例如有未释放的
        // 长寿命后台线程），用 Application.Current.Shutdown 强制结束。
        var app = System.Windows.Application.Current;
        if (app != null && ApplicationIsStillRunning())
        {
            try { app.Shutdown(); } catch { /* ignore */ }
        }
    }

    private static bool ApplicationIsStillRunning()
    {
        try
        {
            var p = System.Diagnostics.Process.GetCurrentProcess();
            return !p.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static void LogShutdownError(string stage, Exception ex)
    {
        // 退出阶段只写 Debug，避免在 Release 构建中日志异常触发崩溃。
        System.Diagnostics.Debug.WriteLine($"[Shutdown] {stage} failed: {ex}");
    }

    // ═══════════════════════════════════════════
    //  Hotkeys
    // ═══════════════════════════════════════════

    private void RegisterHotkeys()
    {
        if (_hotkeyManager == null) return;

        var (mod1, vk1, valid1) = GlobalHotkeyManager.ParseHotkeyString(VM.HotkeyToggle);
        if (valid1 && mod1 != 0 && vk1 != 0)
            _hotkeyManager.SetToggleWindowHotkey(mod1, vk1);

        var (mod2, vk2, valid2) = GlobalHotkeyManager.ParseHotkeyString(VM.HotkeySettings);
        if (valid2 && mod2 != 0 && vk2 != 0)
            _hotkeyManager.SetOpenSettingsHotkey(mod2, vk2);

        var (mod3, vk3, valid3) = GlobalHotkeyManager.ParseHotkeyString(VM.HotkeyShop);
        if (valid3 && mod3 != 0 && vk3 != 0)
            _hotkeyManager.SetOpenShopHotkey(mod3, vk3);

        var (mod4, vk4, valid4) = GlobalHotkeyManager.ParseHotkeyString(VM.HotkeyPack);
        if (valid4 && mod4 != 0 && vk4 != 0)
            _hotkeyManager.SetOpenPackHotkey(mod4, vk4);
    }

    private void OnToggleWindow()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
            ShopPanel.Visibility = Visibility.Collapsed;
            Show();
            WindowState = WindowState.Normal;
            // 每次呼出都按设置重新计算位置（居中 / 跟随鼠标 / 上次位置）
            ApplyWindowPosition();
            Activate();
        }
    }

    private void OnOpenSettings()
    {
        // Show window first if hidden
        if (!IsVisible)
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
        // Open settings panel
        if (SettingsPanel.Visibility != Visibility.Visible)
            ToggleSettings();
    }

    private void OnOpenShop()
    {
        // Show window first if hidden
        if (!IsVisible)
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
        // Open shop panel
        ToggleShop();
    }

    private void OnOpenPack()
    {
        // Show window first if hidden
        if (!IsVisible)
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
        // Open pack dialog
        var packDialog = new PackDialog(VM.Db) { Owner = this };
        packDialog.ShowDialog();
    }

    private void Shop_Click(object sender, RoutedEventArgs e)
    {
        ToggleShop();
    }

    private void ToggleShop()
    {
        if (ShopPanel.Visibility == Visibility.Visible)
        {
            ShopPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            // Collapse settings if open (mutual exclusion)
            SettingsPanel.Visibility = Visibility.Collapsed;

            try
            {
                if (_shopPanel == null)
                {
                    _shopPanel = new ShopPanel(VM);
                    _shopPanel.BackRequested += (_, _) => ShopPanel.Visibility = Visibility.Collapsed;
                    _shopPanel.Installed += (_, _) => RebuildContent();
                    ShopPanel.Child = _shopPanel;
                }
                ShopPanel.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开商店时出错：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// Refreshes the shop panel to show newly added packages.
    /// Called by PackDialog after successful package creation and import.
    /// </summary>
    public async Task RefreshShopUIAsync()
    {
        if (_shopPanel != null)
        {
            await Dispatcher.InvokeAsync(() => _shopPanel.RefreshPackagesAsync());
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        ToggleSettings();
    }

    private void ToggleSettings()
    {
        if (SettingsPanel.Visibility == Visibility.Visible)
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            // Collapse shop if open (mutual exclusion)
            ShopPanel.Visibility = Visibility.Collapsed;
            try
            {
                if (_settingsPanel == null)
                {
                    _settingsPanel = new SettingsDialog(VM);
                    _settingsPanel.BackRequested += (_, _) => SettingsPanel.Visibility = Visibility.Collapsed;
                    SettingsPanel.Child = _settingsPanel;
                }
                SettingsPanel.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开设置时出错：\n{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // ═══════════════════════════════════════════
    //  Input Prompt Dialog
    // ═══════════════════════════════════════════

    private string? PromptInput(string title, string label, string defaultValue)
    {
        var win = new Window
        {
            Title = title, Width = 380, Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this, ResizeMode = ResizeMode.NoResize,
            Background = Brushes.Transparent,
            WindowStyle = WindowStyle.None, AllowsTransparency = true
        };

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = (SolidColorBrush)FindResource("TextSecondaryBrush"),
            FontSize = 13, Margin = new Thickness(0, 0, 0, 8)
        });

        var textBox = new TextBox
        {
            Text = defaultValue,
            Style = (Style)FindResource("SearchBoxStyle")
        };
        panel.Children.Add(textBox);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var cancelBtn = new Button
        {
            Content = "取消", Style = (Style)FindResource("FlatButton"), Width = 80, Margin = new Thickness(0, 0, 10, 0)
        };
        cancelBtn.Click += (s, e) => { win.DialogResult = false; win.Close(); };

        var okBtn = new Button
        {
            Content = "确定", Style = (Style)FindResource("FlatButton"), Width = 80,
            Background = (SolidColorBrush)FindResource("AccentBlueBrush"),
            Foreground = Brushes.White, FontWeight = FontWeights.SemiBold
        };
        okBtn.Click += (s, e) => { win.DialogResult = true; win.Close(); };

        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(okBtn);
        panel.Children.Add(btnRow);

        win.Content = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = (SolidColorBrush)FindResource("BgDarkBrush"),
            BorderBrush = (SolidColorBrush)FindResource("BgCardHoverBrush"),
            BorderThickness = new Thickness(1),
            Child = panel, Margin = new Thickness(4)
        };

        win.Loaded += (s, e) => { textBox.Focus(); textBox.SelectAll(); };

        return win.ShowDialog() == true ? textBox.Text : null;
    }
}

// ═══════════════════════════════════════════
//  Converters
// ═══════════════════════════════════════════

public class BoolToVis : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => value is Visibility v && v == Visibility.Visible;
}

public class BoolToCollapse : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => value is Visibility v && v == Visibility.Visible;
}
