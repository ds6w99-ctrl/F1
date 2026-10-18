// SystemCore.RealWorldProbe
// Audit harness: drives the F1 product engine (SystemCore.Infrastructure + SystemCore.Core,
// the same assemblies shipped in the portable/installed payload) against REAL installed apps
// on a real machine/VM. It reports facts as JSON; independent PowerShell verification scripts
// decide PASS/FAIL. It never invents apps or skips the engine's own safety gates.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using SystemCore.Core.Cleaner;
using SystemCore.Core.MemoryTools;
using SystemCore.Core.Startup;
using SystemCore.Core.Uninstaller;
using SystemCore.Infrastructure.Cleaner;
using SystemCore.Infrastructure.MemoryTools;
using SystemCore.Infrastructure.Startup;
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
        "uninstall-clean" => await UninstallCleanAsync(executor, evidence, inventory, token),
        "bulk" => await BulkAsync(executor, token),
        "leftovers" => await LeftoversAsync(inventory, evidence, token),
        "orphans" => await OrphansAsync(inventory, token),
        "export" => await ExportAsync(inventory, token),
        "restore" => await RestoreAsync(token),
        "clean-scan" => await CleanScanAsync(token),
        "memory-release" => await MemoryReleaseAsync(token),
        "startup-list" => await StartupListAsync(token),
        "startup-toggle" => await StartupToggleAsync(token),
        "inspect-parse" => await InspectParseAsync(token),
        "msi-debug" => await MsiDebugAsync(token),
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

