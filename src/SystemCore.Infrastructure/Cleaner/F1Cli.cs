using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using SystemCore.Core.Cleaner;
using SystemCore.Core.Uninstaller;

namespace SystemCore.Infrastructure.Cleaner;

// HTML report for the inventory and leftover views. Self-contained, no scripts, no external
// resources, and every value is HTML-encoded, so a hostile application name cannot inject markup.
public static class HtmlReport
{
    public static string InventoryReport(IReadOnlyList<InstalledApplication> applications, DateTimeOffset generated)
    {
        var builder = new StringBuilder();
        Header(builder, "F1 inventory", generated);
        builder.Append("<table><thead><tr><th>Name</th><th>Publisher</th><th>Version</th><th>Type</th><th>Size</th><th>Install location</th></tr></thead><tbody>");
        foreach (var app in applications.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            builder.Append("<tr><td>").Append(E(app.DisplayName)).Append("</td><td>").Append(E(app.Publisher)).Append("</td><td>")
                .Append(E(app.DisplayVersion)).Append("</td><td>").Append(E(app.ApplicationType.ToString())).Append("</td><td>")
                .Append(app.EstimatedSize is { } size ? Size(size) : "").Append("</td><td class=\"path\">").Append(E(app.InstallLocation)).Append("</td></tr>");
        builder.Append("</tbody></table></body></html>");
        return builder.ToString();
    }

    public static string LeftoverReport(IReadOnlyList<LeftoverItem> items, DateTimeOffset generated)
    {
        var builder = new StringBuilder();
        Header(builder, "F1 leftover scan", generated);
        builder.Append("<table><thead><tr><th>Location</th><th>Type</th><th>Confidence</th><th>Size</th><th>Evidence</th></tr></thead><tbody>");
        foreach (var item in items)
            builder.Append("<tr><td class=\"path\">").Append(E(item.DisplayLocation)).Append("</td><td>").Append(E(item.Type.ToString()))
                .Append("</td><td>").Append(E(item.Confidence.ToString())).Append("</td><td>").Append(item.Size is { } size ? Size(size) : "")
                .Append("</td><td>").Append(E(item.Evidence)).Append("</td></tr>");
        builder.Append("</tbody></table></body></html>");
        return builder.ToString();
    }

    private static void Header(StringBuilder builder, string title, DateTimeOffset generated) =>
        builder.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>").Append(E(title))
            .Append("</title><style>body{font-family:Segoe UI,sans-serif;margin:24px}table{border-collapse:collapse;width:100%}")
            .Append("th,td{border:1px solid #ccc;padding:6px;text-align:left;font-size:13px}th{background:#f0f4f8}")
            .Append(".path{font-family:Consolas,monospace;direction:ltr}</style></head><body><h1>").Append(E(title))
            .Append("</h1><p>Generated ").Append(generated.ToString("u", CultureInfo.InvariantCulture)).Append(" — local report, nothing uploaded.</p>");

    private static string Size(long bytes) => bytes switch
    {
        < 1024 => bytes + " B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("N1", CultureInfo.InvariantCulture) + " KB",
        < 1024L * 1024 * 1024 => (bytes / 1048576.0).ToString("N1", CultureInfo.InvariantCulture) + " MB",
        _ => (bytes / 1073741824.0).ToString("N2", CultureInfo.InvariantCulture) + " GB"
    };

    private static string E(string? value) => value is null ? "" : HtmlEncoder.Default.Encode(value);
}

// Command-line surface for scripted use. Read-only by design: it can scan and report, and it
// refuses every mutation command, so an automated caller can never delete through the CLI.
public static class F1Cli
{
    public const int Success = 0, InvalidArguments = 2, CommandRefused = 3, Failed = 4;

