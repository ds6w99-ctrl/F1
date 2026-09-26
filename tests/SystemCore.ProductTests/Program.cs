using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using SystemCore.Core.Diagnostics;
using SystemCore.Core.Settings;
using SystemCore.Core.SystemInformation;
using SystemCore.Core.Tools;
using SystemCore.Infrastructure.Diagnostics;
using SystemCore.Infrastructure.Settings;
using SystemCore.Infrastructure.Tools;

var results = new List<(string Name, bool Passed)>();
void Check(string name, bool ok) { results.Add((name, ok)); Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {name}"); }
var root = Path.Combine(Path.GetTempPath(), "SystemCoreProductTests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
try
{
    var platform = new FakePlatform(); var tools = new WindowsToolsService(platform);
    var catalog = await tools.ReadAsync(default);
    Check("Catalog contains exactly fourteen unique closed IDs", catalog.Count == 14 && catalog.Select(t => t.Id).Distinct().Count() == 14);
    Check("Catalog covers all six tool groups", catalog.Select(t => t.Group).Distinct().Count() == 6);
    foreach (var tool in catalog)
    {
        Check("Trusted target " + tool.Id, tool.LaunchType == ToolLaunchType.WindowsUri ? tool.Target.StartsWith("ms-settings:", StringComparison.Ordinal) || tool.Target == "windowsdefender:" : Path.IsPathFullyQualified(tool.Target) && Path.GetDirectoryName(tool.Target) == platform.SystemDirectory);
        Check("Separate allowlisted arguments " + tool.Id, tool.Arguments.Count <= 1 && tool.Arguments.All(a => Path.IsPathFullyQualified(a) && Path.GetDirectoryName(a) == platform.SystemDirectory && Path.GetExtension(a) is ".msc" or ".cpl"));
        Check("No command interpreter " + tool.Id, !new[] { "cmd.exe", "powershell.exe", "pwsh.exe" }.Contains(Path.GetFileName(tool.Target), StringComparer.OrdinalIgnoreCase));
        Check("ID launch dispatch " + tool.Id, await tools.OpenAsync(tool.Id, default) == ToolLaunchResult.Launched && platform.Last?.Target == tool.Target);
    }
    var calls = platform.Calls;
    Check("Unknown ID cannot reach launch platform", await tools.OpenAsync((WindowsToolId)int.MaxValue, default) == ToolLaunchResult.Unavailable && calls == platform.Calls);
    platform.Ready = false;
    Check("Unavailable tool is disabled in inventory", (await tools.ReadAsync(default)).All(t => t.Availability == ToolAvailability.Unavailable));
    Check("Unavailable tool cannot launch", await tools.OpenAsync(WindowsToolId.TaskManager, default) == ToolLaunchResult.Unavailable && calls == platform.Calls);
    platform.Ready = true;
    foreach (var test in new[] { (5, ToolLaunchResult.AccessDenied), (1223, ToolLaunchResult.Cancelled), (2, ToolLaunchResult.Unavailable), (87, ToolLaunchResult.Failed) })
    { platform.Error = test.Item1; Check("Launch error is truthful " + test.Item1, await tools.OpenAsync(WindowsToolId.TaskManager, default) == test.Item2); }
    platform.Error = null;
    var settingsPath = Path.Combine(root, "preferences.json");
    LocalUserSettingsService Settings(string culture = "en") => new(settingsPath, CultureInfo.GetCultureInfo(culture));
    Check("Missing settings uses safe defaults", Settings().GetTheme() == UserTheme.System && Settings().GetLanguage() == "en");
    Check("Arabic Windows default", Settings("ar").GetLanguage() == "ar");
    File.WriteAllText(settingsPath, "{bad json"); Check("Malformed JSON falls back", Settings().GetTheme() == UserTheme.System && Settings().GetLanguage() == "en");
    File.WriteAllText(settingsPath, "{\"theme\":false,\"language\":\"ar\",\"schemaVersion\":\"invalid\",\"future\":{\"enabled\":true}}");
    var settings = Settings(); Check("Invalid key preserves other settings", settings.GetTheme() == UserTheme.System && settings.GetLanguage() == "ar");
    settings.SetTheme(UserTheme.Dark);
    using (var json = JsonDocument.Parse(File.ReadAllText(settingsPath)))
    { Check("Schema version persisted", json.RootElement.GetProperty("schemaVersion").GetInt32() == 1); Check("Unknown fields preserved on write", json.RootElement.GetProperty("future").GetProperty("enabled").GetBoolean()); }
    Check("Theme and language survive reload", Settings().GetTheme() == UserTheme.Dark && Settings().GetLanguage() == "ar");
    File.WriteAllText(settingsPath, "{\"schemaVersion\":3,\"theme\":\"Light\",\"language\":\"en\",\"future\":[1,2,3]}");
    settings = Settings(); settings.SetLanguage("ar");
    using (var json = JsonDocument.Parse(File.ReadAllText(settingsPath))) Check("Future schema and fields retained", json.RootElement.GetProperty("schemaVersion").GetInt32() == 3 && json.RootElement.GetProperty("future").GetArrayLength() == 3);
    var prior = File.ReadAllBytes(settingsPath);
    using (var locked = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
    { settings.SetTheme(UserTheme.Dark); Check("Atomic write failure reported", !settings.LastSaveSucceeded); Check("Write failure preserves previous file", File.ReadAllBytes(settingsPath).SequenceEqual(prior)); Check("Session choice remains usable on storage failure", settings.GetTheme() == UserTheme.Dark); }
    Check("Failed atomic write cleans only its temporary file", Directory.GetFiles(root, "*.tmp").Length == 0);
    settings.SetTheme(UserTheme.Dark); Check("Save can recover after storage failure", settings.LastSaveSucceeded && Settings().GetTheme() == UserTheme.Dark);
    File.WriteAllText(settingsPath, new string('x', 4097)); Check("Oversize settings bounded", Settings().GetTheme() == UserTheme.System);
    var data = Path.Combine(root, "data");
    await using (var diagnostics = new LocalAppDiagnostics(data, "0.8.0-dev", tools, new(3, 1024)))
    {
        for (var index = 0; index < 120; index++) { diagnostics.Record(DiagnosticFeature.Tools, DiagnosticEvent.ToolLaunch, index % 14); if (index % 8 == 0) await diagnostics.FlushAsync(); }
        await diagnostics.FlushAsync();
        var state = diagnostics.GetStatus();
        Check("Log rotation stays within file/count cap", state.Available && state.Files is > 1 and <= 3 && state.Bytes <= 3072);
        Check("Every rotated file bounded", Directory.GetFiles(Path.Combine(data, "Logs"), "*.jsonl").All(f => new FileInfo(f).Length <= 1024));
        var keep = Path.Combine(data, "Logs", "personal-file.txt"); File.WriteAllText(keep, "keep");
        File.WriteAllText(Path.Combine(data, "preferences.json"), "keep-settings"); Directory.CreateDirectory(Path.Combine(data, "StartupState")); File.WriteAllText(Path.Combine(data, "StartupState", "receipt.json"), "keep-state");
        var export = await diagnostics.ExportAsync(default);
        Check("Diagnostic ZIP created and verified", export.Succeeded && File.Exists(Path.Combine(data, "Exports", export.FileName!)));
        using (var zip = ZipFile.OpenRead(Path.Combine(data, "Exports", export.FileName!)))
        {
            Check("Export has only allowlisted entries", zip.Entries.Select(e => e.FullName).OrderBy(x => x).SequenceEqual(new[] { "logs/recent.jsonl", "manifest.json" }));
            using var metadata = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
            Check("Export versions and architecture", metadata.RootElement.GetProperty("AppVersion").GetString() == "0.8.0-dev" && metadata.RootElement.GetProperty("Architecture").GetString() == "X64");
            using var reader = new StreamReader(zip.GetEntry("logs/recent.jsonl")!.Open()); var content = reader.ReadToEnd();
            Check("Recent typed logs present", content.Contains("ToolLaunch", StringComparison.Ordinal) && export.Count > 0);
            Check("No private paths or machine identifiers exported", !content.Contains(root, StringComparison.OrdinalIgnoreCase) && !content.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase) && !content.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase));
        }
        var cleared = await diagnostics.ClearOldAsync(default); await diagnostics.FlushAsync();
        Check("Clear old logs preserves active log", cleared.Succeeded && cleared.Count > 0 && diagnostics.GetStatus().Files == 1);
        Check("Clear logs preserves unrelated files/settings/startup state/exports", File.ReadAllText(keep) == "keep" && File.ReadAllText(Path.Combine(data, "preferences.json")) == "keep-settings" && File.ReadAllText(Path.Combine(data, "StartupState", "receipt.json")) == "keep-state" && File.Exists(Path.Combine(data, "Exports", export.FileName!)));
        var secret = "PRIVATE_TOKEN_4d1c_private.docx_192.0.2.55_00-AA-BB-CC-DD-EE";
        var planted = Path.Combine(data, "Logs", $"systemcore-20000101000000000-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(planted, "{\"Format\":\"SystemCore.Log.v1\"}\n" + JsonSerializer.Serialize(new { Timestamp = DateTimeOffset.UtcNow, Feature = 0, Event = 0, Code = 0, Secret = secret, Path = @"C:\Users\Person\private.docx", Arguments = "--password=SECRET" }) + "\n{\"Timestamp\":\"invalid\",\"Token\":\"SECRET\"}\n");
        var sanitized = await diagnostics.ExportAsync(default);
        using (var zip = ZipFile.OpenRead(Path.Combine(data, "Exports", sanitized.FileName!)))
        {
            var text = string.Join("\n", zip.Entries.Select(e => { using var reader = new StreamReader(e.Open()); return reader.ReadToEnd(); }));
            Check("Export drops unexpected secret/path/address/filename/argument fields", !text.Contains(secret, StringComparison.Ordinal) && !text.Contains("Person", StringComparison.Ordinal) && !text.Contains("SECRET", StringComparison.Ordinal) && !text.Contains("private.docx", StringComparison.Ordinal));
        }
        for (var index = 0; index < 3; index++) Check("Manual export within bounded package count " + index, (await diagnostics.ExportAsync(default)).Succeeded);
        Check("Sixth export refuses instead of growing unbounded", !(await diagnostics.ExportAsync(default)).Succeeded && Directory.GetFiles(Path.Combine(data, "Exports"), "*.zip").Length == 5);
        var sentinel = Path.Combine(root, "outside-sentinel.txt"); File.WriteAllText(sentinel, "{\"Format\":\"SystemCore.Log.v1\"}\nretain");
        var link = Path.Combine(data, "Logs", $"systemcore-20000101000000001-{Guid.NewGuid():N}.jsonl");
        if (!Native.CreateHardLinkW(link, sentinel, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        await diagnostics.ClearOldAsync(default);
        Check("Hardlinked log rejected without deleting outside file", File.Exists(link) && File.ReadAllText(sentinel).EndsWith("retain", StringComparison.Ordinal));
        File.Delete(link);
    }
    var redirectedData = Path.Combine(root, "redirected"); Directory.CreateDirectory(redirectedData);
    var outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside);
    JunctionFixture.Create(Path.Combine(redirectedData, "Logs"), outside);
    await using (var diagnostics = new LocalAppDiagnostics(redirectedData, "0.8.0-dev", tools))
    {
        diagnostics.Record(DiagnosticFeature.App, DiagnosticEvent.Started); await diagnostics.FlushAsync();
        Check("Reparse log root fails closed", !diagnostics.GetStatus().Available && Directory.GetFileSystemEntries(outside).Length == 0);
        Check("Reparse log clearing refused", !(await diagnostics.ClearOldAsync(default)).Succeeded && Directory.GetFileSystemEntries(outside).Length == 0);
    }
    Directory.Delete(Path.Combine(redirectedData, "Logs"));
    var en = XDocument.Load("src/SystemCore.App/Resources/Shell.resx").Root!.Elements("data").ToDictionary(e => e.Attribute("name")!.Value, e => e.Element("value")!.Value);
    var ar = XDocument.Load("src/SystemCore.App/Resources/Shell.ar.resx").Root!.Elements("data").ToDictionary(e => e.Attribute("name")!.Value, e => e.Element("value")!.Value);
    Check("All localization keys match", en.Keys.OrderBy(x => x).SequenceEqual(ar.Keys.OrderBy(x => x)));
    Check("No empty localized strings", en.Values.All(v => !string.IsNullOrWhiteSpace(v)) && ar.Values.All(v => !string.IsNullOrWhiteSpace(v)));
    Check("UI uses approved unit labels", !en.Values.Concat(ar.Values).Any(v => System.Text.RegularExpressions.Regex.IsMatch(v, @"\b(KiB|MiB|GiB|TiB)\b")));
    foreach (var tool in catalog) Check("Tool localized in both languages " + tool.Id, en.ContainsKey(tool.NameKey) && en.ContainsKey(tool.DescriptionKey) && ar.ContainsKey(tool.NameKey) && ar.ContainsKey(tool.DescriptionKey));
    foreach (var culture in new[] { CultureInfo.GetCultureInfo("en"), CultureInfo.GetCultureInfo("ar") })
    { Check("Shared size/rate formatting " + culture.Name, InfoFormatting.Size(1024UL * 1024, culture).EndsWith("MB", StringComparison.Ordinal) && InfoFormatting.Rate(1024, culture).EndsWith("KB/s", StringComparison.Ordinal)); Check("Shared localized duration " + culture.Name, InfoFormatting.Duration(TimeSpan.FromSeconds(90061), culture, "{0}:{1}:{2}:{3}") == "1:1:1:1"); }
    if (args.Contains("--integration"))
    {
        var real = new WindowsToolsService(new WindowsToolPlatform()); var inventory = await real.ReadAsync(default);
        Check("Actual host tool inventory complete", inventory.Count == 14 && inventory.All(t => t.Availability != ToolAvailability.Checking));
        Check("Current host Windows system tool resolves", inventory.Single(t => t.Id == WindowsToolId.SystemInformation).Availability == ToolAvailability.Available);
        Console.WriteLine("REAL TOOLS " + JsonSerializer.Serialize(inventory.Select(t => new { t.Id, t.LaunchType, t.Target, t.Arguments, t.RequiresElevation, t.Availability })));
        var before = Process.GetProcessesByName("msinfo32"); var priorIds = before.Select(p => p.Id).ToHashSet(); foreach (var process in before) process.Dispose();
        var launched = await real.OpenAsync(WindowsToolId.SystemInformation, default);
        Check("Real read-only Windows utility launch accepted", launched == ToolLaunchResult.Launched);
        await Task.Delay(2500);
        foreach (var process in Process.GetProcessesByName("msinfo32"))
        {
            using (process) if (!priorIds.Contains(process.Id)) { if (process.MainWindowHandle != IntPtr.Zero) { process.CloseMainWindow(); Check("Test-launched Windows utility closes normally", process.WaitForExit(15000)); } }
        }
    }
}
finally { Directory.Delete(root, true); }
Console.WriteLine(JsonSerializer.Serialize(new { Checks = results.Count, Failed = results.Count(r => !r.Passed) }));
return results.All(r => r.Passed) ? 0 : 1;

internal sealed class FakePlatform : IWindowsToolPlatform
{
    public string SystemDirectory => @"C:\Windows\System32";
    internal bool Ready = true; internal int Calls; internal int? Error; internal WindowsToolItem? Last;
    public Task<bool> AvailableAsync(WindowsToolItem tool, CancellationToken token) => Task.FromResult(Ready);
    public Task<ToolLaunchResult> LaunchAsync(WindowsToolItem tool, CancellationToken token)
    { Calls++; Last = tool; if (Error is { } error) throw new Win32Exception(error); return Task.FromResult(ToolLaunchResult.Launched); }
}
internal static class Native
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CreateHardLinkW(string link, string target, IntPtr security);
}
