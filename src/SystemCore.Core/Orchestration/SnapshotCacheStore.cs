using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;

namespace SystemCore.Core.Orchestration;

public sealed partial class ApplicationDataOrchestrator
{
    private readonly ConcurrentDictionary<string, CachedSummary> summaries = new();
    private readonly ConcurrentDictionary<DataDataset, DateTimeOffset> summaryTimes = new();
    private sealed record CachedSummary(object Value, DateTimeOffset WrittenAt);

    private sealed class SnapshotCacheStore(string rootDirectory)
    {
        public const int CurrentSchemaVersion = 1;
        private static readonly object FileSync = new();
        public string RootDirectory { get; } = rootDirectory;
        private sealed record Envelope<T>([property: JsonRequired] int SchemaVersion,
            [property: JsonRequired] DateTimeOffset WrittenAt, [property: JsonRequired] T Value);

        public bool TryRead<T>(string key, out T? value, out DateTimeOffset writtenAt, TimeSpan? maxAge = null)
        {
            value = default; writtenAt = default;
            try
            {
                lock (FileSync)
                {
                    var path = PathFor(key, ".json");
                    if (path is null || !File.Exists(path) || new FileInfo(path).Length > 65536 || IsLink(path)) return false;
                    var envelope = JsonSerializer.Deserialize<Envelope<T>>(File.ReadAllBytes(path));
                    var now = DateTimeOffset.UtcNow;
                    if (envelope is null || envelope.SchemaVersion != CurrentSchemaVersion || envelope.Value is null ||
                        envelope.WrittenAt == default || envelope.WrittenAt > now ||
                        (maxAge.HasValue && (maxAge <= TimeSpan.Zero || now - envelope.WrittenAt >= maxAge))) return false;
                    value = envelope.Value; writtenAt = envelope.WrittenAt; return true;
                }
            }
            catch { return false; }
        }

        public void Write<T>(string key, T value)
        {
            try
            {
                lock (FileSync)
                {
                    var path = PathFor(key, ".json"); var temporary = PathFor(key, ".tmp");
                    if (path is null || temporary is null || value is null || IsLink(path) || IsLink(temporary)) return;
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope<T>(CurrentSchemaVersion, DateTimeOffset.UtcNow, value));
                    if (bytes.Length > 65536) return;
                    Directory.CreateDirectory(RootDirectory);
                    // Serialize first, and never touch the previous good payload until a complete temp write exists.
                    using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    { stream.Write(bytes); stream.Flush(true); }
                    File.Move(temporary, path, overwrite: true);
                }
            }
            catch { }
        }

        private string? PathFor(string key, string extension)
        {
            // Reject rather than normalize: distinct invalid keys must not alias a valid summary.
            if (string.IsNullOrEmpty(key) || key.Length > 64 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) return null;
            var root = Path.GetFullPath(RootDirectory);
            for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) return null;
            return Path.Combine(root, key + extension);
        }
        private static bool IsLink(string path) => (File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    public DashboardSummarySnapshot? CacheDashboardSummary(DashboardSummarySnapshot? value = null, TimeSpan? maxAge = null) =>
        CacheSummary("dashboard", value, maxAge ?? TimeSpan.FromMinutes(15));
    public SystemInformationSummarySnapshot? CacheSystemInformationSummary(SystemInformationSummarySnapshot? value = null, TimeSpan? maxAge = null) =>
        CacheSummary("system-information", value, maxAge ?? TimeSpan.FromDays(1));
    public ApplicationsSummarySnapshot? CacheApplicationsSummary(ApplicationsSummarySnapshot? value = null, TimeSpan? maxAge = null) =>
        CacheSummary("applications", value, maxAge ?? TimeSpan.FromMinutes(15));
    public StartupSummarySnapshot? CacheStartupSummary(StartupSummarySnapshot? value = null, TimeSpan? maxAge = null) =>
        CacheSummary("startup", value, maxAge ?? TimeSpan.FromMinutes(15));
    public CleanerSummarySnapshot? CacheCleanerSummary(CleanerSummarySnapshot? value = null, TimeSpan? maxAge = null) =>
        CacheSummary("cleaner", value is { Complete: false } ? null : value, maxAge ?? CleanerStaleThreshold);

    private T? CacheSummary<T>(string key, T? value, TimeSpan maxAge) where T : class
    {
        CachedSummary? summary;
        if (value is not null || !summaries.TryGetValue(key, out summary))
        {
            if (value is not null) cache.Write(key, value);
            if (!cache.TryRead<T>(key, out var result, out var written)) return null;
            summary = new(result!, written); summaries[key] = summary;
            foreach (var dataset in SummaryDatasets(key)) summaryTimes[dataset] = written;
        }
        var age = DateTimeOffset.UtcNow - summary.WrittenAt;
        return age >= TimeSpan.Zero && age < maxAge ? (T)summary.Value : null;
    }

    private static DataDataset[] SummaryDatasets(string key) => key switch
    {
        "dashboard" => [DataDataset.Dashboard], "system-information" => [DataDataset.SystemInformation],
        "applications" => [DataDataset.InstalledApplications, DataDataset.Games], "startup" => [DataDataset.Startup],
        "cleaner" => [DataDataset.Cleaner], _ => []
    };
}
