using System;
using System.IO;

namespace SuperDucker.Shared.Native;

/// <summary>
/// Provides a cross-cutting helper for moving files to the Windows Recycle Bin.
/// Falls back to no-op if the Shell COM object is unavailable.
/// </summary>
public static class RecycleBinHelper
{
    /// <summary>
    /// Moves a file to the Recycle Bin using the Shell.Application COM object.
    /// The caller is responsible for ensuring the file exists and is accessible.
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
                // ssfBITBUCKET = -5 (Recycle Bin). MoveHere moves the file to the bin.
                shell.NameSpace(-5).InvokeMember("MoveHere",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null, shell, new object[] { filePath });
            }
        }
        catch
        {
            // Best-effort: recycle bin may not be available in all environments.
        }
    }
}
