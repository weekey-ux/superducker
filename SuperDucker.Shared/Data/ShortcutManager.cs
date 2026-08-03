using System.Runtime.InteropServices;
using SuperDucker.Shared.Models;

namespace SuperDucker.Shared.Data;

/// <summary>
/// 管理 link/ 目录下的 .lnk 快捷方式文件以及 PATH 环境变量。
/// 使用动态 COM 互操作（WScript.Shell）而非引用 IWshRuntimeLibrary，
/// 以兼容 .NET 8 的 MSBuild 构建模式。
/// </summary>
public static class ShortcutManager
{
    // ═══════════════════════════════════════════
    //  .lnk 文件操作
    // ═══════════════════════════════════════════

    /// <summary>
    /// 在 link/ 目录下为指定程序条目创建或覆盖 .lnk 文件。
    /// </summary>
    public static string CreateShortcut(AppEntry entry)
    {
        var linkDir = DatabaseManager.GetLinkDirectory();
        Directory.CreateDirectory(linkDir);

        var lnkPath = Path.Combine(linkDir, $"{entry.Abbreviation}.lnk");

        try
        {
            CreateLnkFile(lnkPath, entry.TargetPath,
                entry.WorkingDirectory ?? Path.GetDirectoryName(entry.TargetPath) ?? "",
                entry.Description ?? "",
                ResolveIconLocation(entry));
        }
        catch
        {
            // COM may fail on MTA threads, trimmed builds, or if WScript.Shell is unavailable.
            // PowerShell fallback always works.
            CreateLnkViaPowerShell(lnkPath, entry.TargetPath,
                entry.WorkingDirectory ?? Path.GetDirectoryName(entry.TargetPath) ?? "",
                entry.Description ?? "",
                ResolveIconLocation(entry));
        }

        return lnkPath;
    }

