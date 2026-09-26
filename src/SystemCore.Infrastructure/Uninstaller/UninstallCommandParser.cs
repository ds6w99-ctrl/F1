using System.Runtime.InteropServices;
using SystemCore.Core.Uninstaller;

namespace SystemCore.Infrastructure.Uninstaller;

// Fail closed on ambiguous executable tokens. Windows interprets argument quotes;
// no command shell or PATH-based executable resolution is used.
public static class UninstallCommandParser
{
    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
    { "cmd.exe", "powershell.exe", "pwsh.exe", "wmic.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe", "reg.exe", "msiexec.exe" };

    public static UninstallCommand? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32767 || value.IndexOfAny(['\0', '\r', '\n', '|', '&', '<', '>']) >= 0) return null;
        var text = value.Trim();
        var quoteCount = text.Count(c => c == '"');
        if (quoteCount % 2 != 0) return null;
        var pointer = CommandLineToArgvW(text, out var count);
        if (pointer == IntPtr.Zero) return null;
        try
        {
            if (count < 1) return null;
            var parts = Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size)) ?? "").ToArray();
            var executable = Environment.ExpandEnvironmentVariables(parts[0]);
            if (!IsSafeExecutable(executable)) return null;
            return new(executable, Array.AsReadOnly(parts[1..]));
        }
        finally { LocalFree(pointer); }
    }

    public static bool IsSafeExecutable(string? path)
    {
        if (path is null || path.Length < 4 || path.Length > 240 || path.Any(char.IsControl) || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\'
            || path[2..].Contains(':') || path.Contains('/') || path.Contains('%') || path.Contains('"')
            || path.Split('\\').Any(s => s is "." or ".." || s.EndsWith(' ') || s.EndsWith('.'))
            || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || Forbidden.Contains(Path.GetFileName(path))) return false;
        try
        {
            if (!File.Exists(path) || !string.Equals(Path.GetFullPath(path), path, StringComparison.OrdinalIgnoreCase)) return false;
            for (var node = path; node is not null; node = Path.GetDirectoryName(node))
                if ((File.GetAttributes(node) & FileAttributes.ReparsePoint) != 0) return false;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
}
