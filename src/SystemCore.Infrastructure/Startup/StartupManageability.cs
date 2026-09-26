using System.ComponentModel;
using System.Security.Principal;
using SystemCore.Core.Startup;
using SystemCore.Infrastructure.Cleaner;

namespace SystemCore.Infrastructure.Startup;

// Read-only preflight, not authorization. The mutation service still revalidates
// identity, permissions, protected paths, state integrity and read-back at use time.
internal sealed class StartupManageability(IRegistryStartupProvider registry, IStartupFolderProvider folders,
    ITaskSchedulerStartupProvider tasks, StartupStateService? state, IElevatedStartupClient? elevated)
{
    private bool? helperAvailable;
    private bool HasElevationPath()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return true;
        if (helperAvailable.HasValue) return helperAvailable.Value;
        if (elevated is not WindowsElevatedStartupClient) return (helperAvailable = false).Value;
        try { using var helper = new HelperLaunchLease(); return (helperAvailable = true).Value; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception) { return (helperAvailable = false).Value; }
    }

    internal bool CanManage(StartupItem item)
    {
        if (!ManageableStartupItems.HasSupportedState(item) || state is null
            || !StartupStateService.ValidId(item.Fingerprint) || item.Id != new StartupIdentityResolver().Resolve(item.Identity)
            || item.RequiresElevation && !HasElevationPath()) return false;
        try
        {
            if (!RecoveryAvailable(item.RequiresElevation, item.Status != StartupStatus.DisabledBySystemCore)) return false;
            var target = StartupCommandParser.Read(item.CommandDisplay);
            // Disabled folder executables are stored under their recovery name.
            var disabledExecutable = item.Status == StartupStatus.DisabledBySystemCore
                && item.SourceType is StartupSourceType.StartupFolderUser or StartupSourceType.StartupFolderCommon
                && string.Equals(item.SourceLocation, item.ExecutablePath, StringComparison.OrdinalIgnoreCase);
            if (!disabledExecutable && (target.Executable != item.ExecutablePath
                || new StartupSafetyClassifier().Classify(item with { Publisher = target.Publisher, ExecutablePath = target.Executable }) != StartupProtection.None)) return false;
            if (item.Status == StartupStatus.DisabledBySystemCore) return CanRestore(item);
            if (item.SourceType is StartupSourceType.RegistryRun or StartupSourceType.RegistryRunOnce)
            {
                if (registry is not RegistryStartupProvider provider) return false;
                var location = provider.Locations.Registry.SingleOrDefault(l => item.Identity.StartsWith($"Registry:{l.Hive}:{l.View}:{l.KeyPath}:", StringComparison.Ordinal));
                if (location is null) return false;
                // Query rights only. No value writes or transaction is performed.
                using var key = StartupRegistryAccess.Open(location, write: !item.RequiresElevation);
                var name = item.Identity[($"Registry:{location.Hive}:{location.View}:{location.KeyPath}:").Length..];
                var kind = key.GetValueKind(name);
                var value = key.GetValue(name, null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                var bytes = StartupRegistryAccess.ReadBytes(key, name, kind);
                var fingerprint = StartupIdentityResolver.Hash($"{item.Identity}\0{kind}\0{value}\0{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))}");
                return fingerprint == item.Fingerprint && StartupAccessProbe.Allows(key.Handle, true, 2, item.RequiresElevation);
            }
            if (item.SourceType is StartupSourceType.StartupFolderUser or StartupSourceType.StartupFolderCommon)
            {
                if (folders is not StartupFolderProvider provider || !provider.Locations.Folders.Any(f =>
                    f.Scope == item.Scope && string.Equals(f.Path, Path.GetDirectoryName(item.SourceLocation), StringComparison.OrdinalIgnoreCase))) return false;
                using var file = StartupFileMove.Open(item.SourceLocation, false, deleteAccess: !item.RequiresElevation);
                if ((file.Attributes & (uint)FileAttributes.ReadOnly) != 0
                    || !SystemCore.Core.Cleaner.CleanerSafety.SafeFileAttributes(file.Attributes, file.Identity.Links)
                    || file.Identity.Size > 128 * 1024 * 1024
                    || !StartupAccessProbe.Allows(file.Leaf, false, 0x10000, item.RequiresElevation)) return false;
                using var parent = StartupFileMove.Open(Path.GetDirectoryName(item.SourceLocation)!, true);
                if (!StartupAccessProbe.Allows(parent.Leaf, false, 2, item.RequiresElevation)) return false;
                if (item.Fingerprint != StartupIdentityResolver.Hash(item.Identity + "\0" + file.Identity + "\0" + item.CommandDisplay)) return false;
                return SameRecoveryVolume(file.Identity.Volume, item.RequiresElevation);
            }
            return tasks is TaskSchedulerStartupProvider { Source: IStartupTaskControl control }
                && control.Get(item.SourceLocation) is { } task
                && TaskSchedulerStartupProvider.Normalize(task) is { IsProtected: false } fresh && fresh.Fingerprint == item.Fingerprint;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException
            or InvalidOperationException or System.Runtime.InteropServices.COMException or System.Security.Cryptography.CryptographicException
            or System.Text.Json.JsonException or System.Xml.XmlException) { return false; }
    }

    private bool RecoveryAvailable(bool machine, bool newReceipt)
    {
        var root = state!.Root(machine);
        if (Directory.Exists(root))
        {
            using var existing = StartupFileMove.Open(root, true);
            if (machine) StartupStateService.ValidateMachineAcl(root);
            if (newReceipt && Directory.EnumerateFiles(root, "*.state").Take(256).Count() >= 256) return false;
            return StartupAccessProbe.Allows(existing.Leaf, false, 2, machine);
        }
        var parent = Path.GetDirectoryName(root)!;
        if (!Directory.Exists(parent)) parent = Path.GetDirectoryName(parent)!;
        using var ancestor = StartupFileMove.Open(parent, true);
        return StartupAccessProbe.Allows(ancestor.Leaf, false, 4, machine);
    }

    private bool SameRecoveryVolume(ulong volume, bool machine)
    {
        var root = state!.Root(machine);
        while (!Directory.Exists(root)) root = Path.GetDirectoryName(root) ?? throw new IOException("No recovery ancestor");
        using var destination = StartupFileMove.Open(root, true);
        return destination.Identity.Volume == volume && StartupAccessProbe.Allows(destination.Leaf, false, 6, machine);
    }

    private bool CanRestore(StartupItem item)
    {
        var saved = state!.Load(item.Id, item.RequiresElevation);
        if (saved.Phase != StartupStatePhase.Disabled
            || StartupIdentityResolver.Hash(System.Text.Json.JsonSerializer.Serialize(saved)) != item.Fingerprint) return false;
        if (saved.Registry is { } registrySource)
        {
            using var key = StartupRegistryAccess.Open(registrySource.Location, write: !item.RequiresElevation);
            return StartupAccessProbe.Allows(key.Handle, true, 2, item.RequiresElevation)
                && !key.GetValueNames().Contains(registrySource.ValueName, StringComparer.OrdinalIgnoreCase);
        }
        if (saved.File is { } file)
        {
            if (File.Exists(file.Path) || Directory.Exists(file.Path)) return false;
            using var backup = StartupFileMove.Open(state.DisabledPath(item.Id, item.RequiresElevation), false, deleteAccess: !item.RequiresElevation);
            using var destination = StartupFileMove.Open(file.Folder.Path, true);
            return StartupAccessProbe.Allows(backup.Leaf, false, 0x10000, item.RequiresElevation)
                && StartupAccessProbe.Allows(destination.Leaf, false, 2, item.RequiresElevation)
                && backup.Identity == file.FileIdentity && backup.Identity.Volume == destination.Identity.Volume
                && StartupFileMove.Hash(backup) == saved.FileHash;
        }
        return saved.Task is { } task && tasks is TaskSchedulerStartupProvider { Source: IStartupTaskControl control }
            && control.Get(task.Path) is { } current && StartupMutationService.SameTaskDefinition(current, task)
            && TaskSchedulerStartupProvider.Normalize(current) is { IsProtected: false };
    }
}
