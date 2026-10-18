using System.Text;
using System.Text.Json;
using SystemCore.Core.Cleaner;
using SystemCore.Core.Uninstaller;
using SystemCore.Infrastructure.Cleaner;

namespace SystemCore.Infrastructure.Uninstaller;

public sealed record OrphanDirectory(string Path, string Name, long SizeBytes, int FileCount, string Reason, bool Recommended);

// Finds Program Files directories that belong to no installed application. Read-only and
// recommendation-only: nothing here can delete. A directory is reported as recommended only
// when no registration and no neighbouring file references it, so the UI can rank findings.
public static class OrphanDirectoryScanner
{
    internal const int MaximumDirectories = 500;
    internal const int MaximumFileCountPerDirectory = 20000;

    public static Task<IReadOnlyList<OrphanDirectory>> ScanAsync(InventoryResult inventory, CancellationToken token) => Task.Run(() =>
    {
        var results = new List<OrphanDirectory>();
        if (!inventory.Complete) return (IReadOnlyList<OrphanDirectory>)results.AsReadOnly();
        var claimed = new List<string>();
        foreach (var application in inventory.Applications)
        {
            if (application.InstallLocation is { } location && CleanerSafety.Canonical(location) is { } canonical) claimed.Add(canonical);
            if (application.RegisteredUninstallCommand is { } command && UninstallCommandParser.Parse(command) is { } parsed
                && Path.GetDirectoryName(parsed.Executable) is { } executableDirectory && CleanerSafety.Canonical(executableDirectory) is { } owner)
                claimed.Add(owner);
        }
        var roots = new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(Environment.GetFolderPath).Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var index = 0;
        foreach (var root in roots)
        {
            foreach (var child in Directory.EnumerateDirectories(root).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                if (++index > MaximumDirectories) return (IReadOnlyList<OrphanDirectory>)results.AsReadOnly();
                if (CleanerSafety.Canonical(child) is not { } directory) continue;
                try { if ((File.GetAttributes(directory) & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0) continue; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
                if (LeftoverSafety.SharedNamesList.Contains(Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase)) continue;
                if (IsProtectedSystemDirectory(directory)) continue;
                if (claimed.Any(path => string.Equals(path, directory, StringComparison.OrdinalIgnoreCase) || CleanerSafety.IsChild(directory, path) || CleanerSafety.IsChild(path, directory))) continue;
                long bytes = 0; var files = 0; var readable = true;
                try
                {
                    foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                    {
                        if (++files > MaximumFileCountPerDirectory) break;
                        try { bytes += new FileInfo(file).Length; } catch (IOException) { }
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { readable = false; }
                var reason = readable ? "No installed application claims this directory" : "No installed application claims this directory; size is a lower bound";
                results.Add(new(directory, Path.GetFileName(directory), bytes, files, reason, readable && files > 0));
            }
        }
        return (IReadOnlyList<OrphanDirectory>)results.AsReadOnly();
    }, token);

    private static bool IsProtectedSystemDirectory(string directory)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (CleanerSafety.IsChild(windows, directory) || string.Equals(windows, directory, StringComparison.OrdinalIgnoreCase)) return true;
        var protectedNames = new[] { "WindowsApps", "ModifiableWindowsApps", "Windows Defender", "Windows NT", "Windows Mail", "Windows Media Player",
            "Windows Multimedia Platform", "Windows Photo Viewer", "Windows Portable Devices", "Windows Security", "Windows Sidebar",
            "WindowsPowerShell", "Common Files", "Internet Explorer", "Microsoft", "MSBuild", "Reference Assemblies", "Uninstall Information", "dotnet" };
        return protectedNames.Contains(Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase);
    }
}

public enum UninstallerExportFormat { Csv, Json }

// Exports what the page already shows. Paths are written verbatim so a support case can be
// reproduced; nothing is uploaded and no telemetry is involved.
public static class UninstallerExport
{
    public static async Task<string> WriteInventoryAsync(IReadOnlyList<InstalledApplication> applications, UninstallerExportFormat format, string directory, CancellationToken token)
    {
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, $"F1-inventory-{DateTime.Now:yyyyMMdd-HHmmss}.{(format == UninstallerExportFormat.Csv ? "csv" : "json")}");
        if (format == UninstallerExportFormat.Json)
        {
            await using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, applications.Select(a => new
            {
                a.DisplayName, a.Publisher, a.DisplayVersion, a.InstallDate, a.InstallLocation, a.EstimatedSize,
                Type = a.ApplicationType.ToString(), Source = a.Source.ToString(), a.IsFramework, a.IsSystemComponent,
                a.IsProtected, a.CanUninstall, a.UnavailableReason, a.HasQuietUninstall
            }), new JsonSerializerOptions { WriteIndented = true }, token).ConfigureAwait(false);
            return file;
        }
        var builder = new StringBuilder("DisplayName,Publisher,Version,InstallDate,InstallLocation,SizeBytes,Type,Source,CanUninstall,Quiet\n");
        foreach (var a in applications)
            builder.AppendJoin(',', Csv(a.DisplayName), Csv(a.Publisher), Csv(a.DisplayVersion), Csv(a.InstallDate?.ToString("yyyy-MM-dd")),
                Csv(a.InstallLocation), a.EstimatedSize?.ToString() ?? "", a.ApplicationType, a.Source, a.CanUninstall, a.HasQuietUninstall).Append('\n');
        await File.WriteAllTextAsync(file, builder.ToString(), Encoding.UTF8, token).ConfigureAwait(false);
        return file;
    }

    public static async Task<string> WriteLeftoversAsync(IReadOnlyList<LeftoverItem> items, UninstallerExportFormat format, string directory, CancellationToken token)
    {
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, $"F1-leftovers-{DateTime.Now:yyyyMMdd-HHmmss}.{(format == UninstallerExportFormat.Csv ? "csv" : "json")}");
        if (format == UninstallerExportFormat.Json)
        {
            await using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, items.Select(i => new
            {
                i.DisplayLocation, Type = i.Type.ToString(), Confidence = i.Confidence.ToString(), i.Size,
                i.RequiresElevation, i.Evidence, i.Risk
            }), new JsonSerializerOptions { WriteIndented = true }, token).ConfigureAwait(false);
            return file;
        }
        var builder = new StringBuilder("Location,Type,Confidence,SizeBytes,RequiresElevation,Evidence,Risk\n");
        foreach (var item in items)
            builder.AppendJoin(',', Csv(item.DisplayLocation), item.Type, item.Confidence, item.Size?.ToString() ?? "", item.RequiresElevation, Csv(item.Evidence), Csv(item.Risk)).Append('\n');
        await File.WriteAllTextAsync(file, builder.ToString(), Encoding.UTF8, token).ConfigureAwait(false);
        return file;
    }

    private static string Csv(string? value)
    {
        var text = value ?? "";
        return text.IndexOfAny([',', '"', '\n', '\r']) < 0 ? text : '"' + text.Replace("\"", "\"\"") + '"';
    }
}
