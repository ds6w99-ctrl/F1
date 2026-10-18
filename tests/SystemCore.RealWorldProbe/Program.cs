// SystemCore.RealWorldProbe
// Audit harness: drives the F1 product engine (SystemCore.Infrastructure + SystemCore.Core,
// the same assemblies shipped in the portable/installed payload) against REAL installed apps
// on a real machine/VM. It reports facts as JSON; independent PowerShell verification scripts
// decide PASS/FAIL. It never invents apps or skips the engine's own safety gates.
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
using SystemCore.Core.Uninstaller;
using SystemCore.Infrastructure.Uninstaller;

if (args.Length == 0) { Usage(); return 64; }

var token = new CancellationTokenSource(TimeSpan.FromMinutes(TimeoutMinutes())).Token;
var identity = new ApplicationIdentityResolver();
var inventory = new InstalledApplicationInventory(
    new RegistryApplicationProvider(identity),
    new MsiApplicationProvider(identity),
    new MsixApplicationProvider(identity));
var evidence = new ApplicationRemovalEvidence();
var executor = new UninstallExecutor(inventory, new NativeUninstallRunner(), new UninstallCompletionDetector(inventory), evidence);

try
{
    return args[0] switch
    {
        "inventory" => await InventoryAsync(inventory, token),
        "uninstall" => await UninstallAsync(executor, token),
        "bulk" => await BulkAsync(executor, token),
        "leftovers" => await LeftoversAsync(inventory, evidence, token),
        "orphans" => await OrphansAsync(inventory, token),
        "export" => await ExportAsync(inventory, token),
        "inspect-parse" => await InspectParseAsync(token),
        _ => Usage()
    };
}
catch (OperationCanceledException) { Console.Error.WriteLine("CANCELLED: operation exceeded its time budget"); return 124; }
catch (Exception ex) { Console.Error.WriteLine($"PROBE FAILED: {ex.GetType().Name}: {ex.Message}"); return 1; }

int Usage()
{
    Console.Error.WriteLine("""
        SystemCore.RealWorldProbe <command> [options]
          inventory --out <json>
          uninstall --name <DisplayName>|--id <appId> --out <json> [--quiet] [--timeout-min n]
          bulk --names <n1;n2;...> --out <json> [--quiet] [--timeout-min n]
          leftovers --receipt <guid> --out <json> [--deep] [--delete] [--include-personal]
          orphans --out <json>
          export --dir <dir>
        """);
    return 64;
}

int TimeoutMinutes()
{
    var index = Array.IndexOf(args, "--timeout-min");
    return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var m) && m > 0 ? m : 10;
}

string Arg(string name) => args[Array.IndexOf(args, name) + 1];

async Task<int> InventoryAsync(InstalledApplicationInventory inventory, CancellationToken token)
{
    var result = await inventory.ReadAsync(token);
    var payload = new
    {
        Complete = result.Complete,
        UnavailableSources = result.UnavailableSources,
        Count = result.Applications.Count,
        VisibleCount = result.Applications.Count(RemovableApplications.IsVisible),
        Applications = result.Applications.Select(a => new
        {
            a.Id, a.DisplayName, a.Publisher, a.DisplayVersion, a.InstallLocation, a.Architecture,
            a.ApplicationType, a.Source, a.ProductCode, a.PackageFullName,
            CanUninstall = a.CanUninstall, IsProtected = a.IsProtected, IsSystemComponent = a.IsSystemComponent,
            IsFramework = a.IsFramework, a.ItemKind, a.RegistrySource, a.UnavailableReason,
            a.RegisteredUninstallCommand, a.QuietUninstallCommand, HasQuietUninstall = a.HasQuietUninstall,
            a.EstimatedSize, a.InstallDate
        })
    };
    await WriteJsonAsync(Arg("--out"), payload, token);
    Console.WriteLine($"inventory: {payload.Count} apps ({payload.VisibleCount} visible/uninstallable), complete={payload.Complete}");
    return 0;
}

