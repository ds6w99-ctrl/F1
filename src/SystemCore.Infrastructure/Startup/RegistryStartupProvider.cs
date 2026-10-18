using Microsoft.Win32;
using SystemCore.Core.Startup;

namespace SystemCore.Infrastructure.Startup;

internal sealed record RegistryStartupSnapshot(StartupItem Item, StartupRegistryLocation Location, string ValueName, RegistryValueKind Kind, string Value);
public sealed class RegistryStartupProvider : IRegistryStartupProvider
{
    internal IStartupLocations Locations { get; }
    private readonly StartupSafetyClassifier safety = new();
    public RegistryStartupProvider() : this(new WindowsStartupLocations()) { }
    internal RegistryStartupProvider(IStartupLocations locations) => Locations = locations;
    // Read is synchronous; the offloaded lambda returns its result directly instead of blocking on a
    // task, so there is no `.Result` on an asynchronous operation here.
    public Task<StartupInventoryResult> ReadAsync(CancellationToken token) => Task.Run(() => Read(token).Result, token);
    internal (StartupInventoryResult Result, IReadOnlyList<RegistryStartupSnapshot> Snapshots) Read(CancellationToken token)
    {
        var snapshots = new List<RegistryStartupSnapshot>(); var errors = new List<string>();
        var retainedCharacters = 0;
        foreach (var source in Locations.Registry)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var key = StartupRegistryAccess.Open(source);
                var names = BoundedRegistry.Names(key, true, 2000, out var complete);
                if (!complete) errors.Add("RegistryLimit");
                foreach (var name in names)
                {
                    token.ThrowIfCancellationRequested();
                    var kind = key.GetValueKind(name);
                    var value = BoundedRegistry.ReadValue(key, name);
                    var raw = value as string;
                    retainedCharacters += name.Length + (raw?.Length ?? 0);
                    if (retainedCharacters > 4 * 1024 * 1024 || snapshots.Count >= 10000) { errors.Add("RegistryLimit"); break; }
                    var rawFingerprint = kind is RegistryValueKind.String or RegistryValueKind.ExpandString
                        ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(StartupRegistryAccess.ReadBytes(key, name, kind))) : "Unsupported";
                    var command = StartupCommandParser.Read(raw);
                    var identity = $"Registry:{source.Hive}:{source.View}:{source.KeyPath}:{name}";
                    var item = new StartupItem(new StartupIdentityResolver().Resolve(identity), name, command.Publisher, source.Type,
                        $"{source.Hive}/{source.View}\\{source.KeyPath}\\{name}", raw ?? "", command.Executable, command.Arguments,
                        StartupStatus.Enabled, source.Hive == RegistryHive.CurrentUser ? StartupScope.CurrentUser : StartupScope.AllUsers,
                        source.Hive == RegistryHive.LocalMachine, false, raw is null || raw.Length > 32767 || kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString)
                            ? StartupProtection.UnsupportedSource : StartupProtection.None, "NotMeasured", identity, StartupIdentityResolver.Hash($"{identity}\0{kind}\0{raw}\0{rawFingerprint}"));
                    item = safety.Apply(item);
                    // HKCU software is shared across WOW64 views. Only exact aliases fold.
                    if (source.Hive == RegistryHive.CurrentUser && source.View == RegistryView.Registry32
                        && snapshots.Any(s => s.Location.Hive == source.Hive && s.Location.View == RegistryView.Registry64 && s.Location.KeyPath == source.KeyPath
                            && s.ValueName == name && s.Kind == kind && s.Value == (raw ?? ""))) continue;
                    snapshots.Add(new(item, source, name, kind, raw ?? ""));
                }
            }
            catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode is 2 or 3) { }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.ComponentModel.Win32Exception) { errors.Add($"Registry:{source.Hive}:{source.View}:{source.Type}"); }
        }
        return (new(Array.AsReadOnly(snapshots.Select(s => s.Item).ToArray()), errors.Count == 0, errors.Distinct().ToArray()), snapshots);
    }
}
