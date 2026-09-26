using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace SystemCore.Infrastructure.Startup;

internal static class StartupTaskAccess
{
    // Task Scheduler documents file-write rights as the update permission. Query
    // its supported security descriptor API; never infer permission from RunLevel.
    // The helper evaluates the linked limited token too, preserving the requestor's
    // elevation classification across the UAC boundary.
    internal static bool? CanWrite(string descriptor, Action<string, int>? diagnostic = null)
    {
        if (descriptor.Length is 0 or > 65536) return null;
        using var identity = WindowsIdentity.GetCurrent();
        SafeAccessTokenHandle? limited = null; IntPtr security = IntPtr.Zero;
        try
        {
            var token = identity.AccessToken;
            if (!GetTokenInformation(token, 18, out int elevationType, sizeof(int), out _)) { diagnostic?.Invoke("ElevationType", Marshal.GetLastWin32Error()); return null; }
            if (elevationType == 2)
            {
                if (!GetLinkedToken(token, 19, out var linked, IntPtr.Size, out _)) { diagnostic?.Invoke("LinkedToken", Marshal.GetLastWin32Error()); return null; }
                limited = new SafeAccessTokenHandle(linked); token = limited;
            }
            // Identification is sufficient for AccessCheck and works with the
            // identification-level limited token returned by TokenLinkedToken.
            if (!DuplicateToken(token, 1, out var impersonation)) { diagnostic?.Invoke("DuplicateToken", Marshal.GetLastWin32Error()); return null; }
            using (impersonation)
            {
                if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(descriptor, 1, out security, out _)) { diagnostic?.Invoke("Descriptor", Marshal.GetLastWin32Error()); return null; }
                var mapping = new GenericMapping { Read = 0x120089, Write = 0x120116, Execute = 0x1200A0, All = 0x1F01FF };
                uint size = 1024; var privileges = new byte[size];
                if (!AccessCheck(security, impersonation, mapping.Write, ref mapping, privileges, ref size, out _, out var allowed)) { diagnostic?.Invoke("AccessCheck", Marshal.GetLastWin32Error()); return null; }
                return allowed;
            }
        }
        finally { limited?.Dispose(); if (security != IntPtr.Zero) LocalFree(security); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct GenericMapping { public uint Read, Write, Execute, All; }
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int kind, out int value, int size, out int returned);
    [DllImport("advapi32.dll", EntryPoint = "GetTokenInformation", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetLinkedToken(SafeAccessTokenHandle token, int kind, out IntPtr value, int size, out int returned);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DuplicateToken(SafeAccessTokenHandle token, int level, out SafeAccessTokenHandle result);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string text, uint revision, out IntPtr security, out uint size);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AccessCheck(IntPtr security, SafeAccessTokenHandle token, uint access, ref GenericMapping mapping, byte[] privileges, ref uint size, out uint granted, [MarshalAs(UnmanagedType.Bool)] out bool allowed);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
}
