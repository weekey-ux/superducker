using System;
using System.Runtime.InteropServices;

namespace SuperDucker.Shared.Native;

/// <summary>
/// Wrapper around Win32 <see cref="SetThreadExecutionState"/> used to prevent the
/// system from entering sleep / standby / hibernation while SuperDucker is running
/// (when the "阻止系统待机" setting is enabled).
/// </summary>
public static class PowerManager
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    // Flags (see WINBASE.H)
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001; // prevent sleep / standby
    private const uint ES_DISPLAY_REQUIRED = 0x00000002; // keep display on (optional)
    private const uint ES_AWAYMODE_REQUIRED = 0x00000040;

    /// <summary>
    /// Requests that the system (and optionally display) stay awake continuously.
    /// Call <see cref="Restore"/> to release the request.
    /// </summary>
    /// <param name="keepDisplayOn">When true, also prevents the display from turning off.</param>
    public static void PreventSleep(bool keepDisplayOn = false)
    {
        uint flags = ES_CONTINUOUS | ES_SYSTEM_REQUIRED;
        if (keepDisplayOn) flags |= ES_DISPLAY_REQUIRED;
        SetThreadExecutionState(flags);
    }

    /// <summary>
    /// Releases any previous execution-state request (returns to normal power behavior).
    /// </summary>
    public static void Restore()
    {
        SetThreadExecutionState(ES_CONTINUOUS);
    }
}
