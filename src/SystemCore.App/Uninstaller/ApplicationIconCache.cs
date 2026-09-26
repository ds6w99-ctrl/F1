using SystemCore.Core.Uninstaller;
using SystemCore.Infrastructure.Uninstaller;

namespace SystemCore.App.Uninstaller;

// Bytes only: native ImageSource objects belong to their realized UI containers.
internal sealed class ApplicationIconCache
{
    private readonly SemaphoreSlim workers = new(2);
    private readonly Dictionary<string, (byte[]? Bytes, DateTime Used)> cache = [];
    private static string Key(InstalledApplication app) => $"{app.Id}|{app.IconLocation}|{app.DisplayVersion}|{app.InstallLocation}|{app.RegisteredUninstallCommand}|{app.ManifestExecutable}|{app.LauncherIcon}";
    public void Refresh(IReadOnlyList<InstalledApplication> apps)
    {
        var keys = apps.Select(Key).ToHashSet(StringComparer.Ordinal);
        lock (cache)
            foreach (var key in cache.Keys.Where(k => !keys.Contains(k) || cache[k].Bytes is null || DateTime.UtcNow - cache[k].Used > TimeSpan.FromMinutes(5)).ToArray()) cache.Remove(key);
    }
    public async Task<byte[]?> GetAsync(InstalledApplication app, CancellationToken token)
    {
        await workers.WaitAsync(token);
        try
        {
            var key = Key(app);
            lock (cache) if (cache.TryGetValue(key, out var found) && DateTime.UtcNow - found.Used < TimeSpan.FromMinutes(5)) return found.Bytes;
            var bytes = await Task.Run(() => ApplicationIconReader.ReadAsync(app, token), token);
            if (bytes?.Length > 64 * 1024) bytes = null;
            token.ThrowIfCancellationRequested();
            lock (cache)
            {
                if (cache.Count >= 128) cache.Remove(cache.MinBy(p => p.Value.Used).Key);
                cache[key] = (bytes, DateTime.UtcNow);
            }
            return bytes;
        }
        finally { workers.Release(); }
    }
}
