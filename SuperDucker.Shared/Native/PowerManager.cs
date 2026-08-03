using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SuperDucker.Shared.Native;

/// <summary>
/// Win32 SetThreadExecutionState 封装。只要 SuperDucker 的任意进程存活，就阻止系统
/// 自动睡眠 / 待机 / 熄屏 / 休眠，直到进程退出或主动调用 <see cref="Restore"/>。
/// </summary>
public static class PowerManager
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    // Flags (see WINBASE.H)
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001; // 阻止系统睡眠/待机/休眠
    private const uint ES_DISPLAY_REQUIRED = 0x00000002; // 阻止显示器关闭
    // 远离模式：在 Modern Standby (S0ix / 连接待机) 笔记本上，固件常忽略 ES_SYSTEM_REQUIRED，
    // 必须附加 ES_AWAYMODE_REQUIRED 才能让系统保持"活动"后台，否则"阻止睡眠"形同虚设。
    private const uint ES_AWAYMODE_REQUIRED = 0x00000040;

    private static Timer? _heartbeat;
    private static readonly object _lock = new();

    /// <summary>
    /// 请求系统与显示器保持活动（阻止睡眠/待机/熄屏/休眠），并启动周期心跳定时器持续
    /// 续期以对抗现代待机(S0ix / 连接待机)。返回 false 表示底层 API 调用失败。
    /// </summary>
    public static bool PreventSleep()
    {
        lock (_lock)
        {
            if (!Apply()) return false;

            _heartbeat?.Dispose();
            // 每 55 秒续期一次：小于大多数电源计划超时，确保状态不被操作系统丢弃
            _heartbeat = new Timer(_ => Apply(), null,
                TimeSpan.FromSeconds(55), TimeSpan.FromSeconds(55));
            return true;
        }
    }

    /// <summary>
    /// 向系统声明"系统与显示器都需保持活动"，并返回是否成功。
    /// 优先附加 ES_AWAYMODE_REQUIRED 以兼容 Modern Standby 机型；
    /// 若失败（个别老旧系统不支持该标志）则降级重试不含 away mode 的组合。
    /// </summary>
    private static bool Apply()
    {
        uint flags = ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED | ES_AWAYMODE_REQUIRED;
        if (SetThreadExecutionState(flags) != 0) return true;
        // 降级：去掉 away mode 再试一次
        return SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED) != 0;
    }

    /// <summary>
    /// 释放之前的所有保活请求，恢复正常电源行为。进程退出时由 OS 自动清除。
    /// </summary>
    public static void Restore()
    {
        lock (_lock)
        {
            _heartbeat?.Dispose();
            _heartbeat = null;
            // 仅 ES_CONTINUOUS 表示清除所有保活请求
            SetThreadExecutionState(ES_CONTINUOUS);
        }
    }
}
