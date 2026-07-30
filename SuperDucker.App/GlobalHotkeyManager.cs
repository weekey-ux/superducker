using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace SuperDucker.App;

/// <summary>
/// Manages system-wide hotkeys using Win32 RegisterHotKey.
/// </summary>
public sealed class GlobalHotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    // Modifier flags (must match Win32 values)
    public const int MOD_ALT = 0x0001;
    public const int MOD_CONTROL = 0x0002;
    public const int MOD_SHIFT = 0x0004;
    public const int MOD_WIN = 0x0008;
    public const int MOD_NOREPEAT = 0x4000;

    private const int HOTKEY_TOGGLE_WINDOW = 1;
    private const int HOTKEY_OPEN_SETTINGS = 2;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Window _window;
    private HwndSource? _hwndSource;
    private bool _toggleRegistered;
    private bool _settingsRegistered;

    // Pending hotkey values to register when HwndSource becomes available
    private (int modifiers, int vk)? _pendingToggle;
    private (int modifiers, int vk)? _pendingSettings;

    public event Action? ToggleWindowRequested;
    public event Action? OpenSettingsRequested;

    public GlobalHotkeyManager(Window window)
    {
        _window = window;
        var helper = new WindowInteropHelper(window);
        if (helper.Handle != IntPtr.Zero)
        {
            _hwndSource = HwndSource.FromHwnd(helper.Handle);
            _hwndSource?.AddHook(WndProc);
        }
        else
        {
            // Window handle not yet available — defer until loaded
            window.Loaded += (_, _) =>
            {
                helper = new WindowInteropHelper(window);
                _hwndSource = HwndSource.FromHwnd(helper.Handle);
                _hwndSource?.AddHook(WndProc);

                // Register any pending hotkeys now that HwndSource is ready
                if (_pendingToggle.HasValue)
                {
                    var (mod, vk) = _pendingToggle.Value;
                    SetToggleWindowHotkey(mod, vk);
                    _pendingToggle = null;
                }
                if (_pendingSettings.HasValue)
                {
                    var (mod, vk) = _pendingSettings.Value;
                    SetOpenSettingsHotkey(mod, vk);
                    _pendingSettings = null;
                }
            };
        }
    }

    /// <summary>
    /// Register or update the toggle-window hotkey.
    /// </summary>
    public void SetToggleWindowHotkey(int modifiers, int vk)
    {
        UnregisterToggleWindow();
        if (_hwndSource == null)
        {
            // HwndSource not ready yet — save for later registration
            _pendingToggle = (modifiers, vk);
            return;
        }
        if (modifiers != 0 && vk != 0)
        {
            _toggleRegistered = RegisterHotKey(_hwndSource.Handle, HOTKEY_TOGGLE_WINDOW,
                modifiers | MOD_NOREPEAT, vk);
        }
    }

    /// <summary>
    /// Register or update the open-settings hotkey.
    /// </summary>
    public void SetOpenSettingsHotkey(int modifiers, int vk)
    {
        UnregisterOpenSettings();
        if (_hwndSource == null)
        {
            // HwndSource not ready yet — save for later registration
            _pendingSettings = (modifiers, vk);
            return;
        }
        if (modifiers != 0 && vk != 0)
        {
            _settingsRegistered = RegisterHotKey(_hwndSource.Handle, HOTKEY_OPEN_SETTINGS,
                modifiers | MOD_NOREPEAT, vk);
        }
    }

    public void UnregisterToggleWindow()
    {
        if (_toggleRegistered && _hwndSource != null)
        {
            UnregisterHotKey(_hwndSource.Handle, HOTKEY_TOGGLE_WINDOW);
            _toggleRegistered = false;
        }
    }

    public void UnregisterOpenSettings()
    {
        if (_settingsRegistered && _hwndSource != null)
        {
            UnregisterHotKey(_hwndSource.Handle, HOTKEY_OPEN_SETTINGS);
            _settingsRegistered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var hotkeyId = wParam.ToInt32();
            switch (hotkeyId)
            {
                case HOTKEY_TOGGLE_WINDOW:
                    ToggleWindowRequested?.Invoke();
                    handled = true;
                    break;
                case HOTKEY_OPEN_SETTINGS:
                    OpenSettingsRequested?.Invoke();
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterToggleWindow();
        UnregisterOpenSettings();
        _hwndSource?.RemoveHook(WndProc);
    }

    // ═══ Helpers ═══

    /// <summary>
    /// Convert WPF Key to Win32 virtual key code.
    /// </summary>
    public static int KeyToVk(Key key) => KeyInterop.VirtualKeyFromKey(key);

    /// <summary>
    /// Convert Win32 virtual key code to WPF Key.
    /// </summary>
    public static Key VkToKey(int vk) => KeyInterop.KeyFromVirtualKey(vk);

    /// <summary>
    /// Convert modifier flags to a display string like "Ctrl+Shift+S".
    /// </summary>
    public static string ModifiersToString(int modifiers, int vk)
    {
        var parts = new List<string>();
        if ((modifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & MOD_WIN) != 0) parts.Add("Win");
        if (vk != 0)
        {
            var key = VkToKey(vk);
            parts.Add(key.ToString());
        }
        return parts.Count > 0 ? string.Join("+", parts) : "无";
    }

    /// <summary>
    /// Parse a hotkey string like "Ctrl+Shift+S" back to modifiers + vk.
    /// Validates that the key is not just modifiers and the VK can be registered.
    /// Returns (modifiers, vk) or (0, 0) if parsing fails.
    /// </summary>
    public static (int modifiers, int vk, bool isValid) ParseHotkeyString(string s)
    {
        if (string.IsNullOrEmpty(s) || s == "无") return (0, 0, false);
        
        int modifiers = 0;
        int vk = 0;
        bool hasMainKey = false;
        
        foreach (var part in s.Split('+'))
        {
            switch (part.Trim())
            {
                case "Ctrl": modifiers |= MOD_CONTROL; break;
                case "Alt": modifiers |= MOD_ALT; break;
                case "Shift": modifiers |= MOD_SHIFT; break;
                case "Win": modifiers |= MOD_WIN; break;
                default:
                    if (Enum.TryParse<Key>(part.Trim(), out var key))
                    {
                        var keyVk = KeyToVk(key);
                        // Reject modifier keys as main key
                        if (keyVk == 0x11 || keyVk == 0x12 || keyVk == 0x10 || keyVk == 0x5B || keyVk == 0x5C)
                            return (0, 0, false);
                            
                        vk = keyVk;
                        hasMainKey = true;
                    }
                    else
                    {
                        return (0, 0, false);
                    }
                    break;
            }
        }
        
        // Must have at least one modifier and one non-modifier key
        if (modifiers == 0 || !hasMainKey || vk == 0)
            return (0, 0, false);
            
        return (modifiers, vk, true);
    }
}
