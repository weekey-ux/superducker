using System;
using System.IO;

namespace SuperDucker.Shared.Native;

/// <summary>
/// 提供将文件移入 Windows 回收站的通用辅助方法。
/// 若 Shell COM 对象不可用，则静默退化为空操作（no-op）。
/// </summary>
public static class RecycleBinHelper
{
    /// <summary>
    /// 使用 Shell.Application COM 对象将文件移入回收站。
    /// 调用方需保证文件存在且可访问。
    /// </summary>
    public static void MoveToRecycleBin(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return;

            dynamic shell = Activator.CreateInstance(shellType)!;
            var folder = shell.NameSpace(Path.GetDirectoryName(filePath));
            var file = folder?.ParseName(Path.GetFileName(filePath));
            if (file != null)
            {
                // ssfBITBUCKET = -5（回收站）。MoveHere 将文件移入回收站。
                shell.NameSpace(-5).InvokeMember("MoveHere",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null, shell, new object[] { filePath });
            }
        }
        catch
        {
            // 尽力而为：回收站在某些环境中可能不可用，失败即静默忽略。
        }
    }
}
