using System.Diagnostics;
using System.Text.Json;
using SystemCore.Core.Uninstaller;
using SystemCore.Infrastructure.Uninstaller;
using Microsoft.Win32;

if (args.Length == 3 && args[0] is "--synthetic-uninstall" or "--synthetic-delayed-uninstall" or "--synthetic-parent-uninstall")
{
    const string prefix = "SystemCorePhase5Test-";
    if (!args[1].StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(args[1][prefix.Length..], "N", out _)) return 2;
    using var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
    using var parent = user.OpenSubKey(RegistryApplicationProvider.UninstallRoot, true);
    using (var key = parent?.OpenSubKey(args[1]))
        if (key?.GetValue("SystemCoreTestNonce") as string != args[2]) return 3;
    if (args[0] == "--synthetic-parent-uninstall")
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "SystemCore.UninstallerTests.exe")) { UseShellExecute = false };
        foreach (var argument in new[] { "--synthetic-delayed-uninstall", args[1], args[2] }) start.ArgumentList.Add(argument);
        using var child = Process.Start(start);
        return child is null ? 4 : 0;
    }
    if (args[0] == "--synthetic-delayed-uninstall") await Task.Delay(1200);
    parent!.DeleteSubKey(args[1]);
    return 0;
}

var identity = new ApplicationIdentityResolver();
if (args.Contains("--game-inventory"))
{
    var environment = new WindowsGameLibraryEnvironment();
    IGameLibraryProvider[] providers = [new SteamGameLibraryProvider(environment), new EpicGameLibraryProvider(environment), new XboxGameLibraryProvider(),
        new EaGameLibraryProvider(environment), new UbisoftGameLibraryProvider(environment), new BattleNetGameLibraryProvider(environment)];
    var inventory = new InstalledApplicationInventory(new RegistryApplicationProvider(identity), new MsiApplicationProvider(identity), new MsixApplicationProvider(identity), providers);
    var result = await inventory.ReadAsync(default);
    Console.WriteLine(JsonSerializer.Serialize(new { result.Complete, result.GameDiscovery, result.GameDuplicatesMerged,
        Games = result.Applications.Count(a => a.ItemKind == ItemKind.Game), Managed = result.Applications.Count(UnifiedLibraryPresentation.IsLauncherManaged) },
        new JsonSerializerOptions { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }));
    return 0;
}
if (args.Contains("--inventory"))
{
    var clock = Stopwatch.StartNew();
    var inventory = new InstalledApplicationInventory(new RegistryApplicationProvider(identity), new MsiApplicationProvider(identity), new MsixApplicationProvider(identity));
    var result = await inventory.ReadAsync(default);
    var visible = result.Applications.Where(RemovableApplications.IsVisible).ToArray();
    var iconChecks = new List<object>();
    foreach (var type in Enum.GetValues<ApplicationType>())
    {
        foreach (var application in visible.Where(a => a.ApplicationType == type).Take(3))
        {
            var icon = await ApplicationIconReader.ReadAsync(application, default);
            iconChecks.Add(new { Type = type.ToString(), application.DisplayName, Loaded = icon is not null, Bytes = icon?.Length ?? 0 });
        }
    }
    Console.WriteLine(JsonSerializer.Serialize(new { result.Complete, Total = result.Applications.Count,
        Hidden = result.Applications.Count - visible.Length, VisibleRemovable = visible.Length, IconChecks = iconChecks,
        Win32 = result.Applications.Count(a => a.ApplicationType == ApplicationType.Win32),
        Msi = result.Applications.Count(a => a.ApplicationType == ApplicationType.Msi),
        Msix = result.Applications.Count(a => a.ApplicationType == ApplicationType.Msix),
        Protected = result.Applications.Count(a => a.IsProtected),
        UnknownMetadata = result.Applications.Count(a => a.Publisher is null || a.InstallDate is null || a.EstimatedSize is null),
        UnavailableSources = result.UnavailableSources, Milliseconds = clock.ElapsedMilliseconds }, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
var checks = new List<(string Name, bool Passed)>();
void Check(string name, bool ok) { checks.Add((name, ok)); Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {name}"); }
var source = new RegistryApplicationSource(InventoryHive.CurrentUser, InventoryView.Registry64, "Synthetic-a");
Check("Source identity distinguishes view", identity.Resolve(ApplicationType.Win32, source, null, null) != identity.Resolve(ApplicationType.Win32, source with { View = InventoryView.Registry32 }, null, null));
var guid = Guid.NewGuid();
Check("MSI identity independent of registry view", identity.Resolve(ApplicationType.Msi, source, guid, null) == identity.Resolve(ApplicationType.Msi, source with { View = InventoryView.Registry32 }, guid, null));
Check("No display name omitted", RegistryApplicationProvider.Normalize(source, new Dictionary<string, object?>(), identity) is null);
var row = RegistryApplicationProvider.Normalize(source, new Dictionary<string, object?> { ["DisplayName"] = "Synthetic", ["NoRemove"] = 1 }, identity)!;
Check("NoRemove protected", row.IsProtected && !row.CanUninstall);
Check("Missing metadata stays unknown", row.Publisher is null && row.EstimatedSize is null && row.InstallDate is null);
foreach (var command in new[] { "", "cmd.exe /c del x", @"relative.exe /uninstall", @"\\server\share\uninstall.exe", "\"C:\\bad.exe", @"C:\Windows\System32\cmd.exe /c test", @"C:\fake.exe & calc.exe", @"C:\folder\..\uninstall.exe", @"C:\fake.exe:evil" })
    Check("Reject invalid/unsafe executable command", UninstallCommandParser.Parse(command) is null);
Console.WriteLine(JsonSerializer.Serialize(new { Checks = checks.Count, Failed = checks.Count(c => !c.Passed) }));
if (args.Contains("--synthetic"))
foreach (var nativeMode in new[] { "--synthetic-uninstall", "--synthetic-parent-uninstall" })
{
    var keyName = "SystemCorePhase5Test-" + Guid.NewGuid().ToString("N");
    var nonce = Guid.NewGuid().ToString("N");
    var executable = Path.Combine(AppContext.BaseDirectory, "SystemCore.UninstallerTests.exe");
    using var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
    try
    {
        using (var key = user.CreateSubKey(RegistryApplicationProvider.UninstallRoot + "\\" + keyName))
        {
            key.SetValue("DisplayName", "SystemCore synthetic uninstall fixture");
            key.SetValue("Publisher", "SystemCore test harness");
            key.SetValue("SystemCoreTestNonce", nonce);
            key.SetValue("UninstallString", $"\"{executable}\" {nativeMode} {keyName} {nonce}");
        }
        var provider = new RegistryApplicationProvider(identity);
        var inventory = new FixtureInventory(provider, keyName);
        using var executor = new UninstallExecutor(inventory, new NativeUninstallRunner(), new UninstallCompletionDetector(inventory));
        var snapshot = await executor.RefreshAsync(default);
        Check("Synthetic registry app detected", snapshot.Inventory.Applications.Count == 1);
        var app = snapshot.Inventory.Applications.Single();
        Check("Synthetic uninstaller command validated", app.CanUninstall);
        var outcome = await executor.UninstallAsync(new(snapshot.Id, app.Id), null, default);
        Check("Native synthetic executable removed registration: " + nativeMode, outcome.State == UninstallState.Removed && outcome.NativeExitCode == 0);
        var replay = await executor.UninstallAsync(new(snapshot.Id, app.Id), null, default);
        Check("Consumed uninstall session cannot replay", replay.State == UninstallState.Failed);
    }
    finally { user.DeleteSubKey(RegistryApplicationProvider.UninstallRoot + "\\" + keyName, false); }
}
await BoundaryTests.Run(Check);
await InventoryPresentationTests.Run(Check);
await GameDiscoveryTests.Run(Check);
await AutomaticLeftoverTests.Run(Check);
Check("Unified library model distinguishes games without replacing native type", typeof(InstalledApplication).GetProperty("ItemKind") is not null && typeof(InstalledApplication).GetProperty("Source") is not null);
if (args.Contains("--leftover-scan")) await LeftoverScannerTests.Run(Check);
if (args.Contains("--leftover-delete")) await LeftoverScannerTests.Run(Check, true);
if (args.Length == 2 && args[0] == "--lifecycle-core")
{
    using var process = Process.GetCurrentProcess();
    var clock = Stopwatch.StartNew(); var observations = new List<object>();
    for (var cycle = 0; cycle <= 20; cycle++)
    {
        if (cycle > 0) await LeftoverScannerTests.Run(Check, true);
        process.Refresh(); var gc = GC.GetGCMemoryInfo();
        observations.Add(new { Cycle = cycle, Seconds = clock.Elapsed.TotalSeconds, PrivateBytes = process.PrivateMemorySize64,
            WorkingSet = process.WorkingSet64, ManagedBytes = GC.GetTotalMemory(false), gc.HeapSizeBytes, gc.TotalCommittedBytes,
            Handles = process.HandleCount, Threads = process.Threads.Count });
        File.WriteAllText(args[1], JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
        if (cycle < 20) await Task.Delay(3000);
    }
    for (var sample = 1; sample <= 4; sample++)
    {
        await Task.Delay(30000);
        process.Refresh(); var gc = GC.GetGCMemoryInfo();
        observations.Add(new { Cycle = 20, CooldownSeconds = sample * 30, Seconds = clock.Elapsed.TotalSeconds, PrivateBytes = process.PrivateMemorySize64,
            WorkingSet = process.WorkingSet64, ManagedBytes = GC.GetTotalMemory(false), gc.HeapSizeBytes, gc.TotalCommittedBytes,
            Handles = process.HandleCount, Threads = process.Threads.Count });
        File.WriteAllText(args[1], JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
    }
}
Console.WriteLine(JsonSerializer.Serialize(new { FinalChecks = checks.Count, Failed = checks.Count(c => !c.Passed) }));
return checks.All(c => c.Passed) ? 0 : 1;

sealed class FixtureInventory(IWin32ApplicationProvider provider, string keyName) : IInstalledApplicationInventory
{
    public async Task<InventoryResult> ReadAsync(CancellationToken token)
    {
        var result = await provider.ReadAsync(token);
        return result with { Applications = result.Applications.Where(a => a.RegistrySource is { Hive: InventoryHive.CurrentUser, View: InventoryView.Registry64 } source && source.KeyName == keyName).ToArray() };
    }
}
