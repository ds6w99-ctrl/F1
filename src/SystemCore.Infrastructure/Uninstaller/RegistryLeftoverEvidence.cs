using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using SystemCore.Core.Uninstaller;

namespace SystemCore.Infrastructure.Uninstaller;

internal sealed record RegistryTreeValue(string Key, string Name, RegistryValueKind Kind, object Value);
internal sealed record RegistryKeyMetadata(string Key, long LastWriteFileTime);
internal sealed record RegistryTree(string[] Keys, RegistryTreeValue[] Values, RegistryKeyMetadata[] Metadata);
internal sealed record RegistryEvidence(InventoryHive Hive, InventoryView View, string Path, string Fingerprint, RegistryTree Tree);

internal static class RegistryLeftoverEvidence
{
    internal static RegistryKey Open(InventoryHive hive, InventoryView view, string path, RegistryTransaction? transaction = null)
    {
        using var root = RegistryKey.OpenBaseKey(hive == InventoryHive.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine,
            view == InventoryView.Registry64 ? RegistryView.Registry64 : RegistryView.Registry32);
        var parts = path.Split('\\');
        if (parts.Length < 2 || !parts[0].Equals("Software", StringComparison.OrdinalIgnoreCase)
            || parts.Skip(1).Any(p => !LeftoverSafety.SafeRegistryName(p))) throw new IOException("Registry path rejected");
        var ancestors = new List<RegistryKey>();
        try
        {
            foreach (var part in parts)
            {
                var parent = ancestors.Count == 0 ? root : ancestors[^1];
                var access = 0x20019 | (view == InventoryView.Registry64 ? 0x100 : 0x200);
                SafeRegistryHandle handle;
                // OPEN_LINK applies only to the final component, so open and check
                // every component relative to its already-opened parent handle.
                var result = transaction is null ? RegOpenKeyExW(parent.Handle, part, 8, access, out handle)
                    : RegOpenKeyTransactedW(parent.Handle, part, 8, access, out handle, transaction, IntPtr.Zero);
                if (result != 0) { handle?.Dispose(); throw new System.ComponentModel.Win32Exception(result); }
                var key = RegistryKey.FromHandle(handle, view == InventoryView.Registry64 ? RegistryView.Registry64 : RegistryView.Registry32);
                ancestors.Add(key);
                if (BoundedRegistry.HasValue(key, "SymbolicLinkValue")) throw new IOException("Registry link rejected");
            }
            var leaf = ancestors[^1]; ancestors.RemoveAt(ancestors.Count - 1);
            return leaf;
        }
        finally { for (var index = ancestors.Count - 1; index >= 0; index--) ancestors[index].Dispose(); }
    }
    internal static RegistryEvidence Capture(InventoryHive hive, InventoryView view, string path, RegistryTransaction? transaction = null)
    {
        var keys = new List<string>(); var values = new List<RegistryTreeValue>(); var metadata = new List<RegistryKeyMetadata>(); var bytes = 0;
        Visit(path, "", 0);
        var tree = new RegistryTree(keys.ToArray(), values.ToArray(), metadata.ToArray());
        var serialized = JsonSerializer.SerializeToUtf8Bytes(tree);
        if (serialized.Length > 1024 * 1024) throw new IOException("Registry snapshot too large");
        return new(hive, view, path, Convert.ToHexString(SHA256.HashData(serialized)), tree);
        void Visit(string absolute, string relative, int depth)
        {
            if (depth > 8 || keys.Count >= 256 || values.Count >= 1024) throw new IOException("Registry snapshot limit reached");
            using var key = Open(hive, view, absolute, transaction);
            keys.Add(relative);
            var error = RegQueryInfoKeyW(key.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out var lastWrite);
            if (error != 0) throw new System.ComponentModel.Win32Exception(error);
            metadata.Add(new(relative, lastWrite));
            var names = BoundedRegistry.Names(key, true, 1024, out var complete);
            if (!complete) throw new IOException("Registry value limit");
            foreach (var name in names.Order(StringComparer.Ordinal))
            {
                var kind = key.GetValueKind(name);
                if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString or RegistryValueKind.MultiString or RegistryValueKind.DWord or RegistryValueKind.QWord or RegistryValueKind.Binary)) throw new IOException("Unsupported registry type");
                var value = BoundedRegistry.ReadValue(key, name) ?? throw new IOException("Changed registry value");
                bytes = checked(bytes + JsonSerializer.SerializeToUtf8Bytes(value).Length);
                if (bytes > 1024 * 1024 || values.Count >= 1024) throw new IOException("Registry snapshot limit reached");
                values.Add(new(relative, name, kind, value));
            }
            var children = BoundedRegistry.Names(key, false, 256, out complete);
            if (!complete) throw new IOException("Registry child limit");
            foreach (var child in children.Order(StringComparer.Ordinal))
            {
                if (!LeftoverSafety.SafeRegistryName(child)) throw new IOException("Protected or ambiguous child key");
                Visit(absolute + "\\" + child, relative.Length == 0 ? child : relative + "\\" + child, depth + 1);
            }
        }
    }
    internal static bool Matches(InstalledApplication app, RegistryKey key)
    {
        var publisher = key.GetValue("Publisher", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        if (string.IsNullOrWhiteSpace(app.Publisher) || !string.Equals(publisher, app.Publisher, StringComparison.OrdinalIgnoreCase)) return false;
        if (app.ProductCode is { } code && Guid.TryParse(key.GetValue("ProductCode") as string, out var actual) && actual == code) return true;
        if (app.PackageFamilyName is { } family && string.Equals(key.GetValue("PackageFamilyName") as string, family, StringComparison.Ordinal)) return true;
        return app.InstallLocation is { } install && SystemCore.Core.Cleaner.CleanerSafety.Canonical(install) is { } canonical
            && string.Equals(SystemCore.Core.Cleaner.CleanerSafety.Canonical(key.GetValue("InstallLocation") as string), canonical, StringComparison.OrdinalIgnoreCase);
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegOpenKeyExW(SafeRegistryHandle key, string subkey, int options, int access, out SafeRegistryHandle result);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegOpenKeyTransactedW(SafeRegistryHandle key, string subkey, int options, int access, out SafeRegistryHandle result, RegistryTransaction transaction, IntPtr extended);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegQueryInfoKeyW(SafeRegistryHandle key, IntPtr keyClass, IntPtr classLength, IntPtr reserved,
        IntPtr subkeys, IntPtr maxSubkeyLength, IntPtr maxClassLength, IntPtr values, IntPtr maxValueNameLength, IntPtr maxValueLength, IntPtr securityLength, out long lastWrite);
}
