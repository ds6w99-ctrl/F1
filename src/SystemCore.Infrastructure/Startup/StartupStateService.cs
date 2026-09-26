using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using SystemCore.Core.Cleaner;
using SystemCore.Core.Startup;
using SystemCore.Infrastructure.Cleaner;

namespace SystemCore.Infrastructure.Startup;

internal enum StartupStatePhase { Prepared, Disabled, Restoring }
internal sealed record StartupSavedState(int Version, StartupItem Item, StartupStatePhase Phase, DateTimeOffset SavedAt,
    string Owner, bool Machine, RegistryStartupSnapshot? Registry, StartupFileSnapshot? File, StartupTaskSnapshot? Task,
    byte[]? RegistryBytes, string? FileHash);

public sealed class StartupStateService : IStartupStateService
{
    internal string UserRoot { get; }
    internal string MachineRoot { get; }
    internal IStartupLocations Locations { get; }
    public StartupStateService() : this(new WindowsStartupLocations(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemCore", "StartupState"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SystemCore-StartupState-v1")) { }
    internal StartupStateService(IStartupLocations locations, string userRoot, string machineRoot)
    { Locations = locations; UserRoot = userRoot; MachineRoot = machineRoot; }
    internal string Root(bool machine) => machine ? MachineRoot : UserRoot;
    internal static bool ValidId(string? id) => id is { Length: 64 } && id.All(char.IsAsciiHexDigit);
    internal string StatePath(string id, bool machine) => ValidId(id) ? Path.Combine(Root(machine), id + ".state") : throw new IOException("Invalid item ID");
    internal string DisabledPath(string id, bool machine) => ValidId(id) ? Path.Combine(Root(machine), id + ".disabled") : throw new IOException("Invalid item ID");
    internal IDisposable Lock(bool machine)
    {
        EnsureRoot(machine);
        var root = StartupFileMove.Open(Root(machine), true);
        try
        {
            var stream = StartupFileMove.CreateChild(root, "operation.lock", lockFile: true);
            return new StoreLease(root, stream);
        }
        catch { root.Dispose(); throw; }
    }
    private sealed class StoreLease(IDisposable root, IDisposable file) : IDisposable
    { public void Dispose() { file.Dispose(); root.Dispose(); } }
    private void EnsureRoot(bool machine)
    {
        var path = Root(machine);
        if (!Directory.Exists(path))
        {
            var parentPath = Path.GetDirectoryName(path) ?? throw new IOException("Missing state parent");
            if (!Directory.Exists(parentPath))
            {
                // Only the application-owned parent may be absent in production.
                using var ancestor = StartupFileMove.Open(Path.GetDirectoryName(parentPath)!, true);
                Directory.CreateDirectory(parentPath);
            }
            using var parent = StartupFileMove.Open(parentPath, true);
            if (machine)
            {
                var security = new DirectorySecurity();
                security.SetSecurityDescriptorSddlForm("O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)");
                new DirectoryInfo(path).Create(security);
            }
            else Directory.CreateDirectory(path);
        }
        using var root = StartupFileMove.Open(path, true);
        if (machine) ValidateMachineAcl(path);
    }
    internal static void ValidateMachineAcl(string path)
    {
        var acl = new DirectoryInfo(path).GetAccessControl();
        var owner = acl.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier ?? throw new IOException("Missing state owner");
        if (!owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) && !owner.IsWellKnown(WellKnownSidType.LocalSystemSid)) throw new IOException("Untrusted machine state owner");
        if (!acl.AreAccessRulesProtected) throw new IOException("Unprotected machine state ACL");
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            var sid = (SecurityIdentifier)rule.IdentityReference;
            if (rule.AccessControlType == AccessControlType.Allow && !sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)
                && !sid.IsWellKnown(WellKnownSidType.LocalSystemSid)
                && (rule.FileSystemRights & (FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership)) != 0)
                throw new IOException("Writable machine state ACL");
        }
    }
    internal void Save(StartupSavedState state)
    {
        Validate(state);
        var path = StatePath(state.Item.Id, state.Machine);
        if (!File.Exists(path) && Directory.EnumerateFiles(Root(state.Machine), "*.state").Take(257).Count() >= 256) throw new IOException("State limit reached");
        var plain = JsonSerializer.SerializeToUtf8Bytes(state);
        if (plain.Length > 1024 * 1024) throw new IOException("State limit exceeded");
        var encoded = StartupDataProtection.Protect(plain, state.Machine);
        var temp = Path.Combine(Root(state.Machine), Guid.NewGuid().ToString("N") + ".pending");
        using (var directory = StartupFileMove.Open(Root(state.Machine), true))
        using (var file = StartupFileMove.CreateChild(directory, Path.GetFileName(temp)))
        {
            var renamed = false;
            try
            {
                file.Write(encoded); file.Flush(true);
                if (File.Exists(path)) { using var existing = StartupFileMove.Open(path, false); if (existing.Identity.Links != 1) throw new IOException("Unsafe state file"); }
                StartupFileMove.MoveHandle(file.SafeFileHandle, directory, Path.GetFileName(path), replaceState: true);
                renamed = true;
            }
            finally { if (!renamed) StartupFileMove.DiscardPending(file.SafeFileHandle); }
        }
        var check = Load(state.Item.Id, state.Machine);
        if (JsonSerializer.Serialize(check) != JsonSerializer.Serialize(state)) throw new IOException("State read-back failed");
    }
    internal StartupSavedState Load(string id, bool machine)
    {
        using var root = StartupFileMove.Open(Root(machine), true);
        if (machine) ValidateMachineAcl(Root(machine));
        using var lease = StartupFileMove.Open(StatePath(id, machine), false);
        if (lease.Identity.Links != 1 || lease.Identity.Size is <= 0 or > 2 * 1024 * 1024) throw new IOException("Invalid state file");
        var bytes = new byte[checked((int)lease.Identity.Size)];
        var count = RandomAccess.Read(lease.Leaf, bytes, 0);
        if (count != bytes.Length) throw new IOException("Incomplete state");
        var state = JsonSerializer.Deserialize<StartupSavedState>(StartupDataProtection.Unprotect(bytes), new JsonSerializerOptions { MaxDepth = 16, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow }) ?? throw new IOException("Invalid state");
        Validate(state);
        if (state.Item.Id != id || state.Machine != machine) throw new IOException("Mismatched state");
        return state;
    }
    internal void FinalizeState(StartupSavedState state)
    {
        using var file = StartupFileMove.Open(StatePath(state.Item.Id, state.Machine), false, deleteAccess: true);
        file.MarkFileForDeletion();
    }
    internal void Validate(StartupSavedState state)
    {
        using var current = WindowsIdentity.GetCurrent();
        if (state.Item is null || state.Version != 1 || !Enum.IsDefined(state.Phase) || !ValidId(state.Item.Id) || state.Item.IsProtected
            || !Enum.IsDefined(state.Item.SourceType) || !Enum.IsDefined(state.Item.Scope) || !Enum.IsDefined(state.Item.Status)
            || string.IsNullOrEmpty(state.Item.Identity) || state.Item.Identity.Length > 32768
            || state.Item.ProtectionReason != StartupProtection.None || state.Item.Id != new StartupIdentityResolver().Resolve(state.Item.Identity)
            || state.SavedAt > DateTimeOffset.UtcNow.AddMinutes(1) || (!state.Machine && state.Owner != current.User?.Value)) throw new IOException("Invalid state schema/owner");
        var sources = (state.Registry is null ? 0 : 1) + (state.File is null ? 0 : 1) + (state.Task is null ? 0 : 1);
        if (sources != 1) throw new IOException("Ambiguous saved source");
        if (state.Registry is { } registry)
        {
            if (registry.Item is null || !Locations.Registry.Contains(registry.Location) || registry.Item.Id != state.Item.Id
                || state.Item.Identity != $"Registry:{registry.Location.Hive}:{registry.Location.View}:{registry.Location.KeyPath}:{registry.ValueName}"
                || state.Machine != (registry.Location.Hive == Microsoft.Win32.RegistryHive.LocalMachine)
                || registry.Kind is not (Microsoft.Win32.RegistryValueKind.String or Microsoft.Win32.RegistryValueKind.ExpandString)
                || state.RegistryBytes is not { Length: > 0 and <= 65536 }) throw new IOException("Invalid Registry state");
        }
        if (state.File is { } file)
        {
            if (file.Item is null || !Locations.Folders.Contains(file.Folder) || file.Item.Id != state.Item.Id || file.FileIdentity is null
                || !string.Equals(Path.GetDirectoryName(file.Path), file.Folder.Path, StringComparison.OrdinalIgnoreCase)
                || CleanerSafety.Canonical(file.Path) != file.Path || state.Item.Identity != $"Folder:{file.Folder.Scope}:{file.Path}"
                || state.Machine != (file.Folder.Scope == StartupScope.AllUsers) || !ValidId(state.FileHash)) throw new IOException("Invalid file state");
        }
        if (state.Task is { } task)
        {
            var item = TaskSchedulerStartupProvider.Normalize(task);
            if (item is null || item.IsProtected || item.Id != state.Item.Id || item.Fingerprint != state.Item.Fingerprint
                || state.Machine != item.RequiresElevation) throw new IOException("Invalid task state");
        }
    }
    internal IEnumerable<StartupSavedState> ReadStates(bool machine)
    {
        if (!Directory.Exists(Root(machine))) yield break;
        using var root = StartupFileMove.Open(Root(machine), true);
        if (machine) ValidateMachineAcl(Root(machine));
        var files = Directory.EnumerateFiles(Root(machine), "*.state").Take(257).ToArray();
        if (files.Length > 256) throw new IOException("State limit exceeded");
        foreach (var path in files) yield return Load(Path.GetFileNameWithoutExtension(path), machine);
    }
    public Task<StartupInventoryResult> ReadAsync(CancellationToken token) => Task.Run(() =>
    {
        var rows = new List<StartupItem>(); var errors = new List<string>();
        foreach (var machine in new[] { false, true })
        {
            try
            {
                foreach (var state in ReadStates(machine))
                {
                    token.ThrowIfCancellationRequested();
                    rows.Add(state.Item with { Status = state.Phase == StartupStatePhase.Disabled ? StartupStatus.DisabledBySystemCore : StartupStatus.RecoveryRequired,
                        Fingerprint = StartupIdentityResolver.Hash(JsonSerializer.Serialize(state)) });
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception or JsonException or CryptographicException or ArgumentException or System.Xml.XmlException or InvalidOperationException)
            { errors.Add(machine ? "MachineStartupStateInvalid" : "UserStartupStateInvalid"); }
        }
        return new StartupInventoryResult(rows.AsReadOnly(), errors.Count == 0, errors);
    }, token);
}

internal static class StartupDataProtection
{
    internal static byte[] Protect(byte[] value, bool machine) => Transform(value, true, machine);
    internal static byte[] Unprotect(byte[] value) => Transform(value, false, false);
    private static byte[] Transform(byte[] value, bool protect, bool machine)
    {
        var input = new Blob { Length = value.Length, Data = Marshal.AllocHGlobal(value.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(value, 0, input.Data, value.Length);
            var ok = protect ? CryptProtectData(ref input, "SystemCore Startup v1", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, machine ? 5 : 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
            if (output.Length is < 0 or > 2 * 1024 * 1024) throw new IOException("Invalid protected data");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally { Marshal.FreeHGlobal(input.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
}
