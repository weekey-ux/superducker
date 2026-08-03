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
    /// 获取宿主应用（即最终用户启动的 EXE）的版本号字符串（格式：主.次.修订）。
    /// 例如程序集版本 1.1.0.0 返回 "1.1.0"。
    ///
    /// 实现要点：直接调用 <see cref="Assembly.GetExecutingAssembly"/> 在类库中拿到的是
    /// <c>SuperDucker.Shared.dll</c> 自身的版本（默认 1.0.0），而不是调用方 App 的版本，
    /// 这会导致 UI 在 App 升级后仍显示旧版本号。
    ///
    /// 修复策略：优先取 <see cref="Assembly.GetEntryAssembly"/>（即进程的入口 EXE，
    /// 对 WPF 应用就是 SuperDucker.App.exe），托管测试或动态加载宿主为 null 时再回退
    /// 到调用方程序集。任何异常最后回退到字符串 "1.1.0"。
    /// </summary>
    public static string GetVersion()
    {
        try
        {
            // 1) 优先：进程的入口 EXE（WPF App = SuperDucker.App.exe）
            var entry = Assembly.GetEntryAssembly();
            var info = entry?.GetName().Version;
            if (info != null)
            {
                return $"{info.Major}.{info.Minor}.{info.Build}";
            }

            // 2) 回退：调用方程序集（单元测试 / 非标准宿主）
            var calling = Assembly.GetCallingAssembly();
            info = calling?.GetName().Version;
            if (info != null)
            {
                return $"{info.Major}.{info.Minor}.{info.Build}";
            }
        }
        catch
        {
            // 任何反射异常都吞掉，避免 UI 加载版本时崩溃
        }

        return "1.1.0";
    }
}
