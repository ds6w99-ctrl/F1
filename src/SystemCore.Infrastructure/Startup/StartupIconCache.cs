using SystemCore.Core.Startup;
using SystemCore.Infrastructure.Cleaner;
using SystemCore.Infrastructure.Uninstaller;

namespace SystemCore.Infrastructure.Startup;

public sealed record StartupIconResult(byte[]? Png, string Source)
{
    public static StartupIconResult Fallback { get; } = new(null, "Fallback");
}

// Resource bytes only, no ImageSource/thread affinity and no execution. A single
// bounded worker also coalesces repeated requests for the same resource identity.
public sealed class StartupIconCache
{
    private readonly SemaphoreSlim worker = new(1, 1);
    private readonly Dictionary<string, (StartupIconResult Result, long Used)> cache = new(StringComparer.Ordinal);
    private readonly Func<IconFile, CancellationToken, Task<byte[]?>> extract;
    private long clock;
    public StartupIconCache() : this(ApplicationIconReader.ReadFileAsync) { }
    internal StartupIconCache(Func<IconFile, CancellationToken, Task<byte[]?>> extract) => this.extract = extract;
    internal int Count => cache.Count;

    public async Task<StartupIconResult> GetAsync(StartupItem item, CancellationToken token)
    {
        await worker.WaitAsync(token).ConfigureAwait(false);
        try { return await Task.Run(() => ReadAsync(item, token), token).ConfigureAwait(false); }
        finally { worker.Release(); }
    }

    private async Task<StartupIconResult> ReadAsync(StartupItem item, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (!ManageableStartupItems.HasSupportedState(item)) return StartupIconResult.Fallback;
            var candidates = new List<(IconFile File, string Source)>();
            var parsed = StartupCommandParser.Read(item.CommandDisplay);
            if (parsed.Executable is null || parsed.Executable != item.ExecutablePath) return StartupIconResult.Fallback;
            if (item.SourceType is StartupSourceType.StartupFolderUser or StartupSourceType.StartupFolderCommon
                && Path.GetExtension(item.SourceLocation).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                && File.Exists(item.SourceLocation))
            {
                var shortcut = StartupFolderProvider.ReadShortcutMetadata(item.SourceLocation);
                if (StartupCommandParser.Read(shortcut.Command).Executable != parsed.Executable) return StartupIconResult.Fallback;
                if (ApplicationIconReader.Parse($"\"{shortcut.Icon}\",{shortcut.Index}") is { } icon) candidates.Add((icon, "ShortcutIcon"));
            }
            candidates.Add((new(parsed.Executable, 0), item.SourceType switch
            {
                StartupSourceType.RegistryRun or StartupSourceType.RegistryRunOnce => "Registry",
                StartupSourceType.ScheduledTaskLogon or StartupSourceType.ScheduledTaskBoot => "ScheduledTask",
                _ => "StartupFolderTarget"
            }));
            // Include every candidate's file identity so a formerly missing icon,
            // replacement or shortcut metadata change invalidates negative cache too.
            var key = string.Join("|", candidates.Select(c => $"{c.Source}:{c.File.Path}:{c.File.Index}:{Stamp(c.File.Path)}"));
            if (cache.TryGetValue(key, out var found)) { cache[key] = (found.Result, ++clock); return found.Result; }
            var result = StartupIconResult.Fallback;
            foreach (var candidate in candidates)
            {
                token.ThrowIfCancellationRequested();
                if (!ApplicationIconReader.SafePath(candidate.File.Path)) continue;
                var bytes = await extract(candidate.File, token).ConfigureAwait(false);
                if (bytes is { Length: > 0 and <= 65536 }) { result = new(bytes, candidate.Source); break; }
            }
            token.ThrowIfCancellationRequested();
            if (cache.Count >= 128) cache.Remove(cache.MinBy(p => p.Value.Used).Key);
            cache[key] = (result, ++clock);
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
            or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception) { return StartupIconResult.Fallback; }
    }

    private static string Stamp(string path)
    {
        if (!ApplicationIconReader.SafePath(path)) return "unavailable";
        using var pinned = PinnedCleanerPath.Open(path, false, shareRead: true);
        return pinned.Identity.ToString();
    }
}