    /// <summary>
    /// Deletes the .lnk file for the given abbreviation from link/.
    /// </summary>
    public static bool DeleteShortcut(string abbreviation)
    {
        var lnkPath = Path.Combine(DatabaseManager.GetLinkDirectory(), $"{abbreviation}.lnk");
        if (File.Exists(lnkPath))
        {
            File.Delete(lnkPath);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Renames a .lnk file when the abbreviation changes.
    /// </summary>
    public static void RenameShortcut(string oldAbbreviation, string newAbbreviation)
    {
        var linkDir = DatabaseManager.GetLinkDirectory();
        var oldPath = Path.Combine(linkDir, $"{oldAbbreviation}.lnk");
        var newPath = Path.Combine(linkDir, $"{newAbbreviation}.lnk");

        if (File.Exists(oldPath))
        {
            if (File.Exists(newPath))
                File.Delete(newPath);
            File.Move(oldPath, newPath);
        }
    }

    // ═══════════════════════════════════════════
    //  .url File Operations (Internet Shortcuts)
    // ═══════════════════════════════════════════

    /// <summary>
    /// Creates or overwrites a .bat launcher file in the link/ directory for the given URL entry.
    /// Unlike .url Internet Shortcuts (which Win+R does NOT launch), a .bat is directly executable
    /// from Win+R once the link/ directory is on PATH and .bat is in PATHEXT (it is by default).
    /// Typing the abbreviation opens the URL in the default browser.
    /// </summary>
    public static string CreateUrlShortcut(UrlEntry entry)
    {
        var linkDir = DatabaseManager.GetLinkDirectory();
        Directory.CreateDirectory(linkDir);

        var batPath = Path.Combine(linkDir, $"{entry.Abbreviation}.bat");

        // Escape % for the batch parser so URLs with %20, %3A, etc. survive verbatim.
        // Inside double quotes, & and other cmd metacharacters are treated literally.
        var safeUrl = entry.Url.Replace("%", "%%");
        var content = "@echo off\r\n" +
                      "start \"\" \"" + safeUrl + "\"\r\n";

        // Write as system ANSI so non-ASCII URLs display correctly in the cmd console.
        File.WriteAllText(batPath, content, System.Text.Encoding.Default);

        // Clean up legacy .url file from older versions
        var legacyUrl = Path.ChangeExtension(batPath, ".url");
        if (File.Exists(legacyUrl)) File.Delete(legacyUrl);

        return batPath;
    }

    /// <summary>
    /// Deletes the .bat file for the given URL abbreviation from link/.
    /// Also cleans up legacy .url files.
    /// </summary>
    public static bool DeleteUrlShortcut(string abbreviation)
    {
        var linkDir = DatabaseManager.GetLinkDirectory();
        bool deleted = false;

        var batPath = Path.Combine(linkDir, $"{abbreviation}.bat");
        if (File.Exists(batPath)) { File.Delete(batPath); deleted = true; }

        // Clean up legacy .url file
        var urlPath = Path.Combine(linkDir, $"{abbreviation}.url");
        if (File.Exists(urlPath)) { File.Delete(urlPath); deleted = true; }

        return deleted;
    }

    /// <summary>
    /// Renames a URL shortcut file when the abbreviation changes.
    /// </summary>
    public static void RenameUrlShortcut(string oldAbbreviation, string newAbbreviation)
    {
        var linkDir = DatabaseManager.GetLinkDirectory();

        var oldBat = Path.Combine(linkDir, $"{oldAbbreviation}.bat");
        var newBat = Path.Combine(linkDir, $"{newAbbreviation}.bat");
        if (File.Exists(oldBat))
        {
            if (File.Exists(newBat)) File.Delete(newBat);
            File.Move(oldBat, newBat);
        }

        // Clean up legacy .url file from older versions
        var oldUrl = Path.Combine(linkDir, $"{oldAbbreviation}.url");
        if (File.Exists(oldUrl)) File.Delete(oldUrl);
    }

    /// <summary>
    /// Deletes any shortcut file (.lnk, .bat, or .url) for the given abbreviation.
    /// </summary>
    public static bool DeleteAnyShortcut(string abbreviation)
    {
        var linkDir = DatabaseManager.GetLinkDirectory();
        bool deleted = false;

        var lnkPath = Path.Combine(linkDir, $"{abbreviation}.lnk");
        if (File.Exists(lnkPath)) { File.Delete(lnkPath); deleted = true; }

        var batPath = Path.Combine(linkDir, $"{abbreviation}.bat");
        if (File.Exists(batPath)) { File.Delete(batPath); deleted = true; }

        var urlPath = Path.Combine(linkDir, $"{abbreviation}.url");
        if (File.Exists(urlPath)) { File.Delete(urlPath); deleted = true; }

        return deleted;
    }

    /// <summary>
    /// Reads the target path from an existing .lnk file.
    /// </summary>
    public static string? ReadShortcutTarget(string lnkPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            string target = shortcut.TargetPath;
            Marshal.ReleaseComObject(shortcut);
            Marshal.ReleaseComObject(shell);
            return target;
        }
        catch
        {
            return null;
        }
    }

    // ═══════════════════════════════════════════
    //  Path Repair (on startup after move)
    // ═══════════════════════════════════════════

    /// <summary>
    /// Checks and repairs all shortcut paths. Called on superducker.exe startup.
    /// If the SuperDucker folder was moved, recalculates absolute paths from the new root.
    /// </summary>
    /// <returns>Number of shortcuts repaired.</returns>
    public static int RepairAllShortcuts(DatabaseManager db)
    {
        var apps = db.GetAllApps();
        var root = DatabaseManager.GetRootDirectory();
        var linkDir = DatabaseManager.GetLinkDirectory();
        var repaired = 0;

        System.Diagnostics.Debug.WriteLine($"[RepairAllShortcuts] root={root}, linkDir={linkDir}, apps={apps.Count}");

        foreach (var app in apps)
        {
            var needsRepair = false;
            var reason = "";

            // Check if target still exists at recorded path
            if (!File.Exists(app.TargetPath))
            {
                needsRepair = true;
                reason = $"target missing: {app.TargetPath}";
            }

            // Check if the .lnk file exists
            var lnkPath = Path.Combine(linkDir, $"{app.Abbreviation}.lnk");
            if (!File.Exists(lnkPath))
            {
                needsRepair = true;
                reason = string.IsNullOrEmpty(reason) ? $"lnk missing: {lnkPath}" : $"{reason} + lnk missing";
            }
            else if (!needsRepair)
            {
                // .lnk exists and target exists, but verify .lnk points to correct target
                var actualTarget = ReadShortcutTarget(lnkPath);
                if (actualTarget != null && !string.Equals(actualTarget, app.TargetPath, StringComparison.OrdinalIgnoreCase))
                {
                    needsRepair = true;
                    reason = $"target mismatch: lnk→{actualTarget}, db→{app.TargetPath}";
                }
                // If actualTarget is null, COM failed but shortcut may still be valid — skip repair
            }

            if (needsRepair)
            {
                System.Diagnostics.Debug.WriteLine($"[RepairAllShortcuts] Repairing {app.Abbreviation}: {reason}");
                // Try to resolve the path relative to the new root
                if (TryResolvePath(app.TargetPath, root, out var newPath))
                {
                    app.TargetPath = newPath;
                    if (!string.IsNullOrEmpty(app.WorkingDirectory) &&
                        TryResolvePath(app.WorkingDirectory, root, out var newWd))
                    {
                        app.WorkingDirectory = newWd;
                    }
                    db.UpdateApp(app);
                }

                // Recreate the .lnk file
                CreateShortcut(app);
                repaired++;
            }
        }

        // Repair URL entries: ensure .bat launcher files exist
        var urls = db.GetAllUrls();
        foreach (var url in urls)
        {
            var batPath = Path.Combine(linkDir, $"{url.Abbreviation}.bat");
            if (!File.Exists(batPath))
            {
                CreateUrlShortcut(url);
                repaired++;
            }
        }

        // Ensure sd.lnk exists in link/ so Win+R can run "sd" directly
        var sdExePath = Path.Combine(root, "sd.exe");
        var sdLnkPath = Path.Combine(linkDir, "sd.lnk");
        if (File.Exists(sdExePath) && !File.Exists(sdLnkPath))
        {
            CreateRawShortcut(sdExePath, sdLnkPath, "SuperDucker CLI");
            repaired++;
        }

        // Ensure superducker.lnk exists in link/ for Win+R access to the panel
        var panelExePath = Path.Combine(root, "superducker.exe");
        var panelLnkPath = Path.Combine(linkDir, "superducker.lnk");
        if (File.Exists(panelExePath) && !File.Exists(panelLnkPath))
        {
            CreateRawShortcut(panelExePath, panelLnkPath, "SuperDucker Panel");
            repaired++;
        }

        return repaired;
    }

    /// <summary>
    /// Tries to resolve a file path that may be stale due to SuperDucker being moved.
    /// Uses relative path logic: if the old path was inside the old root, compute the
    /// relative portion and apply it to the new root.
    /// </summary>
    private static bool TryResolvePath(string oldPath, string newRoot, out string resolvedPath)
    {
        resolvedPath = oldPath;

        // Look for "app" or "link" directory markers in the path
        var normalizedOld = oldPath.Replace('/', '\\');
        string[] markers = ["\\app\\", "\\link\\"];

        foreach (var marker in markers)
        {
            var markerIndex = normalizedOld.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
            {
                var relativePart = normalizedOld[(markerIndex + 1)..]; // e.g. "app\chrome\chrome.exe"
                resolvedPath = Path.Combine(newRoot, relativePart);
                if (File.Exists(resolvedPath) || Directory.Exists(resolvedPath))
                    return true;
            }
        }

        // Fallback: check if the original path still exists
        if (File.Exists(oldPath) || Directory.Exists(oldPath))
        {
            resolvedPath = oldPath;
            return true;
        }

        return false;
    }

    // ═══════════════════════════════════════════
    //  PATH Environment Variable
    // ═══════════════════════════════════════════

    /// <summary>
    /// Ensures the link/ directory is in the system PATH environment variable.
    /// </summary>
    public static bool EnsureLinkInPath()
    {
        var linkDir = DatabaseManager.GetLinkDirectory();
        Directory.CreateDirectory(linkDir);

        var currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
        var paths = currentPath.Split(';', StringSplitOptions.RemoveEmptyEntries);

        if (paths.Any(p => string.Equals(p.TrimEnd('\\'), linkDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
        {
            System.Diagnostics.Debug.WriteLine($"[EnsureLinkInPath] Already in PATH: {linkDir}");
            return false; // Already in PATH
        }

        var newPath = string.IsNullOrEmpty(currentPath) ? linkDir : $"{currentPath.TrimEnd(';')};{linkDir}";
        Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.User);
        System.Diagnostics.Debug.WriteLine($"[EnsureLinkInPath] Added to PATH: {linkDir}");
        return true;
    }

    /// <summary>
    /// Removes the link/ directory from the user PATH environment variable.
    /// </summary>
    public static bool RemoveLinkFromPath()
    {
        var linkDir = DatabaseManager.GetLinkDirectory();
        var currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
        var paths = currentPath.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var filtered = paths.Where(p => !string.Equals(p.TrimEnd('\\'), linkDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

        var newPath = string.Join(";", filtered);
        if (newPath == currentPath) return false; // Wasn't in PATH

        Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.User);
        return true;
    }

    /// <summary>
    /// Checks if the link/ directory is currently in the user PATH.
    /// </summary>
    public static bool IsLinkInPath()
    {
        var linkDir = DatabaseManager.GetLinkDirectory();
        var currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
        var paths = currentPath.Split(';', StringSplitOptions.RemoveEmptyEntries);
        return paths.Any(p => string.Equals(p.TrimEnd('\\'), linkDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Broadcasts WM_SETTINGCHANGE to notify Explorer that PATH has changed.
    /// This makes Win+R immediately pick up the new PATH without requiring a restart.
    /// </summary>
    public static void BroadcastPathChange()
    {
        SendMessageTimeout(
            HWND_BROADCAST,
            WM_SETTINGCHANGE,
            IntPtr.Zero,
            "Environment",
            SendMessageTimeoutFlags.SMTO_ABORTIFHUNG,
            5000,
            out _
        );
    }

    // ═══════════════════════════════════════════
    //  Private Helpers
    // ═══════════════════════════════════════════

    /// <summary>
    /// Creates a raw .lnk shortcut at any path (for auto-start, desktop, etc.)
    /// Falls back to PowerShell when COM is disabled (e.g. in trimmed single-file builds).
    /// </summary>
    public static void CreateRawShortcut(string targetPath, string lnkPath, string description = "")
    {
        try
        {
            CreateLnkFile(lnkPath, targetPath,
                Path.GetDirectoryName(targetPath) ?? "", description, targetPath);
        }
        catch
        {
            // COM may fail on MTA threads, trimmed builds, or if WScript.Shell is unavailable.
            CreateLnkViaPowerShell(lnkPath, targetPath,
                Path.GetDirectoryName(targetPath) ?? "", description, targetPath);
        }
    }

    /// <summary>
    /// Creates a .lnk shortcut using PowerShell. Slower than COM but works when COM is trimmed.
    /// </summary>
    private static void CreateLnkViaPowerShell(string lnkPath, string targetPath,
        string workingDir, string description, string iconPath)
    {
        var escapedTarget = targetPath.Replace("'", "''");
        var escapedLnk = lnkPath.Replace("'", "''");
        var escapedWorkDir = workingDir.Replace("'", "''");
        var escapedDesc = description.Replace("'", "''");
        var escapedIcon = iconPath.Replace("'", "''");

        // WScript.Shell IconLocation only supports .ico/.exe/.dll, not .png/.jpg
        var ext = Path.GetExtension(iconPath).ToLowerInvariant();
        var iconLine = (ext is ".ico" or ".exe" or ".dll") && !string.IsNullOrEmpty(iconPath)
            ? $"$s.IconLocation = '{escapedIcon},0'; "
            : "";

        var ps = $"$s = (New-Object -ComObject WScript.Shell).CreateShortcut('{escapedLnk}'); " +
                 $"$s.TargetPath = '{escapedTarget}'; " +
                 $"$s.WorkingDirectory = '{escapedWorkDir}'; " +
                 $"$s.Description = '{escapedDesc}'; " +
                 iconLine +
                 "$s.Save()";

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{ps.Replace("\"", "\\\"")}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        using var proc = System.Diagnostics.Process.Start(psi);
        proc?.WaitForExit(10000);
    }

    private static string ResolveIconLocation(AppEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.IconPath) && File.Exists(entry.IconPath))
            return entry.IconPath;
        
        // Support .ico, .png, .jpg for icon files
        if (File.Exists(entry.TargetPath))
            return entry.TargetPath;
            
        return "";
    }

    /// <summary>
    /// Creates a .lnk file using WScript.Shell COM object (dynamic binding).
    /// </summary>
    private static void CreateLnkFile(string lnkPath, string targetPath, string workingDir, string description, string iconLocation)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new PlatformNotSupportedException("WScript.Shell COM object is not available on this system.");

        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            try
            {
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = workingDir;
                shortcut.Description = description;
                // Only set IconLocation for .ico/.exe/.dll — WScript.Shell doesn't support .png/.jpg
                var iconExt = Path.GetExtension(iconLocation).ToLowerInvariant();
                if (!string.IsNullOrEmpty(iconLocation) && iconExt is ".ico" or ".exe" or ".dll")
                    shortcut.IconLocation = $"{iconLocation},0";
                shortcut.Save();
            }
            finally
            {
                Marshal.ReleaseComObject(shortcut);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(shell);
        }
    }

    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    private const uint WM_SETTINGCHANGE = 0x001A;

    [Flags]
    private enum SendMessageTimeoutFlags : uint
    {
        SMTO_ABORTIFHUNG = 0x0002
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, string lParam,
        SendMessageTimeoutFlags flags, uint timeout, out IntPtr result);
}
