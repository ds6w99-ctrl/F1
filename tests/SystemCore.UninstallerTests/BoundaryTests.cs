using System.ComponentModel;
using SystemCore.Core.Uninstaller;
using SystemCore.Infrastructure.Uninstaller;

static class BoundaryTests
{
    public static async Task Run(Action<string, bool> check)
    {
        var identity = new ApplicationIdentityResolver();
        var executable = Path.Combine(AppContext.BaseDirectory, "SystemCore.UninstallerTests.exe");
        foreach (var hive in Enum.GetValues<InventoryHive>())
        foreach (var view in Enum.GetValues<InventoryView>())
        {
            var source = new RegistryApplicationSource(hive, view, "Fixture");
            var values = new Dictionary<string, object?> { ["DisplayName"] = "Same name", ["UninstallString"] = $"\"{executable}\" /remove \"argument with spaces\"" };
            var row = RegistryApplicationProvider.Normalize(source, values, identity)!;
            check($"{hive}/{view} normalized without invented metadata", row.CanUninstall && row.Architecture is null && row.Publisher is null);
            foreach (var flag in new[] { "SystemComponent", "NoRemove" })
            {
                values[flag] = "malformed";
                check($"Malformed {flag} fails closed", !RegistryApplicationProvider.Normalize(source, values, identity)!.CanUninstall);
                values.Remove(flag);
            }
            values["ParentKeyName"] = "Parent";
            check("Child installer entry protected", RegistryApplicationProvider.Normalize(source, values, identity)!.IsProtected);
            values.Remove("ParentKeyName");
            values["EstimatedSize"] = -1;
            values["InstallDate"] = "invalid";
            check("Invalid date/size remain unknown", RegistryApplicationProvider.Normalize(source, values, identity) is { EstimatedSize: null, InstallDate: null });
        }
        var parsed = UninstallCommandParser.Parse($"\"{executable}\" /remove \"space argument\"");
        check("Quoted executable and spaced argument", parsed is not null && parsed.Arguments.SequenceEqual(new[] { "/remove", "space argument" }));
        var unquoted = UninstallCommandParser.Parse(executable + " /remove");
        check("Unquoted executable requires unambiguous token", executable.Any(char.IsWhiteSpace) ? unquoted is null : unquoted?.Executable == executable);
        var embedded = UninstallCommandParser.Parse($"\"{executable}\" /remove \"say \\\"hello\\\"\"");
        check("Embedded quoted argument follows Windows parsing", embedded?.Arguments.Last() == "say \"hello\"");
        var variable = "SYSTEMCORE_UNINSTALL_TEST_" + Guid.NewGuid().ToString("N");
        try
        {
            Environment.SetEnvironmentVariable(variable, executable);
            check("Executable environment variable validated after expansion", UninstallCommandParser.Parse($"\"%{variable}%\" /remove")?.Executable == executable);
        }
        finally { Environment.SetEnvironmentVariable(variable, null); }
        check("Missing executable unavailable", UninstallCommandParser.Parse(@"C:\SystemCore-DefinitelyAbsent.exe") is null);
        var product = Guid.NewGuid();
        var msi = RegistryApplicationProvider.Normalize(new(InventoryHive.LocalMachine, InventoryView.Registry64, product.ToString("B")),
            new Dictionary<string, object?> { ["DisplayName"] = "MSI fixture", ["WindowsInstaller"] = 1 }, identity)!;
        check("Registry ProductCode alone does not authorize MSI uninstall", msi.ProductCode == product && !msi.CanUninstall);
        var msiCommand = NativeUninstallRunner.BuildDesktopCommand(msi with { RegisteredUninstallCommand = "cmd.exe /c malicious", CanUninstall = true });
        check("MSI uses canonical Windows executable and typed arguments", msiCommand.Executable == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe") && msiCommand.Arguments.SequenceEqual(new[] { "/x", product.ToString("B") }));
        var rejectedMsi = false;
        try { NativeUninstallRunner.BuildDesktopCommand(msi with { ProductCode = null }); } catch (InvalidOperationException) { rejectedMsi = true; }
        check("Missing MSI identity cannot fall back to registry command", rejectedMsi);
        check("MSIX framework protected", MsixApplicationProvider.IsRestricted(true, false, false, false, false));
        check("MSIX resource protected", MsixApplicationProvider.IsRestricted(false, true, false, false, false));
        check("MSIX optional protected", MsixApplicationProvider.IsRestricted(false, false, true, false, false));
        check("MSIX bundle excluded", MsixApplicationProvider.IsRestricted(false, false, false, true, false));
        check("MSIX system protected", MsixApplicationProvider.IsRestricted(false, false, false, false, true));
        check("MSIX main package boundary", !MsixApplicationProvider.IsRestricted(false, false, false, false, false));
        var one = new InstalledApplication("id-a", "Same name", "Publisher A", "1", null, null, null, null,
            ApplicationType.Win32, $"\"{executable}\"", false, null, null, null,
            new(InventoryHive.CurrentUser, InventoryView.Registry64, "Synthetic"), null, false, false, false, true, null);
        var provider = new Providers(new([one, one with { Id = "id-b", Publisher = "Publisher B" }], true, []));
        var combined = await new InstalledApplicationInventory(provider, new Providers(new([], true, [])), new Providers(new([], true, []))).ReadAsync(default);
        check("Same name different identity stays separate", combined.Applications.Count == 2);
        var alias = one with { Id = "alias-32", RegistrySource = one.RegistrySource! with { View = InventoryView.Registry32 } };
        var aliases = new Providers(new([one, alias], true, []));
        combined = await new InstalledApplicationInventory(aliases, new Providers(new([], true, [])), new Providers(new([], true, []))).ReadAsync(default);
        check("Identical HKCU shared-view aliases collapse", combined.Applications.Count == 1 && combined.Applications[0].Id == one.Id);
        aliases = new Providers(new([one, alias with { Publisher = "Other publisher" }], true, []));
        combined = await new InstalledApplicationInventory(aliases, new Providers(new([], true, [])), new Providers(new([], true, []))).ReadAsync(default);
        check("Different view metadata not merged", combined.Applications.Count == 2);
        foreach (var scenario in new[] { "removed", "still", "unknown", "uac", "cancel", "failure", "changed", "invalid", "protected" })
        {
            var inventory = new FakeInventory(new([one], true, []));
            var runner = new Runner(() =>
            {
                if (scenario == "uac") throw new Win32Exception(1223);
                if (scenario == "cancel") return 1602;
                if (scenario == "failure") throw new IOException();
                if (scenario == "removed") inventory.Result = new([], true, []);
                if (scenario == "unknown") inventory.Result = new([], false, ["unavailable"]);
                return 0;
            });
            if (scenario == "protected") inventory.Result = new([one with { IsProtected = true, CanUninstall = false }], true, []);
            using var executor = new UninstallExecutor(inventory, runner, new UninstallCompletionDetector(inventory));
            var session = await executor.RefreshAsync(default);
            if (scenario == "changed") inventory.Result = new([one with { RegisteredUninstallCommand = "different" }], true, []);
            var result = await executor.UninstallAsync(new(scenario == "invalid" ? Guid.NewGuid() : session.Id, one.Id), null, default);
            var expected = scenario switch { "removed" => UninstallState.Removed, "still" => UninstallState.StillInstalled,
                "unknown" => UninstallState.Unknown, "uac" or "cancel" => UninstallState.Cancelled, _ => UninstallState.Failed };
            check($"Completion / security: {scenario}", result.State == expected && (scenario is not ("changed" or "invalid" or "protected") || runner.Calls == 0));
        }
    }
    sealed class FakeInventory(InventoryResult result) : IInstalledApplicationInventory
    {
        public InventoryResult Result { get; set; } = result;
        public Task<InventoryResult> ReadAsync(CancellationToken token) => Task.FromResult(Result);
    }
    sealed class Providers(InventoryResult result) : IWin32ApplicationProvider, IMsiApplicationProvider, IMsixApplicationProvider
    {
        public Task<InventoryResult> ReadAsync(CancellationToken token) => Task.FromResult(result);
    }
    sealed class Runner(Func<int> run) : INativeUninstallRunner
    {
        public int Calls { get; private set; }
        public Task<int?> RunAsync(InstalledApplication app, CancellationToken token) { Calls++; return Task.FromResult<int?>(run()); }
    }
}
