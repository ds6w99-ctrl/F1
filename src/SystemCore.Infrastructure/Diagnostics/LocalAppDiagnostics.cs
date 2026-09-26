using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using SystemCore.Core.Diagnostics;
using SystemCore.Core.Tools;
using SystemCore.Infrastructure.Cleaner;

namespace SystemCore.Infrastructure.Diagnostics;

public sealed record DiagnosticLimits(int Files = 10, int FileBytes = 2 * 1024 * 1024)
{
    public long TotalBytes => (long)Files * FileBytes;
}
public sealed class LocalAppDiagnostics : IAppDiagnostics
{
    private static readonly byte[] Header = Encoding.UTF8.GetBytes("{\"Format\":\"SystemCore.Log.v1\"}\n");
    private static readonly JsonSerializerOptions LogJson = new() { Converters = { new JsonStringEnumConverter<DiagnosticFeature>(), new JsonStringEnumConverter<DiagnosticEvent>() } };
    private static readonly Regex LogName = new("^systemcore-[0-9]{17}-[a-f0-9]{32}\\.jsonl$", RegexOptions.CultureInvariant);
    private readonly string data, logs, exports, version;
    private readonly IWindowsToolsService tools;
    private readonly DiagnosticLimits limits;
    private readonly object sync = new();
    private readonly Mutex mutex;
    private readonly Channel<(ApplicationLogRecord? Record, TaskCompletionSource? Fence)> queue = Channel.CreateBounded<(ApplicationLogRecord?, TaskCompletionSource?)>(new BoundedChannelOptions(128) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Task worker;
    private FileStream? stream;
    private string? current;
    private bool available = true, disposed;
    private long dropped;
    public LocalAppDiagnostics(string applicationData, string appVersion, IWindowsToolsService tools, DiagnosticLimits? limits = null)
    {
        data = Path.GetFullPath(applicationData); logs = Path.Combine(data, "Logs"); exports = Path.Combine(data, "Exports");
        version = Regex.IsMatch(appVersion, "^[0-9A-Za-z.+-]{1,64}$") ? appVersion : "unknown";
        this.tools = tools; this.limits = limits ?? new();
        if (this.limits.Files is < 2 or > 10 || this.limits.FileBytes is < 256 or > 2 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(limits));
        mutex = new(false, "Local\\SystemCore.Diagnostics." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data.ToUpperInvariant()))));
        worker = Task.Run(RunAsync);
    }
    public void Record(DiagnosticFeature feature, DiagnosticEvent action, int code = 0)
    {
        if (!Enum.IsDefined(feature) || !Enum.IsDefined(action) || disposed) return;
        if (!queue.Writer.TryWrite((new(DateTimeOffset.UtcNow, feature, action, code), null))) Interlocked.Increment(ref dropped);
    }
    public async Task FlushAsync(CancellationToken token = default)
    {
        if (disposed) return;
        var fence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await queue.Writer.WriteAsync((null, fence), token).ConfigureAwait(false);
        await fence.Task.WaitAsync(token).ConfigureAwait(false);
    }
    private async Task RunAsync()
    {
        await foreach (var item in queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (item.Record is { } record)
            {
                try { Locked(() => { Append(record); return true; }); }
                catch (Exception e) when (StorageFailure(e)) { available = false; Interlocked.Increment(ref dropped); }
            }
            item.Fence?.TrySetResult();
        }
    }
    private T Locked<T>(Func<T> action, bool namespaceWrite = false)
    {
        lock (sync)
        {
            var held = false;
            try
            {
                try { held = mutex.WaitOne(TimeSpan.FromSeconds(2)); } catch (AbandonedMutexException) { held = true; }
                if (!held) throw new IOException("Diagnostics busy");
                using var parent = PinnedCleanerPath.Open(Path.GetDirectoryName(data)!, true, namespaceWrite: namespaceWrite);
                Directory.CreateDirectory(data);
                using var root = PinnedCleanerPath.Open(data, true, namespaceWrite: namespaceWrite);
                Directory.CreateDirectory(logs); Directory.CreateDirectory(exports);
                using var logRoot = PinnedCleanerPath.Open(logs, true, namespaceWrite: namespaceWrite);
                using var exportRoot = PinnedCleanerPath.Open(exports, true, namespaceWrite: namespaceWrite);
                return action();
            }
            finally { if (held) mutex.ReleaseMutex(); }
        }
    }
    private FileInfo[] Files()
    {
        var files = new DirectoryInfo(logs).EnumerateFiles("systemcore-*.jsonl", SearchOption.TopDirectoryOnly).Where(f => LogName.IsMatch(f.Name)).Take(129).ToArray();
        if (files.Length > 128 || files.Any(f => (f.Attributes & FileAttributes.ReparsePoint) != 0 || f.Length > limits.FileBytes)) throw new IOException("Log scope unavailable");
        return files.OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
    }
    private void Append(ApplicationLogRecord record)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, LogJson);
        if (bytes.Length + Header.Length + 1 > limits.FileBytes) throw new IOException("Log record too large");
        if (stream is null || stream.Length + bytes.Length + 1 > limits.FileBytes) Rotate();
        stream!.Write(bytes); stream.WriteByte((byte)'\n'); stream.Flush(); available = true;
    }
    private void Rotate()
    {
        stream?.Dispose(); stream = null; current = null;
        var files = Files().ToList();
        while (files.Count >= limits.Files || files.Sum(f => f.Length) + limits.FileBytes > limits.TotalBytes)
        {
            var candidate = files.FirstOrDefault(f => DeleteVerified(f.FullName));
            if (candidate is null) throw new IOException("Log retention blocked");
            files.Remove(candidate);
        }
        current = Path.Combine(logs, $"systemcore-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.jsonl");
        stream = new(current, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.None);
        stream.Write(Header); stream.Flush();
    }
    private static bool HasHeader(Microsoft.Win32.SafeHandles.SafeFileHandle handle)
    {
        var bytes = new byte[Header.Length];
        return RandomAccess.Read(handle, bytes, 0) == bytes.Length && bytes.AsSpan().SequenceEqual(Header);
    }
    private static bool DeleteVerified(string path)
    {
        try
        {
            using var lease = PinnedCleanerPath.Open(path, false, deleteAccess: true);
            if (lease.Identity.Links != 1 || !HasHeader(lease.Leaf)) return false;
            lease.MarkFileForDeletion(); return true;
        }
        catch (Exception e) when (StorageFailure(e)) { return false; }
    }
    public DiagnosticsStatus GetStatus()
    {
        try { return Locked(() => { var files = Files(); return new DiagnosticsStatus(available, files.Length, files.Sum(f => f.Length), Interlocked.Read(ref dropped)); }); }
        catch (Exception e) when (StorageFailure(e)) { return new(false, 0, 0, Interlocked.Read(ref dropped)); }
    }
    public async Task<DiagnosticOperation> ClearOldAsync(CancellationToken token)
    {
        await FlushAsync(token).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            try
            {
                return Locked(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var old = Files().Where(f => f.FullName != current).ToArray(); var count = 0;
                    foreach (var file in old) { token.ThrowIfCancellationRequested(); if (DeleteVerified(file.FullName)) count++; }
                    Record(DiagnosticFeature.Diagnostics, DiagnosticEvent.LogsCleared, count);
                    return new DiagnosticOperation(count == old.Length, count);
                });
            }
            catch (Exception e) when (StorageFailure(e)) { return new(false); }
        }, token).ConfigureAwait(false);
    }
    public async Task<DiagnosticOperation> ExportAsync(CancellationToken token)
    {
        await FlushAsync(token).ConfigureAwait(false);
        var availability = await tools.ReadAsync(token).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            try
            {
                return Locked(() =>
                {
                    if (Directory.EnumerateFiles(exports, "SystemCore-Diagnostics-*.zip").Take(5).Count() >= 5) return new DiagnosticOperation(false);
                    if (Directory.EnumerateFiles(exports, "*.pending").Take(5).Count() >= 5) return new DiagnosticOperation(false);
                    token.ThrowIfCancellationRequested();
                    var fileName = $"SystemCore-Diagnostics-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip";
                    using var directory = Startup.StartupFileMove.Open(exports, true);
                    using var output = Startup.StartupFileMove.CreateChild(directory, Guid.NewGuid().ToString("N") + ".pending");
                    var committed = false;
                    try
                    {
                    var records = 0;
                    using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                    {
                        using (var entry = archive.CreateEntry("manifest.json").Open()) JsonSerializer.Serialize(entry, new { Format = "SystemCore.Diagnostics.v1", AppVersion = version,
                            WindowsVersion = Environment.OSVersion.Version.ToString(), Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
                            FeatureAvailability = availability.Select(t => new { Tool = t.Id.ToString(), Status = t.Availability.ToString() }),
                            Redaction = "Allowlisted enums, numeric error/event codes and timestamps only; no event payload strings or private identifiers. Invalid or locked log records are omitted.", DroppedRecords = Interlocked.Read(ref dropped) });
                        using var writer = new StreamWriter(archive.CreateEntry("logs/recent.jsonl").Open(), new UTF8Encoding(false));
                        foreach (var file in Files())
                        {
                            token.ThrowIfCancellationRequested();
                            foreach (var record in ReadRedacted(file.FullName)) { writer.WriteLine(JsonSerializer.Serialize(record, LogJson)); records++; }
                        }
                    }
                    output.Flush(true);
                    output.Position = 0;
                    using (var check = new ZipArchive(output, ZipArchiveMode.Read, leaveOpen: true)) if (check.Entries.Count != 2) throw new IOException("Export verification failed");
                    token.ThrowIfCancellationRequested();
                    Startup.StartupFileMove.MoveHandle(output.SafeFileHandle, directory, fileName, replaceState: false);
                    committed = true;
                    Record(DiagnosticFeature.Diagnostics, DiagnosticEvent.Exported, records);
                    return new DiagnosticOperation(true, records, fileName);
                    }
                    finally { if (!committed) Startup.StartupFileMove.DiscardPending(output.SafeFileHandle); }
                }, namespaceWrite: true);
            }
            catch (Exception e) when (StorageFailure(e)) { return new(false); }
        }, token).ConfigureAwait(false);
    }
    private IReadOnlyList<ApplicationLogRecord> ReadRedacted(string path)
    {
        try
        {
            byte[] bytes;
            if (path == current && stream is not null)
            {
                stream.Flush(); bytes = new byte[checked((int)stream.Length)];
                ReadExactly(stream.SafeFileHandle, bytes);
            }
            else
            {
                using var lease = PinnedCleanerPath.Open(path, false, shareRead: true);
                if (lease.Identity.Links != 1 || lease.Identity.Size > limits.FileBytes || !HasHeader(lease.Leaf)) return [];
                bytes = new byte[checked((int)lease.Identity.Size)]; ReadExactly(lease.Leaf, bytes);
            }
            var result = new List<ApplicationLogRecord>();
            foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n'))
            {
                if (line.Length is 0 or > 1024) continue;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Timestamp", out _) || !root.TryGetProperty("Feature", out _) || !root.TryGetProperty("Event", out _) || !root.TryGetProperty("Code", out _)) continue;
                    var record = JsonSerializer.Deserialize<ApplicationLogRecord>(line, LogJson);
                    if (record is not null && record.Timestamp != default && Enum.IsDefined(record.Feature) && Enum.IsDefined(record.Event)) result.Add(record);
                }
                catch (JsonException) { }
            }
            return result;
        }
        catch (Exception e) when (StorageFailure(e)) { return []; }
    }
    private static void ReadExactly(Microsoft.Win32.SafeHandles.SafeFileHandle handle, byte[] bytes)
    {
        var offset = 0;
        while (offset < bytes.Length) { var count = RandomAccess.Read(handle, bytes.AsSpan(offset), offset); if (count == 0) throw new EndOfStreamException(); offset += count; }
    }
    public async Task<bool> OpenFolderAsync(DiagnosticFolder folder, CancellationToken token)
    {
        if (!Enum.IsDefined(folder)) return false;
        try
        {
            var path = Locked(() => folder switch { DiagnosticFolder.Logs => logs, DiagnosticFolder.Exports => exports, _ => data });
            var directory = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(path).AsTask(token).ConfigureAwait(false);
            return await Windows.System.Launcher.LaunchFolderAsync(directory).AsTask(token).ConfigureAwait(false);
        }
        catch (Exception e) when (StorageFailure(e)) { return false; }
    }
    private static bool StorageFailure(Exception e) => e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or System.Security.SecurityException;
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; queue.Writer.TryComplete(); await worker.ConfigureAwait(false);
        lock (sync) { stream?.Dispose(); stream = null; mutex.Dispose(); }
    }
}
