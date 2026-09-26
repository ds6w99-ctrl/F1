using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using SystemCore.Core.Uninstaller;

namespace SystemCore.Infrastructure.Uninstaller;

internal sealed class RegistryTransaction : SafeHandleZeroOrMinusOneIsInvalid
{
    private RegistryTransaction() : base(true) { }
    internal static RegistryTransaction Begin()
    {
        var transaction = CreateTransaction(IntPtr.Zero, IntPtr.Zero, 0, 0, 0, 30000, "SystemCore selected leftover registry deletion");
        if (transaction.IsInvalid) { var error = Marshal.GetLastWin32Error(); transaction.Dispose(); throw new Win32Exception(error); }
        return transaction;
    }
    internal void Delete(RegistryEvidence target)
    {
        using var root = RegistryKey.OpenBaseKey(target.Hive == InventoryHive.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine,
            target.View == InventoryView.Registry64 ? RegistryView.Registry64 : RegistryView.Registry32);
        foreach (var child in target.Tree.Keys.OrderByDescending(k => k.Count(c => c == '\\')).ThenByDescending(k => k.Length))
        {
            var path = target.Path + (child.Length == 0 ? "" : "\\" + child);
            var error = RegDeleteKeyTransactedW(root.Handle, path, target.View == InventoryView.Registry64 ? 0x100 : 0x200, 0, this, IntPtr.Zero);
            if (error != 0) throw new Win32Exception(error);
        }
    }
    internal void Commit() { if (!CommitTransaction(this)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    // Closing an uncommitted transaction rolls it back. Never fall back to a
    // nontransactional tree delete if KTM is unavailable or reports a conflict.
    protected override bool ReleaseHandle() => CloseHandle(handle);
    [DllImport("KtmW32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern RegistryTransaction CreateTransaction(IntPtr security, IntPtr uow, uint options, uint isolationLevel, uint isolationFlags, uint timeout, string description);
    [DllImport("KtmW32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CommitTransaction(RegistryTransaction transaction);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegDeleteKeyTransactedW(SafeRegistryHandle root, string path, int view, int reserved, RegistryTransaction transaction, IntPtr extended);
}
