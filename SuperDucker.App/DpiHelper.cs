using System.Windows;
using System.Windows.Media;

namespace SuperDucker.App;

/// <summary>
/// Helpers for DPI-aware rendering. Provides current monitor DPI scale
/// and methods to compute pixel sizes that look sharp on any display.
/// </summary>
public static class DpiHelper
{
    /// <summary>Default WPF DPI (96 DIP = 1 inch).</summary>
    public const double DefaultDpi = 96.0;

    /// <summary>
    /// Gets the DPI scale factor for the given visual (or the primary screen if null).
    /// Returns 1.0 for 100% scaling, 1.5 for 150%, 2.0 for 200%, etc.
    /// </summary>
    public static double GetScale(Visual? visual = null)
    {
        if (visual != null)
        {
            var dpi = VisualTreeHelper.GetDpi(visual);
            return dpi.DpiScaleX;
        }

        // Fallback: try the primary monitor
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
    /// Gets the actual DPI value (not just scale) for the given visual.
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
    /// Converts a DIP size to actual pixel size for the current DPI.
    /// Use this when creating RenderTargetBitmap to ensure sharp rendering.
    /// </summary>
    public static int ScalePixel(double dipSize, Visual? visual = null)
    {
        return (int)Math.Ceiling(dipSize * GetScale(visual));
    }

    /// <summary>
    /// Returns the best icon size to extract for the current DPI.
    /// On 100% scale returns 48, on 150% returns 64, on 200%+ returns 128.
    /// </summary>
    public static int GetIconExtractSize(Visual? visual = null)
    {
        var scale = GetScale(visual);
        if (scale >= 2.0) return 128;
        if (scale >= 1.5) return 64;
        return 48;
    }
}
