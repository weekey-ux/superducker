using System.Drawing;
using System.Runtime.InteropServices;

namespace SuperDucker.Shared.Data;

/// <summary>
/// 用于从可执行文件中提取图标的工具类。
/// 借助 Win32 的 PrivateExtractIcons 获取可用的最大尺寸图标，
/// 而非 Icon.ExtractAssociatedIcon 默认返回的小尺寸 16x16/32x32 图标。
/// </summary>
public static class IconHelper
{
    // ── Win32 P/Invoke 声明 ──────────────────────────────────────────

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

    /// <summary>按尺寸从大到小的候选列表（256 → 32），优先提取高分辨率图标。</summary>
    private static readonly int[] IconSizes = { 256, 128, 64, 48, 32 };

    /// <summary>
    /// 用 Win32 从 exe 提取指定尺寸的图标句柄（HICON）。
    /// 调用方负责在成功时调用方用完后以 <see cref="DestroyIcon"/> 释放返回的句柄。
    /// 成功返回 true 并将句柄写入 hIcon；失败返回 false（hIcon 为 Zero）。
    /// </summary>
    private static bool TryExtractHIcon(string exePath, int size, out IntPtr hIcon)
    {
        hIcon = IntPtr.Zero;
        try
        {
            var icons = new IntPtr[1];
            var ids = new uint[1];
            var count = PrivateExtractIconsW(exePath, 0, size, size, icons, ids, 1, LR_DEFAULTCOLOR);
            if (count > 0 && icons[0] != IntPtr.Zero)
            {
                hIcon = icons[0];
                return true;
            }
        }
        catch
        {
            // 该尺寸提取失败，调用方会尝试下一个尺寸
        }
        return false;
    }

    /// <summary>
    /// 从可执行文件中提取可用的最大尺寸图标并保存为 .ico 文件。
    /// 按尺寸从大到小依次尝试：256 → 128 → 64 → 48 → 32。
    /// 成功返回输出路径，失败返回 null。
    /// </summary>
    public static string? ExtractAndSaveIcon(string exePath, string outputPath)
    {
        foreach (var size in IconSizes)
        {
            if (TryExtractHIcon(exePath, size, out var hIcon))
            {
                try
                {
                    using var bitmap = Icon.FromHandle(hIcon).ToBitmap();
                    using var icon = BitmapToIcon(bitmap);
                    if (icon == null) continue;

                    using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                    icon.Save(fs);
                    return outputPath;
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
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
    /// 将 System.Drawing.Bitmap 转换为保留原始分辨率（不放大）的多尺寸 ICO。
    /// </summary>
    private static Icon? BitmapToIcon(Bitmap bmp)
    {
        // 创建与位图原始尺寸完全一致的 ICO —— 不做缩放
        var handle = bmp.GetHicon();
        return Icon.FromHandle(handle);
    }

    /// <summary>
    /// 从可执行文件提取图标，并以指定名称保存到 icons/ 目录。
    /// 成功返回完整路径，失败返回 null。
    /// </summary>
    public static string? ExtractToIconsDir(string exePath, string abbreviation)
    {
        var iconsDir = WebHelper.GetIconsDirectory();
        Directory.CreateDirectory(iconsDir);
        var outputPath = Path.Combine(iconsDir, $"{abbreviation.ToUpperInvariant()}.ico");
        return ExtractAndSaveIcon(exePath, outputPath);
    }

    /// <summary>
    /// 从可执行文件直接提取图标为内存中的 BitmapSource，不落盘。
    /// 按尺寸从大到小尝试：256 → 128 → 64 → 48 → 32。
    /// 返回的 BitmapSource 已 Freeze，WPF 线程安全。
    /// 成功返回 BitmapSource，失败返回 null。
    /// </summary>
    public static System.Windows.Media.Imaging.BitmapSource? ExtractToBitmapSource(string exePath)
    {
        foreach (var size in IconSizes)
        {
            if (TryExtractHIcon(exePath, size, out var hIcon))
            {
                try
                {
                    using var bitmap = Icon.FromHandle(hIcon).ToBitmap();
                    var handle = bitmap.GetHicon();
                    try
                    {
                        var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            handle,
                            System.Windows.Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                        src.Freeze();
                        return src;
                    }
                    finally
                    {
                        DestroyIcon(handle);
                    }
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }
        return null;
    }
}
