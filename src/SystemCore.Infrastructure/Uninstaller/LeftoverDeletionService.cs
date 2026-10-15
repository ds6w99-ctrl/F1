using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using SystemCore.Core.Cleaner;
using SystemCore.Core.Uninstaller;
using SystemCore.Infrastructure.Cleaner;

namespace SystemCore.Infrastructure.Uninstaller;

public interface IRegistryBackupWriter
{
    Task<string> SaveAsync(string applicationId, string displayName, string hive, string view, string path, byte[] typedSnapshot, CancellationToken token);
}
public sealed class RegistryBackupWriter : IRegistryBackupWriter
{
    public async Task<string> SaveAsync(string applicationId, string displayName, string hive, string view, string path, byte[] typedSnapshot, CancellationToken token)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        using var localLease = PinnedCleanerPath.Open(local, true);
        var applicationDirectory = Path.Combine(local, "SystemCore");
        Directory.CreateDirectory(applicationDirectory);
        using var applicationLease = PinnedCleanerPath.Open(applicationDirectory, true);
        var directory = Path.Combine(applicationDirectory, "Backups");
        Directory.CreateDirectory(directory);
        using var ancestors = PinnedCleanerPath.Open(directory, true);
        var file = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        var content = JsonSerializer.SerializeToUtf8Bytes(new { Format = "SystemCore.RegistryBackup.v1", ApplicationId = applicationId, Application = displayName,
            Timestamp = DateTimeOffset.UtcNow, Hive = hive, View = view, OriginalPath = path,
            SnapshotSha256 = Convert.ToHexString(SHA256.HashData(typedSnapshot)), TypedSnapshotBase64 = Convert.ToBase64String(typedSnapshot) });
        await using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.WriteThrough);
        await stream.WriteAsync(content, token).ConfigureAwait(false); stream.Flush(true);
        stream.Position = 0; var verified = new byte[content.Length]; await stream.ReadExactlyAsync(verified, token).ConfigureAwait(false);
        if (!verified.AsSpan().SequenceEqual(content)) throw new IOException("Backup verification failed");
        return file;
    }
}

