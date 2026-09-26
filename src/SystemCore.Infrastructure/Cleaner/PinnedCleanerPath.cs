using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SystemCore.Core.Cleaner;

namespace SystemCore.Infrastructure.Cleaner;

// Pins every directory ancestor against rename/deletion, opens the final object without
// following reparses, and checks the handle's resolved name. Delete access was added only
// after the recorded Phase 4A safety gate; scanning never requests it.
internal sealed class PinnedCleanerPath : IDisposable
{
    private readonly List<SafeFileHandle> handles = [];
    public SafeFileHandle Leaf => handles[^1];
    public CleanerIdentity Identity { get; private set; } = null!;
    public uint Attributes { get; private set; }
    private PinnedCleanerPath() { }
    public static PinnedCleanerPath Open(string path, bool directory, bool deleteAccess = false, bool shareRead = false, bool namespaceWrite = false)
    {
        if (deleteAccess && shareRead) throw new ArgumentException("Mutation leases must be exclusive");
        if (TryOpenCore(path, directory, deleteAccess, shareRead, namespaceWrite, out var lease, out var failure, out var error)) return lease;
        throw failure switch { PinFailure.Native => new Win32Exception(error), PinFailure.Reparse => new ReparseRejectedException(), _ => new IOException("Path lease rejected: " + failure) };
    }
    // Scanning encounters locked/inaccessible objects routinely. The same handle
    // validations are used, but expected refusals are returned instead of thrown.
    internal static bool TryOpenRead(string path, bool directory, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PinnedCleanerPath? lease, out PinFailure failure)
        => TryOpenCore(path, directory, false, false, false, out lease, out failure, out _);
    private static bool TryOpenCore(string path, bool directory, bool deleteAccess, bool shareRead, bool namespaceWrite,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PinnedCleanerPath? result, out PinFailure failure, out int error)
    {
        result = null; error = 0; failure = PinFailure.UnsafePath;
        var canonical = CleanerSafety.Canonical(path);
        if (canonical is null) return false;
        if (new DriveInfo(canonical[..3]).DriveType != DriveType.Fixed) { failure = PinFailure.NonLocalDrive; return false; }
        var lease = new PinnedCleanerPath();
        try
        {
            var parts = canonical[3..].Split('\\');
            var current = canonical[..3];
            if (!lease.Add(current, true, false, false, namespaceWrite, out failure, out error)) return false;
            for (var i = 0; i < parts.Length; i++)
            {
                current = Path.Combine(current, parts[i]);
                if (!lease.Add(current, i < parts.Length - 1 || directory, deleteAccess && i == parts.Length - 1, shareRead, namespaceWrite, out failure, out error)) return false;
            }
            result = lease; failure = PinFailure.None; return true;
        }
        finally { if (result is null) lease.Dispose(); }
    }
    private bool Add(string path, bool directory, bool deleteAccess, bool shareRead, bool namespaceWrite, out PinFailure failure, out int error)
    {
        failure = PinFailure.Native; error = 0;
        // READ_ATTRIBUTES alone bypasses sharing checks. Include read-data/list-directory
        // access so ancestor leases deny both write/reparse mutation and rename/deletion.
        // Child namespace changes remain possible, but every child gets its own fresh lease.
        var handle = CreateFile(path, 0x80000080u | (deleteAccess ? 0x10000u : 0u), directory && namespaceWrite ? 3u : directory || shareRead ? 1u : 0u, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid) { error = Marshal.GetLastWin32Error(); handle.Dispose(); return false; }
        handles.Add(handle);
        if (!GetFileInformationByHandle(handle, out var info)) { error = Marshal.GetLastWin32Error(); return false; }
        if ((info.Attributes & 0x400) != 0) { failure = PinFailure.Reparse; return false; }
        if (((info.Attributes & 0x10) != 0) != directory) { failure = PinFailure.ObjectType; return false; }
        failure = PinFailure.FinalPath;
        var final = new StringBuilder(512);
        var length = GetFinalPathNameByHandle(handle, final, (uint)final.Capacity, 0);
        if (length == 0 || length >= final.Capacity) return false;
        var resolved = final.ToString();
        if (!resolved.StartsWith(@"\\?\", StringComparison.Ordinal)) return false;
        if (!string.Equals(resolved[4..].TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
        failure = PinFailure.InvalidMetadata;
        if (info.SizeHigh > int.MaxValue || info.WriteHigh > int.MaxValue || info.CreationHigh > int.MaxValue) return false;
        Attributes = info.Attributes;
        Identity = new(info.Volume, ((ulong)info.IndexHigh << 32) | info.IndexLow,
            checked((long)(((ulong)info.SizeHigh << 32) | info.SizeLow)),
            checked((long)(((ulong)info.WriteHigh << 32) | info.WriteLow)), info.Links,
            checked((long)(((ulong)info.CreationHigh << 32) | info.CreationLow)));
        failure = PinFailure.None; return true;
    }
    public void Dispose() { for (var i = handles.Count - 1; i >= 0; i--) handles[i].Dispose(); handles.Clear(); }
    // FileDispositionInfo deletes the exact exclusively opened object, not a name looked up
    // again after validation. No POSIX/ignore-readonly flags and no delete-on-reboot fallback.
    internal void MarkFileForDeletion()
    {
        if (!CleanerSafety.SafeFileAttributes(Attributes, Identity.Links)) throw new IOException("Unsupported file");
        var disposition = 1;
        if (!SetFileInformationByHandle(Leaf, 4, ref disposition, sizeof(int)))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    internal void MarkEmptyDirectoryForDeletion()
    {
        if ((Attributes & 0x410) != 0x10) throw new IOException("Unsafe directory");
        var disposition = 1;
        // Windows rejects a nonempty directory; no recursive or POSIX deletion.
        if (!SetFileInformationByHandle(Leaf, 4, ref disposition, sizeof(int))) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref int information, int length);
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfo info);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
}
internal sealed class ReparseRejectedException : IOException { }
internal enum PinFailure { None, UnsafePath, NonLocalDrive, Native, Reparse, ObjectType, FinalPath, InvalidMetadata }