    public static async Task<int> RunAsync(string[] args, Func<CancellationToken, Task<CleanerScanSession>> cleanerScan,
        Func<CancellationToken, Task<InventoryResult>> inventoryRead, Action<string> write)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            write("F1 CLI (read-only)");
            write("  F1.exe --cli scan [--json]        Scan Cleaner targets and print the summary");
            write("  F1.exe --cli inventory [--json]   Print the installed-application inventory");
            write("  F1.exe --cli version              Print the product version");
            write("Mutation commands (clean, uninstall, delete) are intentionally unavailable from the CLI.");
            return Success;
        }
        if (args[0] != "--cli" || args.Length < 2) { write("Invalid arguments. Try --help."); return InvalidArguments; }
        var command = args[1];
        var json = args.Contains("--json");
        try
        {
            switch (command)
            {
                case "version":
                    write(SystemCore.Core.ProductIdentity.Name + " " + (typeof(F1Cli).Assembly.GetName().Version?.ToString(3) ?? "unknown"));
                    return Success;
                case "scan":
                {
                    var session = await cleanerScan(CancellationToken.None);
                    if (json) write(JsonSerializer.Serialize(new { session.Id, session.Started, session.Completed, Complete = session.Complete,
                        Categories = session.Categories.Select(c => new { Category = c.Category.ToString(), Status = c.Status.ToString(), Items = c.ItemCount, c.Bytes }) },
                        new JsonSerializerOptions { WriteIndented = true }));
                    else foreach (var category in session.Categories.Where(c => c.Status != CleanerScanStatus.Unavailable))
                        write($"{category.Category,-20} {category.Status,-12} {category.ItemCount,8} items  {category.Bytes,12:N0} bytes");
                    return Success;
                }
                case "inventory":
                {
                    var result = await inventoryRead(CancellationToken.None);
                    if (json) write(JsonSerializer.Serialize(result.Applications.Select(a => new { a.DisplayName, a.Publisher, a.DisplayVersion,
                        Type = a.ApplicationType.ToString(), a.InstallLocation, a.EstimatedSize }), new JsonSerializerOptions { WriteIndented = true }));
                    else foreach (var application in result.Applications.Where(RemovableApplications.IsVisible).OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase))
                        write($"{application.DisplayName} | {application.Publisher} | {application.DisplayVersion}");
                    if (!result.Complete) write("Warning: one or more inventory sources could not be read.");
                    return Success;
                }
                case "clean" or "uninstall" or "delete":
                    write("Refused: F1 does not expose destructive operations to the command line. Use the application.");
                    return CommandRefused;
                case "memory":
                {
                    // Each area must be named explicitly; there is no implicit "free everything".
                    var kinds = new List<SystemCore.Infrastructure.MemoryTools.MemoryReleaseKind>();
                    if (args.Contains("/standbylow")) kinds.Add(SystemCore.Infrastructure.MemoryTools.MemoryReleaseKind.StandbyLowPriority);
                    if (args.Contains("/standby")) kinds.Add(SystemCore.Infrastructure.MemoryTools.MemoryReleaseKind.StandbyAll);
                    if (args.Contains("/workingset")) kinds.Add(SystemCore.Infrastructure.MemoryTools.MemoryReleaseKind.WorkingSet);
                    if (args.Contains("/modifiedpages")) kinds.Add(SystemCore.Infrastructure.MemoryTools.MemoryReleaseKind.ModifiedPageList);
                    if (args.Contains("/combinedpages")) kinds.Add(SystemCore.Infrastructure.MemoryTools.MemoryReleaseKind.CombinedPageList);
                    if (kinds.Count == 0)
                    {
                        write("No area named. Use /standbylow, /standby, /workingset, /modifiedpages or /combinedpages.");
                        return InvalidArguments;
                    }
                    var released = await new SystemCore.Infrastructure.MemoryTools.SystemMemoryReleaser().ReleaseAsync(kinds, Array.Empty<string>(), CancellationToken.None);
                    foreach (var result in released)
                        write($"{(result.Succeeded ? "OK" : "FAIL")} {result.Kind}: {result.Detail}");
                    return released.All(r => r.Succeeded) ? Success : Failed;
                }
                default:
                    write($"Unknown command '{command}'. Try --help.");
                    return InvalidArguments;
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            write("Command failed: " + e.Message);
            return Failed;
        }
    }
}
