using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using SuperDucker.Shared.Data;

namespace SuperDucker.App;

public partial class App : Application
{
    // 单实例互斥体名称（加 Local\\ 前缀，限定为当前会话，避免跨用户冲突）
    private const string MutexName = "Local\\SuperDucker_SingleInstance";
    private Mutex? _mutex;

    // 以下为 Windows API 声明，用于把已运行的实例窗口前置到前台
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    // ShowWindow 的还原常量：将最小化窗口恢复为之前的大小和位置
    private const int SW_RESTORE = 9;

    protected override void OnStartup(StartupEventArgs e)
    {
        // --pack 模式作为独立实例运行（不受单实例限制，便于单独打包启动项）
        bool packMode = e.Args.Any(a => a.Equals("--pack", StringComparison.OrdinalIgnoreCase));

        if (!packMode)
        {
            // 创建命名互斥体，确保同一时间只有一个 SuperDucker 主程序运行
            _mutex = new Mutex(true, MutexName, out bool createdNew);

            if (!createdNew)
            {
                // Another instance is running — activate its window and exit
                var hwnd = FindWindow(null, "SuperDucker");
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, SW_RESTORE);
                    SetForegroundWindow(hwnd);
                }
                Shutdown();
                return;
            }
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        bool packMode = e.Args.Any(a => a.Equals("--pack", StringComparison.OrdinalIgnoreCase));

        if (packMode)
        {
            // Standalone pack dialog mode - show main window first so we can set owner
            var packMainWindow = new MainWindow();
            packMainWindow.Show();

            DatabaseManager? db = null;
            try { db = new DatabaseManager(DatabaseManager.GetDefaultDbPath()); }
            catch { }

            var packDialog = new PackDialog(db);
            packDialog.Owner = packMainWindow; // Set owner for UI refresh capability
            packDialog.ShowDialog();

            db?.Dispose();
            packMainWindow.Close();
            Shutdown();
            return;
        }

        // Show window first for fast perceived startup
        var mainWindow = new MainWindow();
        mainWindow.Show();

        // Defer non-critical maintenance to after window is visible
        mainWindow.Loaded += (_, _) =>
        {
            Task.Run(() =>
            {
                try
                {
                    using var db = new DatabaseManager(DatabaseManager.GetDefaultDbPath());
                    ShortcutManager.RepairAllShortcuts(db);

                    // Ensure link/ is in PATH (only broadcast if changed)
                    if (ShortcutManager.EnsureLinkInPath())
                    {
                        ShortcutManager.BroadcastPathChange();
                    }
                }
                catch (Exception ex)
                {
                    // Don't crash on startup if DB doesn't exist yet or other issues
                    System.Diagnostics.Debug.WriteLine($"[Startup Maintenance] Error: {ex.Message}");
                }
            });
        };
    }
}
