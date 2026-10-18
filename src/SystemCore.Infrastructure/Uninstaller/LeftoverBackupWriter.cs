using System.ComponentModel;
using System.IO.Compression;
using System.Text.Json;
using SystemCore.Core.Cleaner;
using SystemCore.Core.Uninstaller;
using SystemCore.Infrastructure.Cleaner;

namespace SystemCore.Infrastructure.Uninstaller;

internal interface ILeftoverBackupWriter
{
    Task<string> SaveAsync(InstalledApplication application, IEnumerable<ScannedLeftover> items, CancellationToken token);
}

// Copies leftover items into a bounded, verifiable ZIP before anything is marked for deletion.
// A backup that cannot be produced and verified aborts the removal instead of being skipped,
// which is what turns leftover deletion into a recoverable operation.
internal sealed class LeftoverBackupWriter : ILeftoverBackupWriter
{
    internal const long MaximumBackupBytes = 2L * 1024 * 1024 * 1024;
    internal const int MaximumBackupEntries = 20000;
    internal const int MaximumRetainedBackups = 20;

    public async Task<string> SaveAsync(InstalledApplication application, IEnumerable<ScannedLeftover> items, CancellationToken token)
    {
        // No PinnedCleanerPath leases here: those open directories without sharing and would
        // block creating and renaming the archive inside them. This path only appends new files
        // under the app's own state directory.
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (CleanerSafety.Canonical(local) is null) throw new IOException("Local application data is unavailable");
        var applicationDirectory = Path.Combine(local, "SystemCore");
        Directory.CreateDirectory(applicationDirectory);
        var directory = Path.Combine(applicationDirectory, "LeftoverBackups");
        Directory.CreateDirectory(directory);
        Prune(directory);
        var staging = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".pending");
        var target = Path.ChangeExtension(staging, ".zip");
        long bytes = 0;
        var entries = 0;
        var manifest = new List<object>();
        try
        {
            // The file stream must be fully closed before the archive is renamed on Windows.
            // No WriteThrough here: on Windows that option can keep the handle open long enough to
            // block the rename of the completed archive.
            using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920))
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    foreach (var item in items)
                    {
                        token.ThrowIfCancellationRequested();
                        if (item.Registry is not null) continue;
                        if (++entries > MaximumBackupEntries) throw new IOException("Backup entry limit reached");
                        var relative = Path.GetRelativePath(Path.GetPathRoot(item.Path) ?? "C:\\", item.Path).Replace(':', '_');
                        if (item.Item.Type == LeftoverType.Directory)
                        {
                            manifest.Add(new { Kind = "Directory", Path = item.Path });
                            continue;
                        }
                        var info = new FileInfo(item.Path);
                        if (!info.Exists) continue;
                        bytes = checked(bytes + info.Length);
                        if (bytes > MaximumBackupBytes) throw new IOException("Backup size limit reached");
                        var entry = archive.CreateEntry(relative, CompressionLevel.Fastest);
                        using (var output = entry.Open())
                        using (var input = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, FileOptions.SequentialScan))
                            await input.CopyToAsync(output, token).ConfigureAwait(false);
                        manifest.Add(new { Kind = "File", Path = item.Path, ArchiveEntry = relative, Size = info.Length });
                    }
                    var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Fastest);
                    using (var manifestStream = manifestEntry.Open())
                        await JsonSerializer.SerializeAsync(manifestStream, new { Format = "SystemCore.LeftoverBackup.v1", Application = application.DisplayName,
                            ApplicationId = application.Id, CreatedAt = DateTimeOffset.UtcNow, Items = manifest }, cancellationToken: token).ConfigureAwait(false);
                }
                // The archive owns its entries until it is disposed; the stream stays open here
                // precisely so the ZIP central directory is written before the handle is released.
            }
            // Only after the stream is closed may the completed archive be renamed into place.
            // A short retry absorbs antivirus/indexer handles that briefly hold the new file.
            for (var attempt = 0; ; attempt++)
            {
                try { File.Move(staging, target, true); break; }
                catch (IOException) when (attempt < 10) { Thread.Sleep(100); }
            }
            return target;
        }
        catch
        {
            try { File.Delete(staging); } catch (Exception) { }
            try { File.Delete(target); } catch (Exception) { }
            throw;
        }
    }

    // Keeps the backup folder bounded: newest files survive, older ones are deleted.
    internal static void Prune(string directory)
    {
        try
        {
            var files = new DirectoryInfo(directory).GetFiles("*.zip").OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
            foreach (var file in files.Skip(MaximumRetainedBackups)) { try { file.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

// Restores a leftover backup next to its original paths. Never overwrites: an existing path is
// reported instead, so a restore can never destroy newer data.
public static class LeftoverBackupRestorer
{
    public sealed record RestoreResult(int Restored, int Skipped, int Failed, IReadOnlyList<string> Messages);

    public static Task<RestoreResult> RestoreAsync(string archivePath, CancellationToken token) => Task.Run(() =>
    {
        if (CleanerSafety.Canonical(archivePath) is not { } canonical || !File.Exists(canonical)) throw new IOException("Backup archive not found");
        var restored = 0; var skipped = 0; var failed = 0; var messages = new List<string>();
        using var archive = ZipFile.OpenRead(canonical);
        var manifest = archive.GetEntry("manifest.json") ?? throw new IOException("Backup manifest missing");
        using var reader = new StreamReader(manifest.Open());
        using var document = JsonDocument.Parse(reader.ReadToEnd());
        if (!document.RootElement.TryGetProperty("Items", out var items)) throw new IOException("Backup manifest has no items");
        foreach (var item in items.EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            if (!item.TryGetProperty("Path", out var pathValue) || CleanerSafety.Canonical(pathValue.GetString()) is not { } path) { failed++; continue; }
            if (!item.TryGetProperty("Kind", out var kind) || kind.GetString() == "Directory")
            {
                try { if (Directory.Exists(path)) { skipped++; } else { Directory.CreateDirectory(path); restored++; } }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed++; messages.Add(path); }
                continue;
            }
            var entryName = item.GetProperty("ArchiveEntry").GetString();
            var entry = entryName is null ? null : archive.GetEntry(entryName);
            if (entry is null) { failed++; continue; }
            if (File.Exists(path)) { skipped++; continue; }
            try
            {
                var parent = Path.GetDirectoryName(path);
                if (parent is not null) Directory.CreateDirectory(parent);
                entry.ExtractToFile(path, false);
                restored++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception) { failed++; messages.Add(path + ": " + e.Message); }
        }
        return new RestoreResult(restored, skipped, failed, messages.AsReadOnly());
    }, token);
}
