using System.Drawing;
using System.Runtime.InteropServices;

namespace SuperDucker.Shared.Data;

/// <summary>
/// Utility for extracting icons from executable files.
/// Uses Win32 PrivateExtractIcons to retrieve the largest available icon
/// instead of the tiny 16x16/32x32 that Icon.ExtractAssociatedIcon returns.
/// </summary>
public static class IconHelper
{
    // ── Win32 P/Invoke ──────────────────────────────────────────

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint PrivateExtractIconsW(
        string szFileName,
        int nIconIndex,
        int cxIcon,
        int cyIcon,
        [Out] IntPtr[] phicon,
        [Out] uint[] piconid,
        int nIcons,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint LR_DEFAULTCOLOR = 0x00000000;

    /// <summary>
    /// Extract the largest available icon from an executable file and save it as .ico.
    /// Tries sizes from large to small: 256 → 128 → 64 → 48 → 32.
    /// Returns the output path on success, null on failure.
    /// </summary>
    public static string? ExtractAndSaveIcon(string exePath, string outputPath)
    {
        // Try each size from largest to smallest
        int[] sizes = { 256, 128, 64, 48, 32 };

        foreach (var size in sizes)
        {
            try
            {
                var icons = new IntPtr[1];
                var ids = new uint[1];
                var count = PrivateExtractIconsW(exePath, 0, size, size, icons, ids, 1, LR_DEFAULTCOLOR);

                if (count > 0 && icons[0] != IntPtr.Zero)
                {
                    try
                    {
                        using var bitmap = Icon.FromHandle(icons[0]).ToBitmap();
                        using var icon = BitmapToIcon(bitmap);
                        if (icon == null) continue;

                        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                        icon.Save(fs);
                        return outputPath;
                    }
                    finally
                    {
                        DestroyIcon(icons[0]);
                    }
                }
            }
            catch
            {
                // Try next size
            }
        }

        // Final fallback: use the old .NET method (always returns small icon)
        try
        {
            using var fallbackIcon = Icon.ExtractAssociatedIcon(exePath);
            if (fallbackIcon != null)
            {
                using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                fallbackIcon.Save(fs);
                return outputPath;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Convert a System.Drawing.Bitmap to a multi-size ICO that preserves
    /// the original resolution (no upscaling).
    /// </summary>
    private static Icon? BitmapToIcon(Bitmap bmp)
    {
        // Create an ICO with the exact bitmap size — no resizing
        var handle = bmp.GetHicon();
        return Icon.FromHandle(handle);
    }

    /// <summary>
    /// Extract icon from exe and save to the icons/ directory with the given name.
    /// Returns the full path on success, null on failure.
    /// </summary>
    public static string? ExtractToIconsDir(string exePath, string abbreviation)
    {
        var iconsDir = WebHelper.GetIconsDirectory();
        Directory.CreateDirectory(iconsDir);
        var outputPath = Path.Combine(iconsDir, $"{abbreviation.ToUpperInvariant()}.ico");
        return ExtractAndSaveIcon(exePath, outputPath);
    }
}
