using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using SystemCore.Core.Cleaner;
using SystemCore.Core.Settings;
using SystemCore.Core.Startup;
using SystemCore.Infrastructure;
using SystemCore.Infrastructure.Cleaner;
using SystemCore.Infrastructure.Settings;
using SystemCore.Infrastructure.Startup;
using SystemCore.Infrastructure.Uninstaller;

var checks = 0;
void Check(string name, bool ok) { checks++; Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {name}"); if (!ok) throw new InvalidOperationException(name); }
bool Refused(Action action) { try { action(); return false; } catch (Exception e) when (e is IOException or JsonException or System.Security.Cryptography.CryptographicException) { return true; } }
var root = Path.Combine(Path.GetTempPath(), "SystemCoreSecurity-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string?[] invalid = [null, "", "..", @"C:\test\..\escape", @"\\server\share\file", @"\\?\C:\test\file", @"\\.\C:\test", @"C:\test\f:stream", "C:\\test\\a\0b", @"C:\test\NUL.txt", new('x', 100000)];
    foreach (var path in invalid) Check("Unsafe path rejected " + checks, CleanerSafety.Canonical(path) is null);
    Check("Sibling prefix excluded", !CleanerSafety.IsChild(@"C:\test", @"C:\test-other\file"));
    Check("Root excluded", !CleanerSafety.IsChild(@"C:\test", @"C:\test"));
    foreach (var command in new[] { "cmd.exe /c whoami", "powershell.exe -EncodedCommand AAA", "\"C:\\a.exe\" & calc", "\"C:\\a.exe", "C:\\a.exe\0 --x", "relative.exe", "\\\\host\\a.exe", new string('x', 100000) })
        Check("Uninstall injection refused " + checks, UninstallCommandParser.Parse(command) is null);
    var now = DateTimeOffset.UtcNow; var nonce = new string('A', 64);
    var item = new ElevatedCleanerItem(Guid.NewGuid(), CleanerCategory.WindowsTemp, "fixture.tmp", new CleanerIdentity(1, 1, 1, now.AddDays(-2).UtcDateTime.ToFileTimeUtc(), 1, now.AddDays(-3).UtcDateTime.ToFileTimeUtc()));
    var request = new ElevatedCleanerRequest(1, nonce, Guid.NewGuid(), now, [item]);
    Check("Typed helper request accepted for validation only", CleanerElevationProtocol.Validate(request, nonce, root, now));
    foreach (var changed in new[] { request with { Version = 99 }, request with { Nonce = new string('B', 64) }, request with { Items = new ElevatedCleanerItem[2001] }, request with { Items = [item with { Category = (CleanerCategory)999 }] }, request with { Items = [item with { RelativePath = "..\\escape" }] }, request with { ScannedAt = now.AddHours(-1) }, request with { Items = [item, item] } })
        Check("Malformed helper request refused " + checks, !CleanerElevationProtocol.Validate(changed, nonce, root, now));
    Check("Null helper/nonce refused", !CleanerElevationProtocol.Validate(null, null, root, now) && !StartupElevationProtocol.Validate(null, null, now));
    foreach (var length in new[] { -1, 0, int.MaxValue, CleanerElevationProtocol.MaximumPayload + 1 })
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(length)); var rejected = false;
        try { await CleanerElevationProtocol.ReadAsync<ElevatedCleanerRequest>(stream, default); } catch (InvalidDataException) { rejected = true; }
        Check("Frame length rejected before allocation " + length, rejected);
    }
    foreach (var text in new[] { "null", "{", "{\"Version\":1,\"Unknown\":true}", new string('[', 20) + new string(']', 20) })
    {
        using var stream = new MemoryStream(); var bytes = Encoding.UTF8.GetBytes(text); stream.Write(BitConverter.GetBytes(bytes.Length)); stream.Write(bytes); stream.Position = 0;
        var rejected = false; try { await CleanerElevationProtocol.ReadAsync<ElevatedCleanerRequest>(stream, default); } catch (Exception e) when (e is JsonException or InvalidDataException) { rejected = true; }
        Check("Malformed JSON frame rejected " + checks, rejected);
    }
    var random = new Random(913); var chars = "abcXYZ012\\/:\".\0\r\n\u202e\ud800";
    for (var i = 0; i < 1000; i++)
    {
        var value = new string(Enumerable.Range(0, random.Next(0, 300)).Select(_ => chars[random.Next(chars.Length)]).ToArray());
        _ = CleanerSafety.Canonical(value); _ = UninstallCommandParser.Parse(value); _ = StartupCommandParser.Read(value); _ = StartupStateService.ValidId(value);
    }
    Check("1000 seeded malformed parser inputs complete without crash/execution", true);
    var preferences = Path.Combine(root, "preferences.json");
    foreach (var text in new[] { "null", "[]", "{", "{\"theme\":{},\"language\":99}", new string('x', 4097), "{\"$type\":\"System.Diagnostics.Process\",\"theme\":\"Dark\"}" })
    {
        File.WriteAllText(preferences, text); var settings = new LocalUserSettingsService(preferences, CultureInfo.GetCultureInfo("en")); settings.SetLanguage("ar");
        Check("Malformed settings remain usable and bounded " + checks, settings.LastSaveSucceeded && new FileInfo(preferences).Length <= 4096 && settings.GetLanguage() == "ar");
    }
    var outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "preferences.json"), "sentinel");
    var link = Path.Combine(root, "redirected"); JunctionFixture.Create(link, outside);
    try { var settings = new LocalUserSettingsService(Path.Combine(link, "preferences.json"), CultureInfo.GetCultureInfo("en")); settings.SetTheme(UserTheme.Dark); Check("Redirected preferences refuse write", !settings.LastSaveSucceeded && File.ReadAllText(Path.Combine(outside, "preferences.json")) == "sentinel"); }
    finally { Directory.Delete(link); }
    var helperFolder = Path.Combine(root, "ElevatedHost"); Directory.CreateDirectory(helperFolder); File.WriteAllText(Path.Combine(helperFolder, "SystemCore.ElevatedHost.exe"), "fixture-never-executed");
    using (var helper = new HelperLaunchLease(root))
    {
        var start = helper.StartInfo("startup", "SystemCore-Startup-" + Guid.NewGuid().ToString("N"), 123, nonce);
        Check("Helper path and arguments are separate and installation-relative", start.FileName == Path.Combine(helperFolder, "SystemCore.ElevatedHost.exe") && start.ArgumentList.Count == 4 && start.Arguments == "" && start.Verb == "runas");
        Check("Helper launch injection rejected", Refused(() => helper.StartInfo("startup", "pipe & calc", 123, nonce)));
        var leftover = helper.StartInfo("leftover", "SystemCore-Leftover-" + Guid.NewGuid().ToString("N"), 123, nonce);
        Check("Leftover helper is a distinct, closed operation", leftover.ArgumentList[0] == "--leftover" && leftover.ArgumentList.Count == 4);
        Check("Helper operation cannot borrow another prefix", Refused(() => helper.StartInfo("leftover", "SystemCore-Cleaner-" + Guid.NewGuid().ToString("N"), 123, nonce)));
        Check("Unknown helper operation rejected", Refused(() => helper.StartInfo("update", "SystemCore-Leftover-" + Guid.NewGuid().ToString("N"), 123, nonce)));
    }
    var stateRoot = Path.Combine(root, "state"); Directory.CreateDirectory(stateRoot);
    var states = new StartupStateService(new EmptyLocations(), stateRoot, Path.Combine(root, "machine"));
    foreach (var text in new[] { "null", "{}", "{\"Version\":1,\"Item\":null}", "{\"Version\":1,\"Unexpected\":true}" })
    {
        File.WriteAllBytes(states.StatePath(nonce, false), StartupDataProtection.Protect(Encoding.UTF8.GetBytes(text), false));
        Check("Protected but malformed state rejected " + checks, Refused(() => states.Load(nonce, false)));
    }
    var registryPath = "Software\\SystemCore-Security-" + Guid.NewGuid().ToString("N");
    try
    {
        using var key = Registry.CurrentUser.CreateSubKey(registryPath);
        key.SetValue("Text", "fixture", RegistryValueKind.String); key.SetValue("Large", new byte[65537], RegistryValueKind.Binary);
        Check("Bounded Registry reader preserves normal strings", Equals(BoundedRegistry.ReadValue(key, "Text"), "fixture"));
        Check("Oversized Registry value rejected before data allocation", Refused(() => BoundedRegistry.ReadValue(key, "Large")));
        Check("Missing Registry value distinguished", BoundedRegistry.ReadValue(key, "Missing") is null && !BoundedRegistry.HasValue(key, "Missing"));
        var names = BoundedRegistry.Names(key, true, 1, out var complete); Check("Registry enumeration cap reports incomplete", names.Length == 1 && !complete);
    }
    finally { Registry.CurrentUser.DeleteSubKeyTree(registryPath); }
}
finally { Directory.Delete(root, true); }
Console.WriteLine(JsonSerializer.Serialize(new { Checks = checks, Failed = 0, FuzzInputs = 1000 }));

sealed class EmptyLocations : IStartupLocations
{
    public IReadOnlyList<StartupRegistryLocation> Registry => [];
    public IReadOnlyList<StartupFolderLocation> Folders => [];
}