async Task<int> UninstallAsync(UninstallExecutor executor, CancellationToken token)
{
    var name = args.Contains("--name") ? Arg("--name") : null;
    var id = args.Contains("--id") ? Arg("--id") : null;
    var quiet = args.Contains("--quiet");
    var session = await executor.RefreshAsync(token);
    var matches = session.Inventory.Applications.Where(a => (id != null && a.Id == id) || (name != null && string.Equals(a.DisplayName, name, StringComparison.OrdinalIgnoreCase))).ToArray();
    if (matches.Length == 0) { await WriteJsonAsync(Arg("--out"), new { Error = "not-found", Name = name, Id = id }, token); return 3; }
    if (matches.Length > 1 && name != null) { await WriteJsonAsync(Arg("--out"), new { Error = "ambiguous", Name = name, Candidates = matches.Select(a => new { a.Id, a.DisplayName, a.Publisher, a.ApplicationType }) }, token); return 4; }
    var app = matches[0];
    var states = new List<string>();
    IProgress<UninstallState> progress = new Progress<UninstallState>(s => { states.Add(s.ToString()); Console.WriteLine("  state: " + s); });
    // Quiet is opt-in per request. The product's UI gates it behind the Pro plan; the engine
    // path below is byte-identical to what the UI's quiet/bulk flow invokes (QuietRequested +
    // the same guarded UninstallAsync).
    executor.QuietRequested = quiet;
    UninstallOutcome outcome;
    try { outcome = await executor.UninstallAsync(new UninstallRequest(session.Id, app.Id), progress, token); }
    finally { executor.QuietRequested = false; }
    await WriteJsonAsync(Arg("--out"), new
    {
        Requested = new { app.DisplayName, app.Id, app.ApplicationType, app.Publisher, app.DisplayVersion, app.InstallLocation, app.ProductCode, Quiet = quiet },
        outcome.State, outcome.NativeExitCode, outcome.Detail,
        Receipt = outcome.Receipt is null ? null : new { outcome.Receipt.Id, outcome.Receipt.DisplayName, outcome.Receipt.VerifiedAt },
        States = states
    }, token);
    Console.WriteLine($"uninstall: {app.DisplayName} -> {outcome.State} exit={outcome.NativeExitCode} receipt={outcome.Receipt?.Id}");
    return outcome.State == UninstallState.Removed ? 0 : 2;
}

async Task<int> BulkAsync(UninstallExecutor executor, CancellationToken token)
{
    var names = Arg("--names").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var quiet = args.Contains("--quiet");
    var session = await executor.RefreshAsync(token);
    var ids = names.Select(n =>
    {
        var matches = session.Inventory.Applications.Where(a => string.Equals(a.DisplayName, n, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) throw new InvalidOperationException($"not-found: {n}");
        if (matches.Length > 1) throw new InvalidOperationException($"ambiguous: {n}");
        return matches[0].Id;
    }).ToArray();
    var bulk = new BulkUninstallExecutor(executor);
    var outcome = await bulk.UninstallAsync(new BulkUninstallRequest(session.Id, ids, UseQuietUninstaller: quiet),
        new Progress<BulkUninstallItemResult>(r => Console.WriteLine("  bulk item: " + JsonSerializer.Serialize(new { r.ApplicationId, r.State, r.NativeExitCode, r.Detail }))), token);
    await WriteJsonAsync(Arg("--out"), new
    {
        outcome.Removed, outcome.Failed, outcome.Cancelled,
        Items = outcome.Items.Select(i => new { i.ApplicationId, i.State, i.NativeExitCode, i.Detail })
    }, token);
    Console.WriteLine($"bulk: removed={outcome.Removed} failed={outcome.Failed} cancelled={outcome.Cancelled}");
    return outcome.Removed == ids.Length ? 0 : 2;
}

async Task<int> LeftoversAsync(InstalledApplicationInventory inventory, ApplicationRemovalEvidence evidence, CancellationToken token)
{
    var receiptId = Guid.Parse(Arg("--receipt"));
    var deep = args.Contains("--deep");
    var delete = args.Contains("--delete");
    var includePersonal = args.Contains("--include-personal");
    var scanner = new LeftoverScanner(evidence, inventory);
    var scan = await scanner.ScanAsync(receiptId, deep, token);
    object? deletion = null;
    if (delete)
    {
        var service = LeftoverDeletionFactory.Create(scanner, evidence, inventory, new RegistryBackupWriter());
        var ids = scan.Items.Where(i => i.Confidence == LeftoverConfidence.Safe || (includePersonal && i.Confidence == LeftoverConfidence.Review)).Select(i => i.Id).ToArray();
        var result = await service.DeleteAsync(new LeftoverDeletionRequest(scan.Id, scan.ApplicationIdentityId, ids, includePersonal), token);
        deletion = new { result.RemovedBytes, result.BackupFile, Items = result.Items.Select(i => new { i.ItemId, Status = i.Status.ToString(), i.RemovedBytes, i.BackupFile }) };
    }
    await WriteJsonAsync(Arg("--out"), new
    {
        scan.Id, scan.ReceiptId, scan.ApplicationIdentityId, scan.Complete,
        Items = scan.Items.Select(i => new { i.Id, Type = i.Type.ToString(), i.DisplayLocation, i.Evidence, Confidence = i.Confidence.ToString(), i.Risk, i.Size, i.RequiresElevation, i.SelectedByDefault, i.SafetyStatus }),
        Deletion = deletion
    }, token);
    Console.WriteLine($"leftovers: {scan.Items.Count} items, complete={scan.Complete}, delete={delete}");
    return 0;
}

async Task<int> OrphansAsync(InstalledApplicationInventory inventory, CancellationToken token)
{
    var result = await inventory.ReadAsync(token);
    var orphans = await OrphanDirectoryScanner.ScanAsync(result, token);
    await WriteJsonAsync(Arg("--out"), new
    {
        InventoryCount = result.Applications.Count,
        Orphans = orphans.Select(o => new { o.Path, o.Name, o.SizeBytes, o.FileCount, o.Reason, o.Recommended })
    }, token);
    Console.WriteLine($"orphans: {orphans.Count} candidates");
    return 0;
}

async Task<int> InspectParseAsync(CancellationToken token)
{
    // Audit diagnostics: dump the raw UninstallString of matching entries and the parser's
    // verdict, byte by byte, so a rejected native uninstaller can be explained exactly.
    var name = Arg("--name");
    var results = new List<object>();
    foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
    foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var root = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
        if (root is null) continue;
        foreach (var n in root.GetSubKeyNames())
        {
            token.ThrowIfCancellationRequested();
            using var key = root.OpenSubKey(n);
            var display = key?.GetValue("DisplayName") as string;
            if (display is null || !display.Contains(name, StringComparison.OrdinalIgnoreCase)) continue;
            var raw = key!.GetValue("UninstallString") as string;
            var parsed = UninstallCommandParser.Parse(raw);
            results.Add(new
            {
                Hive = hive.ToString(), View = view.ToString(), KeyName = n, DisplayName = display,
                Raw = raw, RawLength = raw?.Length,
                Parsed = parsed is null ? null : new { parsed.Executable, ArgumentCount = parsed.Arguments.Count },
                Checks = DiagnoseParse(raw)
            });
        }
    }
    await WriteJsonAsync(Arg("--out"), results, token);
    foreach (var r in results) Console.WriteLine(JsonSerializer.Serialize(r));
    return 0;
}

