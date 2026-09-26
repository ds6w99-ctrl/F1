using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Win32;
using SystemCore.Core.Compatibility;

// Reads the REAL current host and published metadata. Does not emulate other OS builds.
if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: SystemCore.CompatibilityTests <repository-root> [app-publication-directory]");
    return 64;
}

var root = Path.GetFullPath(args[0]);
var publication = Path.GetFullPath(args.Length == 2 ? args[1] : "artifacts/publish/App", root);
using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
using var identity = WindowsIdentity.GetCurrent();
var version = Environment.OSVersion.Version;
var arch = RuntimeInformation.OSArchitecture;
var edition = key?.GetValue("EditionID") as string;
var client = key?.GetValue("InstallationType") as string == "Client";
var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
var checks = new List<(string Name, bool Passed)>
{
    ("Current host meets Windows 11 23H2+ x64 client floor",
        CompatibilityRules.IsCandidate(version, arch == Architecture.X64, client)),
    ("Current host edition is in scope", CompatibilityRules.IsSupportedEdition(edition)),
    ("Compatibility runner is not elevated", !elevated),
    ("Published F1 PRI exists", File.Exists(Path.Combine(publication, "F1.pri"))),
    ("Published native WinUI exists", File.Exists(Path.Combine(publication, "Microsoft.UI.Xaml.dll")))
};
foreach (var project in new[] { "App", "ElevatedHost" })
{
    var manifest = XDocument.Load(Path.Combine(root, $"src/SystemCore.{project}/app.manifest"));
    var expected = project == "App" ? "asInvoker" : "requireAdministrator";
    checks.Add(($"{project} source manifest requests {expected}", manifest.Descendants()
        .Single(x => x.Name.LocalName == "requestedExecutionLevel").Attribute("level")?.Value == expected));
    var directory = project == "App" ? publication : Path.Combine(publication, "ElevatedHost");
    var path = Path.Combine(directory, project == "App" ? "F1.runtimeconfig.json" : "SystemCore.ElevatedHost.runtimeconfig.json");
    using var config = JsonDocument.Parse(File.ReadAllText(path));
    var included = config.RootElement.GetProperty("runtimeOptions").GetProperty("includedFrameworks");
    checks.Add(($"{project} includes private .NET 10.0.12 runtime", included.EnumerateArray().Any(x =>
        x.GetProperty("name").GetString() == "Microsoft.NETCore.App" &&
        x.GetProperty("version").GetString() == "10.0.12")));
}
Console.WriteLine(JsonSerializer.Serialize(new
{
    Scope = "Actual current host + source manifests + publication metadata only. Not clean-machine certification.",
    WindowsVersion = version.ToString(), BuildRevision = key?.GetValue("UBR"),
    Edition = edition, Architecture = arch.ToString(), Elevated = elevated,
    Results = checks.Select(x => new { Test = x.Name, Result = x.Passed ? "PASS" : "FAIL" })
}, new JsonSerializerOptions { WriteIndented = true }));
return checks.All(x => x.Passed) ? 0 : 1;
