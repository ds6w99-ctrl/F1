using System.ComponentModel;
using SystemCore.Core.Cleaner;
using SystemCore.Core.Uninstaller;
using SystemCore.Infrastructure.Cleaner;

namespace SystemCore.Infrastructure.Uninstaller;

internal sealed record ScannedLeftover(LeftoverItem Item, string Path, CleanerIdentity? Identity, string Root, RegistryEvidence? Registry = null);
internal sealed record OwnedLeftoverSession(LeftoverScanSession Session, IReadOnlyDictionary<Guid, ScannedLeftover> Items);

public sealed class LeftoverScanner(ApplicationRemovalEvidence evidence, IInstalledApplicationInventory inventory) : ILeftoverScanner
{
    private readonly SemaphoreSlim operation = new(1, 1);
    private OwnedLeftoverSession? current;
    internal SemaphoreSlim Operation => operation;
    internal OwnedLeftoverSession Take(LeftoverDeletionRequest request)
    {
        var owned = current;
        if (owned is null || owned.Session.Id != request.SessionId || owned.Session.ApplicationIdentityId != request.ApplicationIdentityId
            || !owned.Session.Complete || DateTimeOffset.UtcNow - owned.Session.CreatedAt > TimeSpan.FromMinutes(10)
            || request.ItemIds.Count is 0 or > 2000 || request.ItemIds.Distinct().Count() != request.ItemIds.Count
            || request.ItemIds.Any(id => !owned.Items.ContainsKey(id))) throw new InvalidOperationException("Invalid leftover selection/session");
        current = null;
        return owned;
    }
    public async Task<LeftoverScanSession> ScanAsync(Guid removalReceiptId, CancellationToken token)
    {
        await operation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            current = null;
            var receipt = evidence.Get(removalReceiptId) ?? throw new InvalidOperationException("Removal receipt expired or invalid");
            var fresh = await inventory.ReadAsync(token).ConfigureAwait(false);
            if (!fresh.Complete || fresh.Applications.Any(a => a.Id == receipt.Application.Id)) throw new InvalidOperationException("Application absence cannot be verified");
            var items = new Dictionary<Guid, ScannedLeftover>();
            var complete = true;
            await Task.Run(() =>
            {
                foreach (var candidate in receipt.Registry)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var snapshot = RegistryLeftoverEvidence.Capture(candidate.Hive, candidate.View, candidate.Path);
                        if (snapshot.Fingerprint != candidate.Fingerprint) continue;
                        if (fresh.Applications.Any(a => string.Equals(a.DisplayName, receipt.Application.DisplayName, StringComparison.OrdinalIgnoreCase))) continue;
                        if (receipt.Application.InstallLocation is { } original && fresh.Applications.Any(a => a.InstallLocation is { } location && LeftoverSafety.Conflicts(original, location))) continue;
                        var id = Guid.NewGuid();
                        var personal = snapshot.Tree.Keys.Any(k => k.Length != 0)
                            || snapshot.Tree.Values.Any(v => !LeftoverSafety.IsRegistryIdentityValue(v.Name));
                        var item = new LeftoverItem(id, receipt.Application.Id, LeftoverType.RegistryKey,
                            $"{candidate.Hive}/{candidate.View}\\{candidate.Path}", "Exact pre-uninstall registry snapshot with matching publisher and product/install identity",
                            personal ? LeftoverConfidence.Review : LeftoverConfidence.Safe, personal ? "May contain personal data" : "Registry backup required; no automatic rollback", null, candidate.Hive == InventoryHive.LocalMachine, false, "Unchanged verified snapshot");
                        items.Add(id, new(item, candidate.Path, null, candidate.Path, snapshot));
                    }
                    catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3) { }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or System.Security.SecurityException) { complete = false; }
                }
                foreach (var root in receipt.Directories)
                {
                    token.ThrowIfCancellationRequested();
                    if (!LeftoverSafety.CanOwnDirectory(receipt.Application, root.Path, fresh.Applications)) continue;
                    try
                    {
                        using var lease = PinnedCleanerPath.Open(root.Path, true);
                        if (lease.Identity.Volume != root.Identity.Volume || lease.Identity.FileId != root.Identity.FileId || lease.Identity.CreationFileTime != root.Identity.CreationFileTime) continue;
                        Visit(root.Path, root.Path, root.Reason, 0);
                    }
                    catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3) { }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception) { complete = false; }
                }
                void Visit(string path, string root, string reason, int depth)
                {
                    token.ThrowIfCancellationRequested();
                    if (depth > 32 || items.Count >= 10000) { complete = false; return; }
                    try
                    {
                        var canonical = CleanerSafety.Canonical(path);
                        if (canonical is null || (canonical != root && !CleanerSafety.IsChild(root, canonical))) { complete = false; return; }
                        var attributes = File.GetAttributes(canonical);
                        if (receipt.Application.ItemKind == ItemKind.Game && LeftoverSafety.SharedGamePath(canonical))
                        { Add(canonical, root, null, LeftoverType.Directory, LeftoverConfidence.Ignore, "Shared game data rejected", null); return; }
                        if ((attributes & FileAttributes.ReparsePoint) != 0) { Add(canonical, root, null, LeftoverType.Directory, LeftoverConfidence.Ignore, "Reparse point rejected", null); return; }
                        var directory = (attributes & FileAttributes.Directory) != 0;
                        using var pinned = PinnedCleanerPath.Open(canonical, directory);
                        if (!directory && !CleanerSafety.SafeFileAttributes(pinned.Attributes, pinned.Identity.Links))
                        { Add(canonical, root, pinned.Identity, LeftoverType.File, LeftoverConfidence.Ignore, "Unsupported file attributes or hard links", null); return; }
                        Add(canonical, root, pinned.Identity, directory ? LeftoverType.Directory : LeftoverType.File, LeftoverConfidence.Safe, reason, directory ? null : pinned.Identity.Size);
                        if (directory)
                            foreach (var child in Directory.EnumerateFileSystemEntries(canonical)) { if (items.Count >= 10000) { complete = false; break; } Visit(child, root, reason, depth + 1); }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception) { complete = false; }
                }
                void Add(string path, string root, CleanerIdentity? identity, LeftoverType type, LeftoverConfidence confidence, string reason, long? size)
                {
                    // Never downgrade Ignore: reparse/hard-link/attribute rejection wins.
                    if (confidence == LeftoverConfidence.Safe && (reason.StartsWith("Exact DataPath", StringComparison.Ordinal)
                        || LeftoverSafety.MayContainPersonalData(path)
                        || (type == LeftoverType.File && !LeftoverSafety.HasApplicationBinaryExtension(path)))) confidence = LeftoverConfidence.Review;
                    var id = Guid.NewGuid();
                    var item = new LeftoverItem(id, receipt.Application.Id, type, path, reason, confidence, confidence == LeftoverConfidence.Safe ? "Application-owned" : confidence == LeftoverConfidence.Review ? "May contain personal data" : "Safety rejected", size,
                        false, false, confidence.ToString());
                    items.Add(id, new(item, path, identity, root));
                }
            }, token).ConfigureAwait(false);
            var session = new LeftoverScanSession(Guid.NewGuid(), removalReceiptId, receipt.Application.Id, DateTimeOffset.UtcNow,
                Array.AsReadOnly(items.Values.Select(i => i.Item).ToArray()), complete);
            current = new(session, items);
            return session;
        }
        finally { operation.Release(); }
    }
}