async Task<int> UninstallCleanAsync(UninstallExecutor executor, ApplicationRemovalEvidence evidence, InstalledApplicationInventory inventory, CancellationToken token)
{
    // Mirrors the product UI flow: uninstall -> automatic leftover scan -> optional deletion,
    // all in one process so the in-memory removal receipt stays valid (by design, a receipt
    // never survives its process).
    var name = args.Contains("--name") ? Arg("--name") : null;
    var quiet = args.Contains("--quiet");
    var deep = args.Contains("--deep");
    var delete = args.Contains("--delete");
    var includePersonal = args.Contains("--include-personal");
    var session = await executor.RefreshAsync(token);
    var matches = session.Inventory.Applications.Where(a => name != null && string.Equals(a.DisplayName, name, StringComparison.OrdinalIgnoreCase)).ToArray();
    if (matches.Length == 0) { await WriteJsonAsync(Arg("--out"), new { Error = "not-found", Name = name }, token); return 3; }
    if (matches.Length > 1) { await WriteJsonAsync(Arg("--out"), new { Error = "ambiguous", Name = name }, token); return 4; }
    var app = matches[0];
    executor.QuietRequested = quiet;
    UninstallOutcome outcome;
    try { outcome = await executor.UninstallAsync(new UninstallRequest(session.Id, app.Id), null, token); }
    finally { executor.QuietRequested = false; }
    Console.WriteLine($"uninstall: {app.DisplayName} -> {outcome.State} exit={outcome.NativeExitCode} receipt={outcome.Receipt?.Id}");
    object? leftovers = null;
    if (outcome.Receipt is not null)
    {
        var scanner = new LeftoverScanner(evidence, inventory);
        var scan = await scanner.ScanAsync(outcome.Receipt.Id, deep, token);
        Console.WriteLine($"leftovers: {scan.Items.Count} items, complete={scan.Complete}");
        object? deletion = null;
        if (delete)
        {
            var ids = scan.Items.Where(i => i.Confidence == LeftoverConfidence.Safe || (includePersonal && i.Confidence == LeftoverConfidence.Review)).Select(i => i.Id).ToArray();
            if (ids.Length == 0)
            {
                deletion = new { Note = "Nothing selectable to delete", RemovedBytes = 0L };
            }
            else
            {
                var service = LeftoverDeletionFactory.Create(scanner, evidence, inventory, new RegistryBackupWriter());
                var result = await service.DeleteAsync(new LeftoverDeletionRequest(scan.Id, scan.ApplicationIdentityId, ids, includePersonal), token);
                deletion = new { result.RemovedBytes, result.BackupFile, Items = result.Items.Select(i => new { i.ItemId, Status = i.Status.ToString(), i.RemovedBytes, i.BackupFile }) };
            }
        }
        leftovers = new
        {
            scan.Id, scan.ReceiptId, scan.Complete,
            Items = scan.Items.Select(i => new { i.Id, Type = i.Type.ToString(), i.DisplayLocation, i.Evidence, Confidence = i.Confidence.ToString(), i.Risk, i.Size, i.RequiresElevation, i.SelectedByDefault, i.SafetyStatus }),
            Deletion = deletion
        };
    }
    await WriteJsonAsync(Arg("--out"), new
    {
        Requested = new { app.DisplayName, app.Id, app.ApplicationType, app.Publisher, app.DisplayVersion, app.InstallLocation, app.ProductCode, Quiet = quiet },
        outcome.State, outcome.NativeExitCode, outcome.Detail,
        Receipt = outcome.Receipt is null ? null : new { outcome.Receipt.Id, outcome.Receipt.DisplayName, outcome.Receipt.VerifiedAt },
        Leftovers = leftovers
    }, token);
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
        var ids = scan.Items.Where(i => i.Confidence == LeftoverConfidence.Safe || (includePersonal && i.Confidence == LeftoverConfidence.Review)).Select(i => i.Id).ToArray();
        if (ids.Length == 0)
        {
            deletion = new { Note = "Nothing selectable to delete", RemovedBytes = 0L };
        }
        else
        {
            var service = LeftoverDeletionFactory.Create(scanner, evidence, inventory, new RegistryBackupWriter());
            var result = await service.DeleteAsync(new LeftoverDeletionRequest(scan.Id, scan.ApplicationIdentityId, ids, includePersonal), token);
            deletion = new { result.RemovedBytes, result.BackupFile, Items = result.Items.Select(i => new { i.ItemId, Status = i.Status.ToString(), i.RemovedBytes, i.BackupFile }) };
        }
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

[DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern uint MsiEnumProductsExW(string? product, string? userSid, uint contexts, uint index, StringBuilder productCode, out uint context, StringBuilder sid, ref uint sidLength);
[DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern uint MsiGetProductInfoExW(string productCode, string? sid, uint context, string property, StringBuilder? value, ref uint length);
[DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern uint MsiEnumComponentsW(uint index, StringBuilder componentCode);
[DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern uint MsiGetProductCodeW(string component, StringBuilder productCode);
[DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern int MsiGetComponentPathExW(string productCode, string? userSid, uint context, string component, StringBuilder? path, ref uint pathLength);

async Task<int> MsiDebugAsync(CancellationToken token)
{
    var name = Arg("--name");
    var result = new Dictionary<string, object>();
    string? product = null; uint context = 0; string? sid = null;
    for (uint i = 0; i < 10000; i++)
    {
        token.ThrowIfCancellationRequested();
        var code = new StringBuilder(39);
        var s = new StringBuilder(256); uint sl = 256;
        var r = MsiEnumProductsExW(null, null, 7, i, code, out var ctx, s, ref sl);
        if (r == 259) break;
        if (r != 0) { result["enum-products-error"] = r; break; }
        string? Prop(string p)
        {
            uint len = 0;
            var owner = ctx == 4 ? null : s.ToString();
            var err = MsiGetProductInfoExW(code.ToString(), owner, ctx, p, null, ref len);
            if (err is not 0 and not 234 || len > 32767) return null;
            var sb = new StringBuilder((int)len + 1); len++;
            return MsiGetProductInfoExW(code.ToString(), owner, ctx, p, sb, ref len) == 0 && sb.Length > 0 ? sb.ToString() : null;
        }
        var display = Prop("InstalledProductName");
        if (display != null && display.Contains(name, StringComparison.OrdinalIgnoreCase))
        {
            product = code.ToString(); context = ctx; sid = s.ToString();
            result["product"] = product; result["context"] = context; result["display"] = display;
            result["InstallLocation"] = Prop("InstallLocation") ?? "<empty>";
            break;
        }
    }
    if (product is null) { result["found"] = false; await WriteJsonAsync(Arg("--out"), result, token); return 0; }
    uint enumerated = 0; var matched = 0; var paths = new List<string>();
    for (uint i = 0; i < 30000; i++)
    {
        token.ThrowIfCancellationRequested();
        var comp = new StringBuilder(39);
        var r = MsiEnumComponentsW(i, comp);
        if (r != 0) { if (r != 259) result["enum-components-error"] = r; break; }
        enumerated++;
        var pc = new StringBuilder(39);
        if (MsiGetProductCodeW(comp.ToString(), pc) != 0) continue;
        if (!string.Equals(pc.ToString(), product, StringComparison.OrdinalIgnoreCase)) continue;
        matched++;
        var owner = context == 4 ? null : sid;
        uint len = 0;
        var sizeState = MsiGetComponentPathExW(product, owner, context, comp.ToString(), null, ref len);
        string? path = null; int state = -1;
        if (len > 0 && (sizeState >= 0 || sizeState == 234))
        {
            var sb = new StringBuilder((int)len + 1);
            state = MsiGetComponentPathExW(product, owner, context, comp.ToString(), sb, ref len);
            if (state >= 0 && sb.Length > 0) path = sb.ToString();
        }
        if (paths.Count < 12) paths.Add($"sizeState={sizeState} state={state} len={len} path={path}");
    }
    result["components-enumerated"] = enumerated; result["components-matched"] = matched; result["paths-sample"] = paths;
    await WriteJsonAsync(Arg("--out"), result, token);
    foreach (var kv in result) Console.WriteLine($"{kv.Key}: {kv.Value}");
    return 0;
}

async Task<int> RestoreAsync(CancellationToken token)
{
    // Uses the product's own LeftoverBackupRestorer on a backup archive the engine produced.
    var result = await LeftoverBackupRestorer.RestoreAsync(Arg("--file"), token);
    await WriteJsonAsync(Arg("--out"), new { result.Restored, result.Skipped, result.Failed, result.Messages }, token);
    Console.WriteLine($"restore: restored={result.Restored} skipped={result.Skipped} failed={result.Failed}");
    return result.Failed == 0 ? 0 : 2;
}

async Task<int> CleanScanAsync(CancellationToken token)
{
    // The same engine wiring the product uses, minus the elevated broker (probe parents are
    // rejected by the helper identity gate by design). Recycle bin is only emptied when the
    // caller passes --empty-recycle-bin explicitly.
    var service = new WindowsCleanerService(new WindowsCleanerTargets(),
        recycleBin: new WindowsRecycleBinService(new WindowsRecycleBinShell()),
        explorerHistory: new ExplorerHistoryCleaner());
    var scan = await service.ScanAsync(token);
    var clean = args.Contains("--clean");
    var emptyBin = args.Contains("--empty-recycle-bin");
    var categoriesArg = args.Contains("--categories") ? Arg("--categories") : null;
    var wanted = categoriesArg is null ? null
        : new HashSet<string>(categoriesArg.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
    object? deletion = null;
    if (clean)
    {
        var ids = scan.Categories.Where(c => wanted is null || wanted.Contains(c.Category.ToString()))
            .SelectMany(c => c.Items).Select(i => i.Id).ToArray();
        if (ids.Length == 0 && !emptyBin)
            deletion = new { Note = "No items in selected categories" };
        else
        {
            var result = await service.CleanAsync(new CleanerCleanRequest(scan.Id, ids, emptyBin), token);
            deletion = new { result.Accepted, Items = result.Items.Select(i => new { i.ItemId, Outcome = i.Outcome.ToString(), i.RemovedBytes }) };
        }
    }
    await WriteJsonAsync(Arg("--out"), new
    {
        scan.Id,
        Categories = scan.Categories.Select(c => new
        {
            Category = c.Category.ToString(), Status = c.Status.ToString(), c.Bytes, c.ItemCount,
            Sample = c.Items.Take(15).Select(i => new { i.Id, i.DisplayPath, i.Size })
        }),
        Deletion = deletion
    }, token);
    Console.WriteLine($"clean-scan: {scan.Categories.Count} categories, clean={clean}");
    return 0;
}

static MemoryStatusEx MemoryNow() { var s = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() }; NativeMemory.GlobalMemoryStatusEx(ref s); return s; }

async Task<int> MemoryReleaseAsync(CancellationToken token)
{
    var kinds = new List<MemoryReleaseKind> { MemoryReleaseKind.StandbyLowPriority, MemoryReleaseKind.WorkingSet };
    if (args.Contains("--standby-all")) kinds.Add(MemoryReleaseKind.StandbyAll);
    if (args.Contains("--modified")) kinds.Add(MemoryReleaseKind.ModifiedPageList);
    if (args.Contains("--registry-cache")) kinds.Add(MemoryReleaseKind.RegistryCache);
    var before = MemoryNow();
    var results = await new SystemMemoryReleaser().ReleaseAsync(kinds, [], token);
    var after = MemoryNow();
    await WriteJsonAsync(Arg("--out"), new
    {
        BeforeAvailPhys = before.AvailPhys, AfterAvailPhys = after.AvailPhys, DeltaAvailPhys = (long)(after.AvailPhys - before.AvailPhys),
        Results = results.Select(r => new { Kind = r.Kind.ToString(), r.Succeeded, r.Affected, r.Detail })
    }, token);
    Console.WriteLine($"memory-release: avail {before.AvailPhys} -> {after.AvailPhys}");
    return results.Any(r => !r.Succeeded) ? 2 : 0;
}

async Task<int> StartupListAsync(CancellationToken token)
{
    var inventory = new StartupInventoryService(new RegistryStartupProvider(), new StartupFolderProvider(),
        new TaskSchedulerStartupProvider(), new StartupStateService());
    var session = await inventory.RefreshAsync(token);
    await WriteJsonAsync(Arg("--out"), new
    {
        session.Id, Complete = session.Inventory.Complete, Summary = inventory.Summary,
        Items = session.Inventory.Items.Select(i => new { i.Id, i.Name, i.Publisher, Source = i.SourceType.ToString(), i.SourceLocation,
            Status = i.Status.ToString(), Protection = i.ProtectionReason.ToString(), i.RequiresElevation, i.CanManage })
    }, token);
    Console.WriteLine($"startup-list: {session.Inventory.Items.Count} items");
    return 0;
}

async Task<int> StartupToggleAsync(CancellationToken token)
{
    var registry = new RegistryStartupProvider();
    var folders = new StartupFolderProvider();
    var tasks = new TaskSchedulerStartupProvider();
    var state = new StartupStateService();
    var inventory = new StartupInventoryService(registry, folders, tasks, state);
    var session = await inventory.RefreshAsync(token);
    var name = Arg("--name");
    var action = args.Contains("--enable") ? StartupAction.Enable : StartupAction.Disable;
    var item = session.Inventory.Items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
    if (item is null) { await WriteJsonAsync(Arg("--out"), new { Error = "not-found", Name = name }, token); return 3; }
    var mutation = new StartupMutationService(inventory, registry, folders, tasks, state);
    var result = await mutation.ApplyAsync(new StartupOperationRequest(session.Id, item.Id, action, Confirmed: true), token);
    await WriteJsonAsync(Arg("--out"), new { Requested = new { item.Id, item.Name, Action = action.ToString() }, Outcome = result.Outcome.ToString() }, token);
    Console.WriteLine($"startup-toggle: {item.Name} -> {result.Outcome}");
    return result.Outcome == StartupOutcome.Success ? 0 : 2;
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

// --- type declarations (must follow all top-level statements) ---
[StructLayout(LayoutKind.Sequential)]
struct MemoryStatusEx { public uint Length; public uint MemoryLoad; public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual; }
static class NativeMemory
{
    [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
}
