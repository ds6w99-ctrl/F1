using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SystemCore.Core.Cleaner;
using SystemCore.Infrastructure.Cleaner;

namespace SystemCore.Infrastructure.Startup;

internal static class StartupFileMove
{
    // Namespace mutation needs directory write sharing. Ancestors remain pinned
    // against rename/delete; all writes below are relative to validated directory
    // handles, never a subsequently resolved absolute destination path.
    internal static PinnedCleanerPath Open(string path, bool directory, bool deleteAccess = false) => PinnedCleanerPath.Open(path, directory, deleteAccess, namespaceWrite: true);
    internal static string Hash(PinnedCleanerPath file)
    {
        if (file.Identity.Size is < 0 or > 128 * 1024 * 1024 || !CleanerSafety.SafeFileAttributes(file.Attributes, file.Identity.Links)) throw new IOException("Unsupported startup file");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536]; long offset = 0;
        while (offset < file.Identity.Size)
        {
            var count = RandomAccess.Read(file.Leaf, buffer.AsSpan(0, (int)Math.Min(buffer.Length, file.Identity.Size - offset)), offset);
            if (count == 0) throw new IOException("File changed");
            hash.AppendData(buffer, 0, count); offset += count;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    internal static void Move(PinnedCleanerPath source, PinnedCleanerPath destinationDirectory, string name, bool replaceState = false)
    {
        if (source.Identity.Volume != destinationDirectory.Identity.Volume) throw new IOException("Cross-volume move rejected");
        MoveHandle(source.Leaf, destinationDirectory, name, replaceState);
    }
    internal static void MoveHandle(SafeFileHandle source, PinnedCleanerPath destinationDirectory, string name, bool replaceState)
    {
        if (name.Length == 0 || name != Path.GetFileName(name) || name.IndexOfAny(['\\', '/', ':']) >= 0
            || name is "." or ".." || name.EndsWith(' ') || name.EndsWith('.') || name.Any(char.IsControl)
            ) throw new IOException("Unsafe move");
        var bytes = Encoding.Unicode.GetBytes(name); var buffer = Marshal.AllocHGlobal(20 + bytes.Length);
        try
        {
            // x64 FILE_RENAME_INFO: ReplaceIfExists=false, destination pinned handle,
            // relative basename only. Windows atomically refuses an occupied slot.
            for (var index = 0; index < 20; index++) Marshal.WriteByte(buffer, index, 0);
            if (replaceState)
            {
                if (!name.EndsWith(".state", StringComparison.Ordinal) || !StartupStateService.ValidId(name[..^6])) throw new IOException("Replacement is limited to owned state blobs");
                Marshal.WriteByte(buffer, 0, 1);
            }
            Marshal.WriteIntPtr(buffer, 8, destinationDirectory.Leaf.DangerousGetHandle());
            Marshal.WriteInt32(buffer, 16, bytes.Length); Marshal.Copy(bytes, 0, buffer + 20, bytes.Length);
            // NtSetInformationFile supports a root-directory-relative rename;
            // the Win32 wrapper requires RootDirectory=NULL on this platform.
            var status = NtSetInformationFile(source, out _, buffer, (uint)(20 + bytes.Length), 10);
            if (status < 0) throw new Win32Exception((int)RtlNtStatusToDosError(status));
        }
        finally { Marshal.FreeHGlobal(buffer); GC.KeepAlive(destinationDirectory); }
    }
    internal static FileStream CreateChild(PinnedCleanerPath directory, string name, bool lockFile = false)
    {
        if (name != Path.GetFileName(name) || name.IndexOfAny(['\\', '/', ':']) >= 0 || (lockFile ? name != "operation.lock" : !name.EndsWith(".pending", StringComparison.Ordinal))) throw new IOException("Invalid store child");
        var text = Marshal.StringToHGlobalUni(name); var unicode = Marshal.AllocHGlobal(16);
        try
        {
            Marshal.WriteInt16(unicode, 0, checked((short)(name.Length * 2))); Marshal.WriteInt16(unicode, 2, checked((short)(name.Length * 2 + 2))); Marshal.WriteIntPtr(unicode, 8, text);
            var attributes = new ObjectAttributes { Length = Marshal.SizeOf<ObjectAttributes>(), Root = directory.Leaf.DangerousGetHandle(), Name = unicode, Attributes = 0x40 };
            var status = NtCreateFile(out var handle, 0xC0110080, ref attributes, out _, IntPtr.Zero, 0x80, 0, lockFile ? 3u : 2u, 0x00200060, IntPtr.Zero, 0);
            if (status < 0) { handle.Dispose(); throw new Win32Exception((int)RtlNtStatusToDosError(status)); }
            try
            {
                if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x410) != 0 || info.Links != 1) throw new IOException("Unsafe store child");
                return new FileStream(handle, FileAccess.ReadWrite);
            }
            catch { handle.Dispose(); throw; }
        }
        finally { Marshal.FreeHGlobal(unicode); Marshal.FreeHGlobal(text); GC.KeepAlive(directory); }
    }
    internal static void DiscardPending(SafeFileHandle pending)
    {
        // Only the newly created, still-exclusive pending-state handle is used.
        // Do not re-resolve a pathname to clean up a failed atomic state write.
        var disposition = 1;
        if (!SetFileInformationByHandle(pending, 4, ref disposition, sizeof(int))) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    [StructLayout(LayoutKind.Sequential)] private struct ObjectAttributes { public int Length; public IntPtr Root, Name; public uint Attributes; public IntPtr Security, Quality; }
    [StructLayout(LayoutKind.Sequential)] private struct FileInfo { public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
    [DllImport("ntdll.dll")] private static extern int NtCreateFile(out SafeFileHandle handle, uint access, ref ObjectAttributes attributes, out IoStatus status, IntPtr allocation, uint fileAttributes, uint share, uint disposition, uint options, IntPtr extended, uint extendedLength);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, ref int value, int length);
    [StructLayout(LayoutKind.Sequential)] private struct IoStatus { public IntPtr Status; public UIntPtr Information; }
    [DllImport("ntdll.dll")] private static extern int NtSetInformationFile(SafeFileHandle handle, out IoStatus status, IntPtr value, uint length, int informationClass);
    [DllImport("ntdll.dll")] private static extern uint RtlNtStatusToDosError(int status);
}