public sealed class LeftoverDeletionService(LeftoverScanner scanner, ApplicationRemovalEvidence evidence,
    IInstalledApplicationInventory inventory, IRegistryBackupWriter backups) : ILeftoverDeletionService
{
    public async Task<LeftoverDeletionResult> DeleteAsync(LeftoverDeletionRequest request, CancellationToken token)
    {
        if (request.ItemIds is null || request.ItemIds.Count is 0 or > 2000) throw new InvalidOperationException("Invalid selection size");
        request = request with { ItemIds = Array.AsReadOnly(request.ItemIds.ToArray()) };
        await scanner.Operation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var session = scanner.Take(request);
            var receipt = evidence.Get(session.Session.ReceiptId) ?? throw new InvalidOperationException("Removal receipt expired");
            var results = new List<LeftoverDeletionItemResult>(); long removed = 0;
            var selected = request.ItemIds.Select(id => session.Items[id]).OrderBy(i => i.Item.Type == LeftoverType.Directory ? 1 : 0)
                .ThenByDescending(i => i.Path.Count(c => c == '\\')).ToArray();
            var changedDirectories = new List<string>();
            // Review directories join the identity pre-check only when the caller explicitly
            // acknowledged personal-data destruction; Ignore items are never eligible.
            foreach (var directory in session.Items.Values.Where(i => i.Item.Type == LeftoverType.Directory
                && (i.Item.Confidence == LeftoverConfidence.Safe || (request.IncludePersonalData && i.Item.Confidence == LeftoverConfidence.Review))))
            {
                try
                {
                    using var pinned = PinnedCleanerPath.Open(directory.Path, true);
                    if (pinned.Identity != directory.Identity) changedDirectories.Add(directory.Path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception) { changedDirectories.Add(directory.Path); }
            }
            foreach (var item in selected)
            {
                if (token.IsCancellationRequested) { results.Add(new(item.Item.Id, LeftoverDeletionStatus.Cancelled, 0)); continue; }
                if (DateTimeOffset.UtcNow - session.Session.CreatedAt > TimeSpan.FromMinutes(10) || evidence.Get(session.Session.ReceiptId) is null)
                { results.Add(new(item.Item.Id, LeftoverDeletionStatus.SafetyRejected, 0)); continue; }
                if (item.Item.Confidence == LeftoverConfidence.Ignore
                    || (item.Item.Confidence == LeftoverConfidence.Review && !request.IncludePersonalData))
                { results.Add(new(item.Item.Id, LeftoverDeletionStatus.SafetyRejected, 0)); continue; }
                if (item.Registry is null && changedDirectories.Any(path => path == item.Path || CleanerSafety.IsChild(path, item.Path)))
                { results.Add(new(item.Item.Id, LeftoverDeletionStatus.Changed, 0)); continue; }
                try
                {
                    var fresh = await inventory.ReadAsync(token).ConfigureAwait(false);
                    if (!fresh.Complete || fresh.Applications.Any(a => a.Id == receipt.Application.Id))
                    { results.Add(new(item.Item.Id, LeftoverDeletionStatus.SafetyRejected, 0)); continue; }
                    if (item.Registry is { } registry)
                    {
                        if (fresh.Applications.Any(a => string.Equals(a.DisplayName, receipt.Application.DisplayName, StringComparison.OrdinalIgnoreCase)
                            || (a.InstallLocation is { } location && receipt.Application.InstallLocation is { } original && LeftoverSafety.Conflicts(location, original))))
                        { results.Add(new(item.Item.Id, LeftoverDeletionStatus.SafetyRejected, 0)); continue; }
                        using var transaction = RegistryTransaction.Begin();
                        var snapshot = RegistryLeftoverEvidence.Capture(registry.Hive, registry.View, registry.Path, transaction);
                        if (snapshot.Fingerprint != registry.Fingerprint) { results.Add(new(item.Item.Id, LeftoverDeletionStatus.Changed, 0)); continue; }
                        string backup;
                        try { backup = await backups.SaveAsync(receipt.Application.Id, receipt.Application.DisplayName, registry.Hive.ToString(), registry.View.ToString(), registry.Path,
                            JsonSerializer.SerializeToUtf8Bytes(snapshot.Tree), token).ConfigureAwait(false); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
                        { results.Add(new(item.Item.Id, LeftoverDeletionStatus.BackupFailed, 0)); continue; }
                        // Repeat within the same transaction after durable backup.
                        var rechecked = RegistryLeftoverEvidence.Capture(registry.Hive, registry.View, registry.Path, transaction);
                        if (rechecked.Fingerprint != snapshot.Fingerprint) { results.Add(new(item.Item.Id, LeftoverDeletionStatus.Changed, 0)); continue; }
                        var beforeCommit = await inventory.ReadAsync(token).ConfigureAwait(false);
                        if (!beforeCommit.Complete || beforeCommit.Applications.Any(a => a.Id == receipt.Application.Id
                            || string.Equals(a.DisplayName, receipt.Application.DisplayName, StringComparison.OrdinalIgnoreCase)
                            || (a.InstallLocation is { } location && receipt.Application.InstallLocation is { } original && LeftoverSafety.Conflicts(location, original))))
                        { results.Add(new(item.Item.Id, LeftoverDeletionStatus.SafetyRejected, 0)); continue; }
                        token.ThrowIfCancellationRequested(); transaction.Delete(snapshot); transaction.Commit();
                        results.Add(new(item.Item.Id, LeftoverDeletionStatus.Deleted, 0, backup));
                        continue;
                    }
                    if (!LeftoverSafety.CanOwnDirectory(receipt.Application, item.Root, fresh.Applications)
                        || (item.Path != item.Root && !CleanerSafety.IsChild(item.Root, item.Path)))
                    { results.Add(new(item.Item.Id, LeftoverDeletionStatus.SafetyRejected, 0)); continue; }
                    var directory = item.Item.Type == LeftoverType.Directory;
                    using var pinned = PinnedCleanerPath.Open(item.Path, directory, true);
                    var expected = item.Identity;
                    var identityMatches = expected is not null && (directory
                        ? pinned.Identity.Volume == expected.Volume && pinned.Identity.FileId == expected.FileId && pinned.Identity.CreationFileTime == expected.CreationFileTime
                        : pinned.Identity == expected);
                    if (!identityMatches) { results.Add(new(item.Item.Id, LeftoverDeletionStatus.Changed, 0)); continue; }
                    // Empty directory removal is nonrecursive. Its write timestamp may
                    // change due to our earlier selected child removals; identity must not.
                    token.ThrowIfCancellationRequested();
                    if (directory) pinned.MarkEmptyDirectoryForDeletion(); else pinned.MarkFileForDeletion();
                    var bytes = directory ? 0 : pinned.Identity.Size;
                    removed = checked(removed + bytes);
                    results.Add(new(item.Item.Id, LeftoverDeletionStatus.Deleted, bytes));
                }
                catch (OperationCanceledException) { results.Add(new(item.Item.Id, LeftoverDeletionStatus.Cancelled, 0)); }
                catch (UnauthorizedAccessException) { results.Add(new(item.Item.Id, LeftoverDeletionStatus.AccessDenied, 0)); }
                catch (Win32Exception ex)
                {
                    var status = ex.NativeErrorCode switch { 2 or 3 => LeftoverDeletionStatus.AlreadyGone, 5 => LeftoverDeletionStatus.AccessDenied,
                        32 or 33 => LeftoverDeletionStatus.InUse, 145 => LeftoverDeletionStatus.Skipped, 6800 => LeftoverDeletionStatus.Changed, _ => LeftoverDeletionStatus.Failed };
                    results.Add(new(item.Item.Id, status, 0));
                }
                catch (IOException) { results.Add(new(item.Item.Id, LeftoverDeletionStatus.SafetyRejected, 0)); }
            }
            return new(results.AsReadOnly(), removed);
        }
        finally { scanner.Operation.Release(); }
    }
}
