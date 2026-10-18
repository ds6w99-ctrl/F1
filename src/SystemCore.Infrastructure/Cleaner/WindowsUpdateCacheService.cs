using SystemCore.Core.Cleaner;

namespace SystemCore.Infrastructure.Cleaner;

public sealed record WindowsUpdateCacheSnapshot(bool Available, long Bytes, long Files, string Reason);

public interface IWindowsUpdateCacheService
{
    Task<WindowsUpdateCacheSnapshot> QueryAsync(CancellationToken token);
}

// Windows Update download cache. Only the download payload folder is offered, never the
// servicing state database, and the folder is never deleted wholesale: individual files are
// eligible, so an update that is being installed is refused by sharing checks rather than by
// guesswork. Requires elevation, like every Windows tree.
public sealed class WindowsUpdateCacheService : IWindowsUpdateCacheService
{
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");

    public Task<WindowsUpdateCacheSnapshot> QueryAsync(CancellationToken token) => Task.Run(() =>
    {
        try
        {
            if (!Directory.Exists(Root)) return new WindowsUpdateCacheSnapshot(false, 0, 0, "Download cache not present");
            long bytes = 0, files = 0;
            var missing = false;
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                files++;
                try { bytes += new FileInfo(file).Length; } catch (IOException) { missing = true; }
            }
            return new WindowsUpdateCacheSnapshot(true, bytes, files, missing ? "Some entries could not be read" : "");
        }
        catch (UnauthorizedAccessException) { return new WindowsUpdateCacheSnapshot(false, 0, 0, "Elevation required"); }
        catch (Exception e) when (e is IOException or System.Security.SecurityException) { return new WindowsUpdateCacheSnapshot(false, 0, 0, "Unavailable"); }
    }, token);
}
