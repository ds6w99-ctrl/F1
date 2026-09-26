using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Xml.Linq;
using SystemCore.Core.Startup;
using SystemCore.Infrastructure.Cleaner;
using SystemCore.Infrastructure.Uninstaller;

namespace SystemCore.Infrastructure.Startup;

public sealed class StartupMutationService(StartupInventoryService inventory, IRegistryStartupProvider registry,
    IStartupFolderProvider folders, ITaskSchedulerStartupProvider tasks, StartupStateService state, IElevatedStartupClient? elevated = null) : IStartupMutationService
{
    internal Action<Exception>? DiagnosticFailure { get; set; }
    public async Task<StartupOperationResult> ApplyAsync(StartupOperationRequest request, CancellationToken token)
    {
        try { await inventory.Operation.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return new(StartupOutcome.Cancelled); }
        try
        {
            var item = inventory.Consume(request);
            if (item is null) return new(StartupOutcome.RefreshRequired);
            if (item.IsProtected) return new(StartupOutcome.Protected);
            StartupDiagnostics.Log.Operation((int)request.Action, -1);
            StartupOperationResult result;
            using var identity = WindowsIdentity.GetCurrent();
            if (item.RequiresElevation && !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                result = elevated is null ? new(StartupOutcome.AccessDenied) : await elevated.ApplyAsync(item.Id, item.Fingerprint, request.Action, token).ConfigureAwait(false);
            else result = await Task.Run(() => Execute(item, request.Action, token)).ConfigureAwait(false);
            StartupDiagnostics.Log.Operation((int)request.Action, (int)result.Outcome);
            return result;
        }
        finally { inventory.Operation.Release(); }
    }
    internal StartupOperationResult Execute(StartupItem item, StartupAction action, CancellationToken token)
    {
        StartupSavedState? saved = null;
        var prepared = false;
        try
        {
            token.ThrowIfCancellationRequested();
            if (item.IsProtected || item.ProtectionReason != StartupProtection.None || !StartupStateService.ValidId(item.Id)) return new(StartupOutcome.Protected);
            using var storeLock = state.Lock(item.RequiresElevation);
            if (item.Status is StartupStatus.DisabledBySystemCore or StartupStatus.RecoveryRequired)
            {
                if (action != StartupAction.Enable) return new(StartupOutcome.RefreshRequired);
                saved = state.Load(item.Id, item.RequiresElevation);
                if (StartupIdentityResolver.Hash(JsonSerializer.Serialize(saved)) != item.Fingerprint) return new(StartupOutcome.RefreshRequired);
                token.ThrowIfCancellationRequested();
                saved = saved with { Phase = StartupStatePhase.Restoring }; state.Save(saved); prepared = true;
                Restore(saved, token);
                state.FinalizeState(saved); return new(StartupOutcome.Success);
            }
            if (File.Exists(state.StatePath(item.Id, item.RequiresElevation))) return new(StartupOutcome.RecoveryRequired);
            if (registry is not RegistryStartupProvider reg || folders is not StartupFolderProvider folder || tasks is not TaskSchedulerStartupProvider task)
                return new(StartupOutcome.Failed);
            using var current = WindowsIdentity.GetCurrent();
            var owner = current.User!.Value;
            if (item.SourceType is StartupSourceType.RegistryRun or StartupSourceType.RegistryRunOnce)
            {
                if (action != StartupAction.Disable || item.Status != StartupStatus.Enabled) return new(StartupOutcome.RefreshRequired);
                var source = reg.Read(token).Snapshots.SingleOrDefault(s => s.Item.Id == item.Id);
                if (source is null || !Matches(item, source.Item)) return new(StartupOutcome.RefreshRequired);
                using var transaction = RegistryTransaction.Begin();
                using var key = StartupRegistryAccess.Open(source.Location, transaction, true);
                var bytes = StartupRegistryAccess.ReadBytes(key, source.ValueName, source.Kind);
                if (key.GetValue(source.ValueName, null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames) as string != source.Value) return new(StartupOutcome.RefreshRequired);
                var transactionFingerprint = StartupIdentityResolver.Hash($"{source.Item.Identity}\0{source.Kind}\0{source.Value}\0{Convert.ToHexString(SHA256.HashData(bytes))}");
                if (transactionFingerprint != item.Fingerprint) return new(StartupOutcome.RefreshRequired);
                saved = new(1, item, StartupStatePhase.Prepared, DateTimeOffset.UtcNow, owner, item.RequiresElevation, source, null, null, bytes, null);
                state.Save(saved); prepared = true; token.ThrowIfCancellationRequested();
                if (!StartupRegistryAccess.ReadBytes(key, source.ValueName, source.Kind).SequenceEqual(bytes)) return new(StartupOutcome.RefreshRequired);
                StartupRegistryAccess.Remove(key, source.ValueName);
                if (key.GetValueNames().Contains(source.ValueName, StringComparer.OrdinalIgnoreCase)) throw new IOException("Removal not verified");
                transaction.Commit();
                using var verify = StartupRegistryAccess.Open(source.Location);
                if (verify.GetValueNames().Contains(source.ValueName, StringComparer.OrdinalIgnoreCase)) throw new IOException("Source recreated");
            }
            else if (item.SourceType is StartupSourceType.StartupFolderUser or StartupSourceType.StartupFolderCommon)
            {
                if (action != StartupAction.Disable || item.Status != StartupStatus.Enabled) return new(StartupOutcome.RefreshRequired);
                var source = folder.Read(token).Snapshots.SingleOrDefault(s => s.Item.Id == item.Id);
                if (source is null || !Matches(item, source.Item)) return new(StartupOutcome.RefreshRequired);
                using (var file = StartupFileMove.Open(source.Path, false, deleteAccess: true))
                using (var destination = StartupFileMove.Open(state.Root(item.RequiresElevation), true))
                {
                    if (file.Identity != source.FileIdentity || file.Identity.Volume != destination.Identity.Volume) return new(StartupOutcome.RefreshRequired);
                    var hash = StartupFileMove.Hash(file);
                    saved = new(1, item, StartupStatePhase.Prepared, DateTimeOffset.UtcNow, owner, item.RequiresElevation, null, source, null, null, hash);
                    state.Save(saved); prepared = true; token.ThrowIfCancellationRequested();
                    StartupFileMove.Move(file, destination, Path.GetFileName(state.DisabledPath(item.Id, item.RequiresElevation)));
                }
                using var check = StartupFileMove.Open(state.DisabledPath(item.Id, item.RequiresElevation), false);
                if (check.Identity != source.FileIdentity || StartupFileMove.Hash(check) != saved.FileHash || File.Exists(source.Path)) throw new IOException("Move not verified");
            }
            else
            {
                if (task.Source is not IStartupTaskControl control) return new(StartupOutcome.Failed);
                var source = control.Get(item.SourceLocation);
                var fresh = source is null ? null : TaskSchedulerStartupProvider.Normalize(source);
                if (source is null || fresh is null || !Matches(item, fresh)) return new(StartupOutcome.RefreshRequired);
                if (action == StartupAction.Enable && !source.Enabled)
                {
                    // A source-disabled task needs no fabricated registration. Keep a
                    // prepared recovery receipt until the supported state change verifies.
                    saved = new(1, item, StartupStatePhase.Restoring, DateTimeOffset.UtcNow, owner, item.RequiresElevation, null, null, source, null, null);
                    state.Save(saved); prepared = true; token.ThrowIfCancellationRequested();
                    control.SetEnabled(source, true);
                    VerifyTask(control, source, true); state.FinalizeState(saved); return new(StartupOutcome.Success);
                }
                if (action != StartupAction.Disable || !source.Enabled) return new(StartupOutcome.RefreshRequired);
                saved = new(1, item, StartupStatePhase.Prepared, DateTimeOffset.UtcNow, owner, item.RequiresElevation, null, null, source, null, null);
                state.Save(saved); prepared = true; token.ThrowIfCancellationRequested();
                control.SetEnabled(source, false); VerifyTask(control, source, false);
            }
            state.Save(saved! with { Phase = StartupStatePhase.Disabled });
            return new(StartupOutcome.Success);
        }
        catch (StartupConflictException) { return new(StartupOutcome.Conflict); }
        catch (OperationCanceledException) { return new(prepared ? StartupOutcome.RecoveryRequired : StartupOutcome.Cancelled); }
        catch (UnauthorizedAccessException) { return new(prepared ? StartupOutcome.RecoveryRequired : StartupOutcome.AccessDenied); }
        catch (Win32Exception e) when (e.NativeErrorCode == 5) { return new(prepared ? StartupOutcome.RecoveryRequired : StartupOutcome.AccessDenied); }
        catch (Exception e) when (e is IOException or Win32Exception or COMException or JsonException or CryptographicException or ArgumentException or InvalidOperationException)
        { DiagnosticFailure?.Invoke(e); return new(prepared ? StartupOutcome.RecoveryRequired : StartupOutcome.StateInvalid); }
    }
    private void Restore(StartupSavedState saved, CancellationToken token)
    {
        if (saved.Registry is { } source)
        {
            var parsed = StartupCommandParser.Read(source.Value);
            if (parsed.Executable is null || new StartupSafetyClassifier().Classify(saved.Item with { ExecutablePath = parsed.Executable, Publisher = parsed.Publisher }) != StartupProtection.None) throw new IOException("Changed protection");
            using var transaction = RegistryTransaction.Begin();
            using var key = StartupRegistryAccess.Open(source.Location, transaction, true);
            if (key.GetValueNames().Contains(source.ValueName, StringComparer.OrdinalIgnoreCase))
            {
                // Crash before removal, or after a successful restore: an exact
                // read-back can finalize the receipt without rewriting the slot.
                if (key.GetValueKind(source.ValueName) == source.Kind && StartupRegistryAccess.ReadBytes(key, source.ValueName, source.Kind).SequenceEqual(saved.RegistryBytes!)) return;
                throw new StartupConflictException();
            }
            token.ThrowIfCancellationRequested();
            StartupRegistryAccess.Restore(key, source.ValueName, source.Kind, saved.RegistryBytes!);
            if (!StartupRegistryAccess.ReadBytes(key, source.ValueName, source.Kind).SequenceEqual(saved.RegistryBytes!)) throw new IOException("Restore not verified");
            transaction.Commit();
            using var verify = StartupRegistryAccess.Open(source.Location);
            if (!StartupRegistryAccess.ReadBytes(verify, source.ValueName, source.Kind).SequenceEqual(saved.RegistryBytes!)) throw new IOException("Restore read-back failed");
        }
        else if (saved.File is { } file)
        {
            if (File.Exists(file.Path) || Directory.Exists(file.Path))
            {
                if (!File.Exists(state.DisabledPath(saved.Item.Id, saved.Machine)))
                {
                    using var original = StartupFileMove.Open(file.Path, false);
                    if (original.Identity == file.FileIdentity && StartupFileMove.Hash(original) == saved.FileHash) return;
                }
                throw new StartupConflictException();
            }
            using (var sourceFile = StartupFileMove.Open(state.DisabledPath(saved.Item.Id, saved.Machine), false, deleteAccess: true))
            using (var destination = StartupFileMove.Open(file.Folder.Path, true))
            {
                if (sourceFile.Identity != file.FileIdentity || StartupFileMove.Hash(sourceFile) != saved.FileHash) throw new IOException("Changed disabled file");
                if (!string.Equals(file.Path, saved.Item.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    var parsed = StartupCommandParser.Read(saved.Item.CommandDisplay);
                    if (parsed.Executable is null || new StartupSafetyClassifier().Classify(saved.Item with { ExecutablePath = parsed.Executable, Publisher = parsed.Publisher }) != StartupProtection.None) throw new IOException("Changed target protection");
                }
                token.ThrowIfCancellationRequested(); StartupFileMove.Move(sourceFile, destination, Path.GetFileName(file.Path));
            }
            using var check = StartupFileMove.Open(file.Path, false);
            if (check.Identity != file.FileIdentity || StartupFileMove.Hash(check) != saved.FileHash) throw new IOException("File restore not verified");
        }
        else if (saved.Task is { } task && tasks is TaskSchedulerStartupProvider provider && provider.Source is IStartupTaskControl control)
        {
            var current = control.Get(task.Path) ?? throw new IOException("Task missing");
            if (!SameTaskDefinition(current, task)) throw new StartupConflictException();
            var item = TaskSchedulerStartupProvider.Normalize(current);
            if (item is null || item.IsProtected) throw new IOException("Task protection changed");
            token.ThrowIfCancellationRequested();
            if (!current.Enabled) control.SetEnabled(current, true);
            VerifyTask(control, task, true);
        }
        else throw new IOException("Unsupported saved source");
    }
    private static bool Matches(StartupItem expected, StartupItem actual) => actual.Id == expected.Id && actual.Fingerprint == expected.Fingerprint
        && !actual.IsProtected && actual.RequiresElevation == expected.RequiresElevation;
    internal static bool SameTaskDefinition(StartupTaskSnapshot left, StartupTaskSnapshot right)
    {
        if (left.Path != right.Path || left.SecurityDescriptor != right.SecurityDescriptor) return false;
        static string Definition(string text)
        {
            // XML was already bounded/DTD-rejected by Normalize before storage.
            var xml = XDocument.Parse(text); XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var settings = xml.Root?.Element(ns + "Settings");
            settings?.Element(ns + "Enabled")?.Remove();
            if (settings is not null && !settings.HasElements && !settings.HasAttributes && string.IsNullOrWhiteSpace(settings.Value)) settings.Remove();
            return xml.ToString(SaveOptions.DisableFormatting);
        }
        return Definition(left.Xml) == Definition(right.Xml);
    }
    private static void VerifyTask(IStartupTaskControl control, StartupTaskSnapshot source, bool enabled)
    {
        var verify = control.Get(source.Path);
        if (verify is null || verify.Enabled != enabled || !SameTaskDefinition(verify, source)) throw new IOException("Task definition/read-back changed");
    }
}
internal sealed class StartupConflictException : IOException;
