using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using SystemCore.Infrastructure.Uninstaller;

namespace SystemCore.Infrastructure.Startup;

internal static class StartupRegistryAccess
{
    internal static RegistryKey Open(StartupRegistryLocation location, RegistryTransaction? transaction = null, bool write = false)
    {
        using var root = RegistryKey.OpenBaseKey(location.Hive, location.View);
        // RegistryKey.Handle on a predefined hive can request write access. Open a
        // read-only root handle explicitly so HKLM inventory works without UAC.
        using var readRoot = root.OpenSubKey("", false) ?? throw new IOException("Registry root unavailable");
        var handles = new List<RegistryKey>();
        try
        {
            var parts = location.KeyPath.Split('\\');
            foreach (var part in parts)
            {
                if (part.Length == 0 || part is "." or ".." || part.Contains('/') || part.Any(char.IsControl)) throw new IOException("Unsafe registry identity");
                var parent = handles.Count == 0 ? readRoot : handles[^1];
                var access = 0x20019 | (write && handles.Count == parts.Length - 1 ? 2 : 0) | (location.View == RegistryView.Registry64 ? 0x100 : 0x200);
                SafeRegistryHandle handle;
                var error = transaction is null ? RegOpenKeyExW(parent.Handle, part, 8, access, out handle)
                    : RegOpenKeyTransactedW(parent.Handle, part, 8, access, out handle, transaction, IntPtr.Zero);
                if (error != 0) { handle.Dispose(); throw new Win32Exception(error); }
                var key = RegistryKey.FromHandle(handle, location.View); handles.Add(key);
                if (BoundedRegistry.HasValue(key, "SymbolicLinkValue")) throw new IOException("Registry link rejected");
            }
            var result = handles[^1]; handles.RemoveAt(handles.Count - 1); return result;
        }
        finally { foreach (var key in handles.AsEnumerable().Reverse()) key.Dispose(); }
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegOpenKeyExW(SafeRegistryHandle key, string path, int options, int access, out SafeRegistryHandle result);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegOpenKeyTransactedW(SafeRegistryHandle key, string path, int options, int access, out SafeRegistryHandle result, RegistryTransaction transaction, IntPtr extended);
    internal static byte[] ReadBytes(RegistryKey key, string name, RegistryValueKind expected)
    {
        var length = 0;
        var error = RegQueryValueExW(key.Handle, name, IntPtr.Zero, out var kind, null, ref length);
        if (error != 0) throw new Win32Exception(error);
        if (kind != (int)expected || length is < 0 or > 65536) throw new IOException("Unsupported value");
        var data = new byte[length];
        error = RegQueryValueExW(key.Handle, name, IntPtr.Zero, out kind, data, ref length);
        if (error != 0) throw new Win32Exception(error);
        if (kind != (int)expected || length != data.Length) throw new IOException("Changed value");
        return data;
    }
    internal static void Remove(RegistryKey key, string name)
    { var error = RegDeleteValueW(key.Handle, name); if (error != 0) throw new Win32Exception(error); }
    internal static void Restore(RegistryKey key, string name, RegistryValueKind kind, byte[] data)
    { var error = RegSetValueExW(key.Handle, name, 0, (int)kind, data, data.Length); if (error != 0) throw new Win32Exception(error); }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegQueryValueExW(SafeRegistryHandle key, string name, IntPtr reserved, out int kind, byte[]? data, ref int length);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegDeleteValueW(SafeRegistryHandle key, string name);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegSetValueExW(SafeRegistryHandle key, string name, int reserved, int kind, byte[] data, int length);
}
