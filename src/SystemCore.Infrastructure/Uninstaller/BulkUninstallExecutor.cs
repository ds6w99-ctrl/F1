using SystemCore.Core.Uninstaller;

namespace SystemCore.Infrastructure.Uninstaller;

// Sequential bulk removal over the exact single-item path. Each entry re-reads the registration,
// captures its own pre-uninstall evidence, gets its own single-use session and is independently
// identity-verified, so a batch can never be weaker than removing the same items one by one.
public sealed class BulkUninstallExecutor(UninstallExecutor executor) : IBulkUninstallExecutor
{
    public const int MaximumBatchSize = 100;

    public async Task<BulkUninstallOutcome> UninstallAsync(BulkUninstallRequest request, IProgress<BulkUninstallItemResult>? progress, CancellationToken token)
    {
        var results = new List<BulkUninstallItemResult>();
        if (request.ApplicationIds is null || request.ApplicationIds.Count is 0 or > MaximumBatchSize
            || request.ApplicationIds.Distinct().Count() != request.ApplicationIds.Count)
            return new(results.AsReadOnly(), 0, 0, 0);
        var published = executor.PublishedSession;
        if (published is null || published.Id != request.InventorySessionId || DateTimeOffset.UtcNow - published.CreatedAt > TimeSpan.FromMinutes(10))
            return new(results.AsReadOnly(), 0, 0, 0);
        // Validate the whole selection before the first removal so an invalid batch changes nothing.
        var selected = new List<InstalledApplication>();
        foreach (var id in request.ApplicationIds)
        {
            var application = published.Inventory.Applications.SingleOrDefault(a => a.Id == id);
            if (application is null || !application.CanUninstall || application.IsProtected || application.ItemKind != ItemKind.Application
                || !RemovableApplications.IsVisible(application))
                return new(results.AsReadOnly(), 0, 0, 0);
            selected.Add(application);
        }
        executor.QuietRequested = request.UseQuietUninstaller;
        try
        {
            foreach (var application in selected)
            {
                if (token.IsCancellationRequested)
                {
                    results.Add(new(application.Id, UninstallState.Cancelled, null, "Stopped waiting; native installer was not terminated"));
                    try { progress?.Report(results[^1]); } catch (Exception) { }
                    continue;
                }
                BulkUninstallItemResult item;
                try
                {
                    // Re-publish a single-use session for this entry; the inner call performs the
                    // same fresh-identity verification and completion detection as a lone removal.
                    var perItem = new ApplicationInventorySession(Guid.NewGuid(), DateTimeOffset.UtcNow,
                        published.Inventory with { Applications = Array.AsReadOnly(published.Inventory.Applications.ToArray()) });
                    executor.PublishedSession = perItem;
                    var outcome = await executor.UninstallAsync(new UninstallRequest(perItem.Id, application.Id), (IProgress<UninstallState>?)null, token).ConfigureAwait(false);
                    item = new(application.Id, outcome.State, outcome.NativeExitCode, outcome.Detail);
                }
                catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                { item = new(application.Id, UninstallState.Failed, null, "Native uninstall could not be completed"); }
                results.Add(item);
                try { progress?.Report(item); } catch (Exception) { }
            }
        }
        finally { executor.QuietRequested = false; }
        var removed = results.Count(r => r.State == UninstallState.Removed);
        var cancelled = results.Count(r => r.State == UninstallState.Cancelled);
        return new(results.AsReadOnly(), removed, results.Count - removed - cancelled, cancelled);
    }
}
