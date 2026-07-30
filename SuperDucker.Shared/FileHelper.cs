namespace SuperDucker.Shared;

/// <summary>
/// 文件相关通用辅助方法。
/// </summary>
public static class FileHelper
{
    /// <summary>
    /// 将字节数转换为带单位的可读字符串（自动选择 B / KB / MB / GB）。
    /// 例如 1536 返回 "1.5 KB"，用于在 UI 与 CLI 中统一展示文件大小。
    /// </summary>
    /// <param name="bytes">字节数。</param>
    /// <returns>形如 "1.5 KB" 的字符串。</returns>
    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:F1} {units[unit]}";
    }
}
