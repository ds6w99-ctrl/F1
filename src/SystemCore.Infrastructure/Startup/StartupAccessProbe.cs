using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace SystemCore.Infrastructure.Startup;

// AccessCheck against the actual current/linked elevated token. Merely having a
// helper is not evidence that an ACL permits a write. This never changes an ACL.
internal static class StartupAccessProbe
{
    internal static bool Allows(SafeHandle handle, bool registry, uint access, bool elevated)
    {
        using var identity = WindowsIdentity.GetCurrent();
        SafeAccessTokenHandle? linked = null; IntPtr descriptor = IntPtr.Zero;
        try
        {
            var token = identity.AccessToken;
            if (elevated)
            {
                if (!GetTokenInformation(token, 18, out int type, 4, out _)) return false;
                if (type == 3)
                {
                    if (!GetLinkedToken(token, 19, out var full, IntPtr.Size, out _)) return false;
                    linked = new(full); token = linked;
                }
                else if (type != 2 && !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return false;
            }
            if (GetSecurityInfo(handle, registry ? 4 : 1, 7, out _, out _, out _, out _, out descriptor) != 0) return false;
            if (!DuplicateToken(token, 1, out var impersonation)) return false;
            using (impersonation)
            {
                var mapping = registry ? new Mapping { Read = 0x20019, Write = 0x20006, Execute = 0x20019, All = 0xF003F }
                    : new Mapping { Read = 0x120089, Write = 0x120116, Execute = 0x1200A0, All = 0x1F01FF };
                uint size = 1024; var privileges = new byte[size];
                return AccessCheck(descriptor, impersonation, access, ref mapping, privileges, ref size, out _, out var allowed) && allowed;
            }
        }
        finally { linked?.Dispose(); if (descriptor != IntPtr.Zero) LocalFree(descriptor); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Mapping { public uint Read, Write, Execute, All; }
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int kind, out int value, int size, out int returned);
    [DllImport("advapi32.dll", EntryPoint = "GetTokenInformation", SetLastError = true)] private static extern bool GetLinkedToken(SafeAccessTokenHandle token, int kind, out IntPtr value, int size, out int returned);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DuplicateToken(SafeAccessTokenHandle token, int level, out SafeAccessTokenHandle result);
    [DllImport("advapi32.dll")] private static extern uint GetSecurityInfo(SafeHandle handle, int type, uint information, out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr descriptor);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AccessCheck(IntPtr descriptor, SafeAccessTokenHandle token, uint access, ref Mapping mapping, byte[] privileges, ref uint size, out uint granted, [MarshalAs(UnmanagedType.Bool)] out bool allowed);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
}
