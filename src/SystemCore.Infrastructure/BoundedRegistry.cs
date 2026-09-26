using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

namespace SystemCore.Infrastructure;

// Query lengths before allocation. Never materialize arbitrary Registry value bags
// or unbounded name arrays. Returned status codes are authoritative (not LastError).
internal static class BoundedRegistry
{
    private static readonly UnicodeEncoding Unicode = new(false, false, true);
    internal static bool HasValue(RegistryKey key, string name)
    {
        uint size = 0;
        var status = RegQueryValueExW(key.Handle, name, IntPtr.Zero, out _, null, ref size);
        if (status == 2) return false;
        if (status is 0 or 234) return true;
        throw new IOException("Registry value presence unavailable");
    }
    internal static object? ReadValue(RegistryKey key, string name, int maximumBytes = 65536)
    {
        if (maximumBytes is < 8 or > 65536) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        uint size = 0;
        var status = RegQueryValueExW(key.Handle, name, IntPtr.Zero, out var kind, null, ref size);
        if (status == 2) return null;
        if (status != 0 || size > maximumBytes) throw new IOException("Registry value unavailable or oversized");
        var data = new byte[size]; var expectedKind = kind; var expectedSize = size;
        status = RegQueryValueExW(key.Handle, name, IntPtr.Zero, out kind, data, ref size);
        if (status != 0 || kind != expectedKind || size != expectedSize) throw new IOException("Registry value changed");
        if (kind == 4 && size == 4) return BitConverter.ToInt32(data);
        if (kind == 11 && size == 8) return BitConverter.ToInt64(data);
        if (kind == 3) return data;
        if (kind is 1 or 2 or 7 && size % 2 == 0)
        {
            try
            {
                var text = Unicode.GetString(data).TrimEnd('\0');
                return kind == 7 ? text.Split('\0') : text;
            }
            catch (DecoderFallbackException e) { throw new IOException("Invalid Registry text", e); }
        }
        throw new IOException("Unsupported Registry value type");
    }
    internal static string[] Names(RegistryKey key, bool values, int limit, out bool complete)
    {
        if (limit is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(limit));
        var result = new List<string>(); var characters = 0;
        var buffer = new StringBuilder(values ? 16384 : 256);
        for (uint index = 0; index <= limit; index++)
        {
            uint length = (uint)buffer.Capacity; buffer.Clear();
            var status = values ? RegEnumValueW(key.Handle, index, buffer, ref length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero)
                : RegEnumKeyExW(key.Handle, index, buffer, ref length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (status == 259) { complete = true; return result.ToArray(); }
            if (status != 0 || index == limit || characters + length > 1024 * 1024) { complete = false; return result.ToArray(); }
            var name = buffer.ToString(); result.Add(name); characters += name.Length;
        }
        complete = false; return result.ToArray();
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegQueryValueExW(SafeRegistryHandle key, string name, IntPtr reserved, out uint kind, byte[]? data, ref uint size);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegEnumValueW(SafeRegistryHandle key, uint index, StringBuilder name, ref uint size, IntPtr reserved, IntPtr kind, IntPtr data, IntPtr bytes);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegEnumKeyExW(SafeRegistryHandle key, uint index, StringBuilder name, ref uint size, IntPtr reserved, IntPtr keyClass, IntPtr classSize, IntPtr lastWrite);
}
