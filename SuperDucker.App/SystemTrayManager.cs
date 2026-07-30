using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace SuperDucker.App;

/// <summary>
/// Manages the system tray (notification area) icon and context menu.
/// </summary>
public sealed class SystemTrayManager : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Window _window;

    public bool IsVisible
    {
        get => _notifyIcon.Visible;
        set => _notifyIcon.Visible = value;
    }

    public event Action? OpenSettingsRequested;
    public event Action? OpenShopRequested;

    public SystemTrayManager(Window window)
    {
        _window = window;

        _notifyIcon = new Forms.NotifyIcon
        {
            Visible = true,
            Text = ""
        };

        // Try to load the LOGO.ico embedded resource
        try
        {
            var uri = new Uri("pack://application:,,,/LOGO.ico", UriKind.Absolute);
            var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
            if (stream != null)
                _notifyIcon.Icon = new Icon(stream, 16, 16);
        }
        catch
        {
            // Fallback: use the application icon or default
            try
            {
                var exePath = Environment.ProcessPath;
                if (exePath != null)
                    _notifyIcon.Icon = Icon.ExtractAssociatedIcon(exePath);
            }
            catch { }
        }

        // Double-click tray icon → show window
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();

        // Build context menu
        var contextMenu = new Forms.ContextMenuStrip();

        var showItem = new Forms.ToolStripMenuItem("显示面板");
        showItem.Font = new Font(showItem.Font, System.Drawing.FontStyle.Bold);
        showItem.Click += (_, _) => ShowWindow();

        var shopItem = new Forms.ToolStripMenuItem("商城");
        shopItem.Click += (_, _) =>
        {
            ShowWindow();
            OpenShopRequested?.Invoke();
        };

        var settingsItem = new Forms.ToolStripMenuItem("设置");
        settingsItem.Click += (_, _) =>
        {
            ShowWindow();
            OpenSettingsRequested?.Invoke();
        };

        var exitItem = new Forms.ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitApp();

        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(shopItem);
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    private void ShowWindow()
    {
        if (_window is MainWindow mw)
        {
            mw.SettingsPanel.Visibility = Visibility.Collapsed;
            mw.ShopPanel.Visibility = Visibility.Collapsed;
        }
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void ExitApp()
    {
        _notifyIcon.Visible = false;
        if (_window is MainWindow mw)
            mw.ForceClose();
        else
            _window.Close();
    }

    public void ShowBalloonTip(string title, string text, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(3000, title, text, icon);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
