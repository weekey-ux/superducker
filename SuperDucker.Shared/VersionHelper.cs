using System.Reflection;

namespace SuperDucker.Shared;

/// <summary>
/// 程序版本信息辅助类。
/// 集中读取程序集版本，避免在多处重复编写程序集反射代码，
/// 并作为 GUI 标题栏与 CLI 帮助信息统一获取版本的入口。
/// </summary>
public static class VersionHelper
{
    /// <summary>
    /// 获取当前执行程序集的版本号字符串（格式：主.次.修订，不含构建号）。
    /// 例如程序集版本 1.0.0.0 返回 "1.0.0"。
    /// 当无法读取版本时回退为 "1.0.0"。
    /// </summary>
    public static string GetVersion()
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.0.0";
    }
}
