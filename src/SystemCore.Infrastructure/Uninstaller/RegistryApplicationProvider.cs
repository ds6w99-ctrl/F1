using Microsoft.Win32;
using System.Globalization;
using SystemCore.Core.Uninstaller;

namespace SystemCore.Infrastructure.Uninstaller;

public sealed class RegistryApplicationProvider(IApplicationIdentityResolver identity) : IWin32ApplicationProvider
{
    public const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    private static readonly string[] Fields = ["DisplayName", "Publisher", "DisplayVersion", "InstallDate", "InstallLocation", "EstimatedSize", "DisplayIcon", "UninstallString", "QuietUninstallString", "WindowsInstaller", "SystemComponent", "NoRemove", "ParentKeyName", "ReleaseType"];
    public Task<InventoryResult> ReadAsync(CancellationToken token) => Task.Run(() =>
    {
        var rows = new List<InstalledApplication>();
        var unavailable = new List<string>();
        var retainedCharacters = 0;
        foreach (var hive in Enum.GetValues<InventoryHive>())
        foreach (var view in Enum.GetValues<InventoryView>())
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive == InventoryHive.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine,
                    view == InventoryView.Registry64 ? RegistryView.Registry64 : RegistryView.Registry32);
                using var root = baseKey.OpenSubKey(UninstallRoot, false);
                if (root is null) continue;
                var names = BoundedRegistry.Names(root, false, 5000, out var complete);
                if (!complete) unavailable.Add($"{hive}/{view}:limit");
                foreach (var name in names)
                {
                    token.ThrowIfCancellationRequested();
                    if (rows.Count >= 10000 || retainedCharacters >= 8 * 1024 * 1024) { unavailable.Add("InventoryLimit"); break; }
                    try
                    {
                        using var key = root.OpenSubKey(name, false);
                        if (key is null) continue;
                        var values = Fields.Select(n => new KeyValuePair<string, object?>(n, BoundedRegistry.ReadValue(key, n)))
                            .Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
                        retainedCharacters += values.Values.OfType<string>().Sum(v => v.Length);
                        if (retainedCharacters > 8 * 1024 * 1024) { unavailable.Add("InventoryLimit"); break; }
                        var row = Normalize(new(hive, view, name), values, identity);
                        if (row is not null) rows.Add(row);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { unavailable.Add($"{hive}/{view}:entry"); }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { unavailable.Add($"{hive}/{view}"); }
        }
        return new InventoryResult(rows.AsReadOnly(), unavailable.Count == 0, unavailable.Distinct().ToArray());
    }, token);

    public static InstalledApplication? Normalize(RegistryApplicationSource source, IReadOnlyDictionary<string, object?> values, IApplicationIdentityResolver identity)
    {
        string? Text(string name) => values.TryGetValue(name, out var value) && value is string s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
        // Unexpected flag types are not evidence that a restriction is absent.
        bool Flag(string name) => values.TryGetValue(name, out var value) && (value is not int i || i != 0);
        var name = Text("DisplayName");
        if (name is null) return null;
        var msi = Flag("WindowsInstaller");
        Guid? code = msi && Guid.TryParse(source.KeyName, out var parsed) && parsed != Guid.Empty ? parsed : null;
        var system = Flag("SystemComponent");
        var protectedEntry = system || Flag("NoRemove") || Text("ParentKeyName") is not null || Text("ReleaseType") is not null;
        var command = Text("UninstallString");
        var type = code is not null ? ApplicationType.Msi : ApplicationType.Win32;
        var canUninstall = !protectedEntry && !msi && UninstallCommandParser.Parse(command) is not null;
        DateOnly? date = DateOnly.TryParseExact(Text("InstallDate"), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        long? size = values.TryGetValue("EstimatedSize", out var raw) && raw is int sizeKiB && sizeKiB >= 0 ? (long)sizeKiB * 1024 : null;
        return new(identity.Resolve(type, source, code, null), name, Text("Publisher"), Text("DisplayVersion"), date,
            Text("InstallLocation"), size, null, type, command, Text("QuietUninstallString") is not null, code, null, null, source,
            Text("DisplayIcon"), system, false, protectedEntry, canUninstall,
            canUninstall ? null : protectedEntry ? "Protected registration" : msi ? "MSI registration requires Windows Installer verification" : "Native uninstaller unavailable");
    }
}
