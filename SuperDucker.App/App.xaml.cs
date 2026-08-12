using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
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

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    // ShowWindow 的还原常量：将最小化窗口恢复为之前的大小和位置
    private const int SW_RESTORE = 9;

    // 与 GlobalHotkeyManager.WM_OPEN_PACK 保持一致：请求已运行实例打开打包窗口
    private const int WM_OPEN_PACK = 0x0401;

    public App()
    {
        // 在构造期订阅全局异常兜底，避免任何未捕获异常被系统弹"未知软件异常(0xE0434352)"红框。
        // 注意：这些订阅必须先于 OnStartup / 任何事件循环，否则可能错过。
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // --pack 不再是独立实例：它只是「请求打开打包窗口」的语义。
        // 若已有主面板在运行，则通过 WM_OPEN_PACK 通知它打开，自己立即退出；
        // 若没有，则正常成为唯一实例，并在启动后自动打开打包窗口。
        bool packMode = e.Args.Any(a => a.Equals("--pack", StringComparison.OrdinalIgnoreCase));

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
                if (packMode)
                {
                    // 请求已运行实例打开打包窗口，而非另起进程
                    PostMessage(hwnd, WM_OPEN_PACK, IntPtr.Zero, IntPtr.Zero);
                }
            }
            Shutdown();
            return;
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
        // --pack 现在是唯一实例的普通启动，只是额外在窗口加载后打开打包窗口。
        // 判断交给 MainWindow（读取命令行 --pack），这里统一走主窗口创建流程。

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

    // ═══════════════════════════════════════════
    //  全局异常兜底（防止出现 0xE0434352 红框错误弹窗）
    // ═══════════════════════════════════════════

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // WPF UI 线程未处理异常：记录并标记 Handled，避免进程崩溃 / 红框弹窗。
        System.Diagnostics.Debug.WriteLine($"[UnhandledUI] {e.Exception}");
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // 非 UI 线程的未处理异常：只能记录，不能阻止进程终止（CLR 在此之后会调用 Terminate）。
        if (e.ExceptionObject is Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UnhandledDomain] {ex}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[UnhandledDomain] non-CLR exception: {e.ExceptionObject}");
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // 后台 Task 抛异常没人观察：让 GC 终结器跑时不会因此杀死进程。
        System.Diagnostics.Debug.WriteLine($"[UnobservedTask] {e.Exception}");
        e.SetObserved();
    }
}
