using System.Runtime.InteropServices;
using SystemCore.Infrastructure.Cleaner;
using SystemCore.Infrastructure.Uninstaller;

namespace SystemCore.Infrastructure.Startup;

public static class StartupFileLocation
{
    public static bool Open(string executable)
    {
        if (!UninstallCommandParser.IsSafeExecutable(executable)) return false;
        try
        {
            using var lease = PinnedCleanerPath.Open(executable, false, shareRead: true);
            var id = ILCreateFromPathW(executable);
            if (id == IntPtr.Zero) return false;
            try { return SHOpenFolderAndSelectItems(id, 0, IntPtr.Zero, 0) >= 0; }
            finally { Marshal.FreeCoTaskMem(id); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { return false; }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr ILCreateFromPathW(string path);
    [DllImport("shell32.dll")] private static extern int SHOpenFolderAndSelectItems(IntPtr folder, uint count, IntPtr children, uint flags);
}
