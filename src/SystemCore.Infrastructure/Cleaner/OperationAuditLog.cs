using System.Text.Json;
using SystemCore.Core.Cleaner;

namespace SystemCore.Infrastructure.Cleaner;

public sealed record AuditEntry(DateTimeOffset Timestamp, string Operation, string Target, string Outcome, long Bytes, string? Detail);

// Append-only local audit trail. Every removal the app performs is recorded so a user can see
// what happened and when, and correlate it with the backup archives that were written first.
// Bounded (maximum entries, maximum file size) and never uploaded.
public sealed class OperationAuditLog
{
    internal const int MaximumEntries = 2000;
    internal const long MaximumFileBytes = 4L * 1024 * 1024;
    private readonly object sync = new();
    public string FilePath { get; }
    public OperationAuditLog() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemCore", "Audit", "operations.jsonl")) { }
    public OperationAuditLog(string path) { FilePath = path; }

    public void Record(string operation, string target, string outcome, long bytes = 0, string? detail = null)
    {
        try
        {
            lock (sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                Rotate();
                var entry = new AuditEntry(DateTimeOffset.UtcNow, operation, target, outcome, bytes, detail);
                File.AppendAllText(FilePath, JsonSerializer.Serialize(entry) + "\n");
            }
        }
        // Auditing must never break the operation it describes.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
    }

    public IReadOnlyList<AuditEntry> Read(int maximum = 500)
    {
        var entries = new List<AuditEntry>();
        try
        {
            lock (sync)
            {
                if (!File.Exists(FilePath)) return entries.AsReadOnly();
                foreach (var line in File.ReadLines(FilePath).TakeLast(Math.Max(1, maximum)))
                {
                    try { if (JsonSerializer.Deserialize<AuditEntry>(line) is { } entry) entries.Add(entry); }
                    catch (JsonException) { }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return entries.AsReadOnly();
    }

    // Keeps the trail bounded: old lines are dropped from the front once the limits are reached.
    private void Rotate()
    {
        var info = new FileInfo(FilePath);
        if (!info.Exists) return;
        if (info.Length <= MaximumFileBytes) return;
        var lines = File.ReadLines(FilePath).TakeLast(MaximumEntries).ToArray();
        File.WriteAllLines(FilePath, lines);
    }

    // Records a completed clean result item-by-item, one place so every caller is consistent.
    public void RecordCleanResult(CleanerCategory category, CleanerCleanResult result)
    {
        foreach (var item in result.Items)
        {
            if (item.Outcome == CleanerItemOutcome.Deleted) Record("clean", category.ToString(), "Deleted", item.RemovedBytes);
            else if (item.Outcome != CleanerItemOutcome.AlreadyGone) Record("clean", category.ToString(), item.Outcome.ToString(), 0);
        }
        if (result.RecycleBin is { } bin) Record("recycle-bin", "RecycleBin", bin.ToString(), 0);
    }
}
