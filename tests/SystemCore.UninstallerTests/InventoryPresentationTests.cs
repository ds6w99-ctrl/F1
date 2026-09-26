using SystemCore.Core.Uninstaller;
using SystemCore.Infrastructure.Uninstaller;

internal static class InventoryPresentationTests
{
    public static async Task Run(Action<string, bool> check)
    {
        var resolver = new ApplicationIdentityResolver();
        var source = new RegistryApplicationSource(InventoryHive.CurrentUser, InventoryView.Registry64, "F1-ReadOnly-Presentation-Test");
        var executable = Path.Combine(AppContext.BaseDirectory, "SystemCore.UninstallerTests.exe");
        var values = new Dictionary<string, object?> { ["DisplayName"] = "Normal application", ["UninstallString"] = $"\"{executable}\" --not-executed" };
        InstalledApplication Make() => RegistryApplicationProvider.Normalize(source, values, resolver)!;
        var app = Make();
        check("Removable normal Win32 visible", RemovableApplications.IsVisible(app));
        foreach (var flag in new[] { "SystemComponent", "NoRemove" })
        {
            values[flag] = 1; check(flag + " hidden from main list", !RemovableApplications.IsVisible(Make())); values.Remove(flag);
        }
        values["UninstallString"] = @"C:\missing-f1-uninstall.exe /remove";
        check("Broken uninstall registration hidden", !RemovableApplications.IsVisible(Make()));
        values.Remove("UninstallString");
        check("Missing uninstall mechanism hidden", !RemovableApplications.IsVisible(Make()));
        var msi = app with { Id = "msi-test", ApplicationType = ApplicationType.Msi, ProductCode = Guid.NewGuid(), RegistrySource = null };
        check("Verified MSI identity visible", RemovableApplications.IsVisible(msi));
        check("Unverified MSI hidden", !RemovableApplications.IsVisible(msi with { CanUninstall = false }));
        check("Invalid MSI product code hidden", !RemovableApplications.IsVisible(msi with { ProductCode = Guid.Empty }));
        var msix = app with { ApplicationType = ApplicationType.Msix, PackageFullName = "Test_1.0.0.0_x64__test", RegistrySource = null };
        check("Removable MSIX main identity visible", RemovableApplications.IsVisible(msix));
        check("Framework MSIX hidden", !RemovableApplications.IsVisible(msix with { IsFramework = true }));
        check("Protected MSIX hidden", !RemovableApplications.IsVisible(msix with { IsProtected = true }));
        check("WindowsAppRuntime framework hidden", !RemovableApplications.IsVisible(msix with { DisplayName = "WindowsAppRuntime.1.8", IsFramework = true }));
        check("Resource and optional helper MSIX restricted", MsixApplicationProvider.IsRestricted(false, true, false, false, false) && MsixApplicationProvider.IsRestricted(false, false, true, false, false));
        var inventory = new InstalledApplicationInventory(new Win32Rows([app, app with { Id = "protected", IsProtected = true }]), new MsiRows([msi with { CanUninstall = false }]), new MsixRows([msix with { Id = "framework", IsFramework = true, CanUninstall = false }]));
        var raw = await inventory.ReadAsync(default);
        check("Internal inventory retains hidden registrations", raw.Applications.Count == 4 && raw.Applications.Count(RemovableApplications.IsVisible) == 1);
        check("Unconfirmed native MSI stays unavailable after merge", !raw.Applications.Single(a => a.Id == "msi-test").CanUninstall);
        check("DisplayIcon with quoted path and index", ApplicationIconReader.Parse($"\"{executable}\",0") is { Index: 0 });
        check("DisplayIcon negative resource index parsed", ApplicationIconReader.Parse($"\"{executable}\",-123") is { Index: -123 });
        foreach (var invalid in new[] { "", @"\\server\share\icon.dll,0", @"relative.ico", @"C:\missing.ico", executable + ":stream", $"\"{executable}\",nonsense", "C:\\bad\nicon.exe" })
            check("Malformed/missing icon metadata falls back", ApplicationIconReader.Parse(invalid) is null);
        var systemIcon = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll");
        var bytes = await ApplicationIconReader.ReadAsync(app with { IconLocation = $"\"{systemIcon}\",0" }, default);
        check("Real PE resource icon read as PNG without launch", bytes is { Length: > 8 } && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        check("Missing icon has explicit fallback result", await ApplicationIconReader.ReadAsync(app with { IconLocation = @"C:\missing.ico", InstallLocation = null }, default) is null);
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        check("Trusted installed executable fallback", await ApplicationIconReader.ReadAsync(app with { IconLocation = null, InstallLocation = system, RegisteredUninstallCommand = $"\"{Path.Combine(system, "notepad.exe")}\"" }, default) is { Length: > 8 });
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await ApplicationIconReader.ReadAsync(app, cancelled.Token); check("Icon loading cancellation", false); }
        catch (OperationCanceledException) { check("Icon loading cancellation", true); }
    }
    private sealed class Win32Rows(InstalledApplication[] rows) : IWin32ApplicationProvider { public Task<InventoryResult> ReadAsync(CancellationToken token) => Task.FromResult(new InventoryResult(rows, true, [])); }
    private sealed class MsiRows(InstalledApplication[] rows) : IMsiApplicationProvider { public Task<InventoryResult> ReadAsync(CancellationToken token) => Task.FromResult(new InventoryResult(rows, true, [])); }
    private sealed class MsixRows(InstalledApplication[] rows) : IMsixApplicationProvider { public Task<InventoryResult> ReadAsync(CancellationToken token) => Task.FromResult(new InventoryResult(rows, true, [])); }
}
