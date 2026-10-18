using System.ComponentModel;
using SystemCore.Core.Cleaner;
using SystemCore.Core.Uninstaller;
using SystemCore.Infrastructure.Cleaner;

namespace SystemCore.Infrastructure.Uninstaller;

internal sealed record ScannedLeftover(LeftoverItem Item, string Path, CleanerIdentity? Identity, string Root, RegistryEvidence? Registry = null);
internal sealed record OwnedLeftoverSession(LeftoverScanSession Session, IReadOnlyDictionary<Guid, ScannedLeftover> Items);

public sealed class LeftoverScanner(ApplicationRemovalEvidence evidence, IInstalledApplicationInventory inventory) : ILeftoverScanner
{
    // A truncated session still allows deleting items the user can see, because every single item is
    // revalidated against its captured identity at execution time. Completeness describes the preview,
    // not the safety of the individually verified items inside it.
    private const int GlobalItemLimit = 10000;
    // One unverified anchor (observed or name-derived) may not flood the review: its enumeration
    // stops at this budget and the session is marked incomplete, visibly, instead of hiding the
    // receipt-verified items behind thousands of Review rows or disabling deletion altogether.
    private const int UnverifiedAnchorItemLimit = 300;
    private readonly SemaphoreSlim operation = new(1, 1);
    private OwnedLeftoverSession? current;
    internal SemaphoreSlim Operation => operation;
    internal OwnedLeftoverSession Take(LeftoverDeletionRequest request)
    {
        var owned = current;
        if (owned is null || owned.Session.Id != request.SessionId || owned.Session.ApplicationIdentityId != request.ApplicationIdentityId
            || DateTimeOffset.UtcNow - owned.Session.CreatedAt > TimeSpan.FromMinutes(10)
            || request.ItemIds.Count is 0 or > 2000 || request.ItemIds.Distinct().Count() != request.ItemIds.Count
            || request.ItemIds.Any(id => !owned.Items.ContainsKey(id))) throw new InvalidOperationException("Invalid leftover selection/session");
        current = null;
        return owned;
    }
    public Task<LeftoverScanSession> ScanAsync(Guid removalReceiptId, CancellationToken token) => ScanAsync(removalReceiptId, false, token);
    public async Task<LeftoverScanSession> ScanAsync(Guid removalReceiptId, bool deepScan, CancellationToken token)
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
                        var budget = int.MaxValue;
                        Visit(root.Path, root.Path, root.Reason, 0, false, ref budget);
                    }
                    catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3) { }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception) { complete = false; }
                }
                // Directories the native uninstaller provably worked on: their last-write time moved
                // and their direct entry count decreased while it ran, which is the observable
                // footprint of an actual deletion. This is the only signal that identifies an
                // application-owned data directory whose name does not resemble the application, so
                // it belongs to the conservative scan rather than the deep scan. Claimed before the
                // name guess so the stronger evidence names the item.
                foreach (var touched in receipt.Touched ?? Array.Empty<string>())
                {
                    token.ThrowIfCancellationRequested();
                    if (CleanerSafety.Canonical(touched) is not { } observed || !Directory.Exists(observed)) continue;
                    if (!LeftoverSafety.CanOwnObservedDirectory(receipt.Application, observed, fresh.Applications)) continue;
                    if (receipt.Directories.Any(directory => LeftoverSafety.Conflicts(directory.Path, observed))) continue;
                    if (items.Values.Any(existing => LeftoverSafety.Conflicts(existing.Path, observed))) continue;
                    try
                    {
                        var budget = UnverifiedAnchorItemLimit;
                        Visit(observed, observed, "Ownership observed: the native uninstaller deleted entries in this directory during removal", 0, true, ref budget);
                    }
                    catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3) { }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception) { }
                }
                if (deepScan) DiscoverNameDerived();
                void DiscoverNameDerived()
                {
                    var application = receipt.Application;
                    if (!LeftoverSafety.SafeRegistryName(application.DisplayName)) return;
                    var bases = new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData,
                        Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
                        .Select(Environment.GetFolderPath).Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    var names = new List<string> { application.DisplayName };
                    if (application.Publisher is { } publisher && LeftoverSafety.SafeRegistryName(publisher)
                        && !string.Equals(publisher, application.DisplayName, StringComparison.OrdinalIgnoreCase))
                        names.Add(Path.Combine(publisher, application.DisplayName));
                    foreach (var baseRoot in bases)
                    foreach (var name in names)
                    {
                        token.ThrowIfCancellationRequested();
                        var candidate = Path.Combine(baseRoot, name);
                        // Never re-add something an exact receipt already authorized, and never
                        // overlap a candidate with an object already captured from the receipt.
                        if (receipt.Directories.Any(directory => LeftoverSafety.Conflicts(directory.Path, candidate))) continue;
                        if (items.Values.Any(existing => LeftoverSafety.Conflicts(existing.Path, candidate))) continue;
                        if (CleanerSafety.Canonical(candidate) is null || !LeftoverSafety.CanOwnDirectory(application, candidate, fresh.Applications)) continue;
                        try
                        {
                            if (!Directory.Exists(candidate) && !File.Exists(candidate)) continue;
                            var budget = UnverifiedAnchorItemLimit;
                            Visit(candidate, candidate, "Name-derived candidate; not verified by a pre-uninstall receipt", 0, true, ref budget);
                        }
                        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3) { }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception) { }
                    }
                }
                void Visit(string path, string root, string reason, int depth, bool unverified, ref int budget)
                {
                    token.ThrowIfCancellationRequested();
                    if (depth > 32 || items.Count >= GlobalItemLimit || budget <= 0) { complete = false; return; }
                    try
                    {
                        var canonical = CleanerSafety.Canonical(path);
                        if (canonical is null || (canonical != root && !CleanerSafety.IsChild(root, canonical))) { complete = false; return; }
                        var attributes = File.GetAttributes(canonical);
                        if (receipt.Application.ItemKind == ItemKind.Game && LeftoverSafety.SharedGamePath(canonical))
                        { Add(canonical, root, null, LeftoverType.Directory, LeftoverConfidence.Ignore, "Shared game data rejected", null, unverified); budget--; return; }
                        if ((attributes & FileAttributes.ReparsePoint) != 0) { Add(canonical, root, null, LeftoverType.Directory, LeftoverConfidence.Ignore, "Reparse point rejected", null, unverified); budget--; return; }
                        var directory = (attributes & FileAttributes.Directory) != 0;
                        using var pinned = PinnedCleanerPath.Open(canonical, directory);
                        if (!directory && !CleanerSafety.SafeFileAttributes(pinned.Attributes, pinned.Identity.Links))
                        { Add(canonical, root, pinned.Identity, LeftoverType.File, LeftoverConfidence.Ignore, "Unsupported file attributes or hard links", null, unverified); budget--; return; }
                        Add(canonical, root, pinned.Identity, directory ? LeftoverType.Directory : LeftoverType.File, LeftoverConfidence.Safe, reason, directory ? null : pinned.Identity.Size, unverified);
                        budget--;
                        if (directory)
                            foreach (var child in Directory.EnumerateFileSystemEntries(canonical))
                            {
                                if (items.Count >= GlobalItemLimit || budget <= 0) { complete = false; break; }
                                Visit(child, root, reason, depth + 1, unverified, ref budget);
                            }
                    }
                    // Unverified candidates (name-derived and uninstaller-observed) are best-effort: a
                    // refused read narrows the preview instead of poisoning the receipt-verified
                    // session's completeness.
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception) { if (!unverified) complete = false; }
                }
                void Add(string path, string root, CleanerIdentity? identity, LeftoverType type, LeftoverConfidence confidence, string reason, long? size, bool unverified)
                {
                    // Never downgrade Ignore: reparse/hard-link/attribute rejection wins. Name-derived
                    // candidates are never receipt-verified, so they can never be Safe. A pre-uninstall
                    // footprint is captured, not guessed, but its link to the application is still only
                    // the name. A directory the uninstaller was observed to delete from has a stronger
                    // link, but the observation window is not airtight, so it is held at Review as well.
                    if (confidence == LeftoverConfidence.Safe && (unverified || reason.StartsWith("Exact DataPath", StringComparison.Ordinal)
                        || reason.StartsWith("Pre-uninstall footprint", StringComparison.Ordinal)
                        || reason.StartsWith("Ownership observed", StringComparison.Ordinal)
                        || LeftoverSafety.MayContainPersonalData(path)
                        || (type == LeftoverType.File && !LeftoverSafety.HasApplicationBinaryExtension(path)))) confidence = LeftoverConfidence.Review;
                    var id = Guid.NewGuid();
                    var item = new LeftoverItem(id, receipt.Application.Id, type, path, reason, confidence, confidence == LeftoverConfidence.Safe ? "Application-owned" : confidence == LeftoverConfidence.Review ? "May contain personal data" : "Safety rejected", size,
                        LeftoverSafety.IsElevatedRoot(root), false, confidence.ToString());
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
