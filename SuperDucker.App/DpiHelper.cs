using System.Windows;
using System.Windows.Media;

namespace SuperDucker.App;

/// <summary>
/// 用于 DPI 感知渲染的辅助类。提供当前显示器 DPI 缩放比例，
/// 以及计算在各显示器上都清晰锐利的像素尺寸的方法。
/// </summary>
public static class DpiHelper
{
    /// <summary>WPF 默认 DPI（96 DIP = 1 英寸）。</summary>
    public const double DefaultDpi = 96.0;

    /// <summary>
    /// 获取指定视觉元素（若为 null 则取主屏幕）的 DPI 缩放比例。
    /// 100% 缩放返回 1.0，150% 返回 1.5，200% 返回 2.0，以此类推。
    /// </summary>
    public static double GetScale(Visual? visual = null)
    {
        if (visual != null)
        {
            var dpi = VisualTreeHelper.GetDpi(visual);
            return dpi.DpiScaleX;
        }

        // 回退：尝试主显示器
        try
        {
            var dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow);
            return dpi.DpiScaleX;
        }
        catch
        {
            return 1.0;
        }
    }

    /// <summary>
    /// 获取指定视觉元素的实际 DPI 值（不仅是缩放比例）。
    /// </summary>
    public static double GetDpi(Visual? visual = null)
    {
        if (visual != null)
        {
            var dpi = VisualTreeHelper.GetDpi(visual);
            return dpi.PixelsPerDip * DefaultDpi;
        }

        try
        {
            var dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow);
            return dpi.PixelsPerDip * DefaultDpi;
        }
        catch
        {
            return DefaultDpi;
        }
    }

    /// <summary>
    /// 将 DIP（设备无关像素）尺寸转换为当前 DPI 下的实际像素尺寸。
    /// 创建 RenderTargetBitmap 时应使用本方法以保证渲染锐利。
    /// </summary>
    public static int ScalePixel(double dipSize, Visual? visual = null)
    {
        return (int)Math.Ceiling(dipSize * GetScale(visual));
    }

    /// <summary>
    /// 返回当前 DPI 下应提取的最佳图标尺寸。
    /// 100% 缩放返回 48，150% 返回 64，200% 及以上返回 128。
    /// </summary>
    public static int GetIconExtractSize(Visual? visual = null)
    {
        var scale = GetScale(visual);
        if (scale >= 2.0) return 128;
        if (scale >= 1.5) return 64;
        return 48;
    }
}