static Dictionary<string, object> DiagnoseParse(string? value)
{
    // Mirrors UninstallCommandParser.Parse/IsSafeExecutable conditions for audit diagnostics.
    var d = new Dictionary<string, object>();
    d["value-null-or-blank"] = string.IsNullOrWhiteSpace(value);
    if (value is null) return d;
    d["length"] = value.Length;
    d["length>32767"] = value.Length > 32767;
    d["has-forbidden-char"] = value.IndexOfAny(['\0', '\r', '\n', '|', '&', '<', '>']) >= 0;
    var text = value.Trim();
    d["odd-quote-count"] = text.Count(c => c == '"') % 2 != 0;
    // emulate CommandLineToArgvW for the simple no-quote case
    var tokens = SplitLikeArgv(text);
    d["token-count"] = tokens.Count;
    var executable = tokens.Count == 0 ? "" : Environment.ExpandEnvironmentVariables(tokens[0]);
    d["executable"] = executable;
    d["path-len<4"] = executable.Length < 4;
    d["path-len>240"] = executable.Length > 240;
    d["has-control"] = executable.Any(char.IsControl);
    d["not-absolute"] = executable.Length < 3 || !char.IsAsciiLetter(executable[0]) || executable[1] != ':' || executable[2] != '\\';
    d["colon-after-drive"] = executable.Length > 2 && executable[2..].Contains(':');
    d["slash-or-percent-or-quote"] = executable.Contains('/') || executable.Contains('%') || executable.Contains('"');
    d["dot-or-space-segment"] = executable.Split('\\').Any(s => s is "." or ".." || s.EndsWith(' ') || s.EndsWith('.'));
    d["not-exe"] = !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    d["forbidden-name"] = executable.Length > 0 && new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "cmd.exe", "powershell.exe", "pwsh.exe", "wmic.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe", "reg.exe", "msiexec.exe" }
        .Contains(Path.GetFileName(executable));
    try
    {
        d["file-exists"] = File.Exists(executable);
        d["fullpath-mismatch"] = File.Exists(executable) && !string.Equals(Path.GetFullPath(executable), executable, StringComparison.OrdinalIgnoreCase);
        var reparse = new List<string>();
        for (var node = executable; node is not null; node = Path.GetDirectoryName(node))
            if (File.Exists(node) && (File.GetAttributes(node) & FileAttributes.ReparsePoint) != 0) reparse.Add(node);
        d["reparse-ancestors"] = reparse;
    }
    catch (Exception e) { d["attr-error"] = e.GetType().Name + ": " + e.Message; }
    d["overall-parsed-null"] = UninstallCommandParser.Parse(value) is null;
    return d;
}

static List<string> SplitLikeArgv(string text)
{
    // Approximation sufficient for diagnostics of simple commands.
    if (text.Contains('"')) return new List<string> { text };
    return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
}

async Task<int> ExportAsync(InstalledApplicationInventory inventory, CancellationToken token)
{
    var result = await inventory.ReadAsync(token);
    var directory = Arg("--dir");
    Directory.CreateDirectory(directory);
    var csv = await UninstallerExport.WriteInventoryAsync(result.Applications, UninstallerExportFormat.Csv, directory, token);
    var json = await UninstallerExport.WriteInventoryAsync(result.Applications, UninstallerExportFormat.Json, directory, token);
    await WriteJsonAsync(Path.Combine(directory, "probe-export.json"), new { Csv = csv, Json = json, Count = result.Applications.Count }, token);
    Console.WriteLine($"export: {csv}; {json}");
    return 0;
}

async Task WriteJsonAsync(string path, object payload, CancellationToken token)
{
    var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
    Directory.CreateDirectory(directory);
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), token);
    Console.WriteLine("  wrote: " + path);
}
