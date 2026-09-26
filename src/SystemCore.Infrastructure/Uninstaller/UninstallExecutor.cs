using System.ComponentModel;
using System.Diagnostics;
using SystemCore.Core.Uninstaller;
using Windows.Management.Deployment;

namespace SystemCore.Infrastructure.Uninstaller;

public interface INativeUninstallRunner
{
    Task<int?> RunAsync(InstalledApplication application, CancellationToken token);
}

public sealed class UninstallCompletionDetector(IInstalledApplicationInventory inventory) : IUninstallCompletionDetector
{
    public async Task<UninstallState> VerifyAsync(InstalledApplication original, CancellationToken token)
    {
        var current = await inventory.ReadAsync(token).ConfigureAwait(false);
        if (current.Applications.Any(a => a.Id == original.Id)) return UninstallState.StillInstalled;
        return current.Complete ? UninstallState.Removed : UninstallState.Unknown;
    }
}

// The UI can submit only an ID from a short-lived, service-owned inventory.
// Fresh native metadata, not caller-supplied command strings, authorizes launch.
public sealed class UninstallExecutor(IInstalledApplicationInventory inventory, INativeUninstallRunner runner,
    IUninstallCompletionDetector completion, ApplicationRemovalEvidence? evidence = null) : IUninstallExecutor, IDisposable
{
    private readonly SemaphoreSlim operation = new(1, 1);
    private ApplicationInventorySession? session;
    public async Task<ApplicationInventorySession> RefreshAsync(CancellationToken token)
    {
        await operation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            session = null;
            var result = await inventory.ReadAsync(token).ConfigureAwait(false);
            // Clone collections at this ownership boundary.
            session = new(Guid.NewGuid(), DateTimeOffset.UtcNow, result with { Applications = Array.AsReadOnly(result.Applications.ToArray()) });
            return session;
        }
        finally { operation.Release(); }
    }

    public async Task<UninstallOutcome> UninstallAsync(UninstallRequest request, IProgress<UninstallState>? progress, CancellationToken token)
    {
        if (!await operation.WaitAsync(0, token).ConfigureAwait(false)) return new(UninstallState.Failed, request.ApplicationId, null, "Another operation is active");
        int? exit = null;
        try
        {
            var owned = session;
            var application = owned?.Inventory.Applications.SingleOrDefault(a => a.Id == request.ApplicationId);
            if (owned is null || owned.Id != request.InventorySessionId || DateTimeOffset.UtcNow - owned.CreatedAt > TimeSpan.FromMinutes(10)
                || application is null || !application.CanUninstall || application.IsProtected)
                return new(UninstallState.Failed, request.ApplicationId, null, "Invalid or unavailable inventory selection");
            session = null; // A request is single-use, including cancellation/failure.
            var fresh = await inventory.ReadAsync(token).ConfigureAwait(false);
            var current = fresh.Applications.SingleOrDefault(a => a.Id == application.Id);
            if (!fresh.Complete || current != application || !current.CanUninstall || current.IsProtected)
                return new(UninstallState.Failed, request.ApplicationId, null, "Application registration changed or cannot be verified");
            token.ThrowIfCancellationRequested();
            if (evidence is not null) await evidence.CaptureAsync(current, fresh, token).ConfigureAwait(false);
            progress?.Report(UninstallState.Launching);
            var pending = runner.RunAsync(current, token);
            progress?.Report(UninstallState.WaitingForUninstaller);
            exit = await pending.ConfigureAwait(false);
            progress?.Report(UninstallState.UninstallerExited);
            if (exit == 1602) return new(UninstallState.Cancelled, request.ApplicationId, exit, "Native uninstall cancelled");
            progress?.Report(UninstallState.Verifying);
            var state = await completion.VerifyAsync(application, token).ConfigureAwait(false);
            // Never infer removal from exit zero. A parent can exit before its child.
            for (var attempt = 0; state == UninstallState.StillInstalled && attempt < 3; attempt++)
            {
                await Task.Delay(1000, token).ConfigureAwait(false);
                state = await completion.VerifyAsync(application, token).ConfigureAwait(false);
            }
            if (state == UninstallState.StillInstalled && exit is not null and not 0 and not 3010 and not 1641)
                state = UninstallState.Failed;
            return new(state, request.ApplicationId, exit, state == UninstallState.Removed ? null : "Completion requires identity verification; refresh to check again",
                state == UninstallState.Removed ? evidence?.ConfirmRemoved(request.ApplicationId) : null);
        }
        catch (OperationCanceledException) { return new(UninstallState.Cancelled, request.ApplicationId, exit, "Stopped waiting; native installer was not terminated"); }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { return new(UninstallState.Cancelled, request.ApplicationId, null, "Elevation denied"); }
        catch (Exception e) when (e is Win32Exception or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        { return new(UninstallState.Failed, request.ApplicationId, exit, "Native uninstall could not be completed"); }
        finally { operation.Release(); }
    }
    public void Dispose() => operation.Dispose();
}

public sealed class NativeUninstallRunner : INativeUninstallRunner
{
    public async Task<int?> RunAsync(InstalledApplication application, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!application.CanUninstall || application.IsProtected) throw new InvalidOperationException("Protected application");
        if (application.ApplicationType == ApplicationType.Msix)
        {
            if (string.IsNullOrWhiteSpace(application.PackageFullName)) throw new InvalidOperationException("Missing package identity");
            var manager = new PackageManager();
            // Current-user removal only; no all-user or forced-removal options.
            var result = await manager.RemovePackageAsync(application.PackageFullName, RemovalOptions.None).AsTask(token).ConfigureAwait(false);
            if (result.ExtendedErrorCode is not null && result.ExtendedErrorCode.HResult < 0) throw result.ExtendedErrorCode;
            return 0;
        }
        var command = BuildDesktopCommand(application);
        // Hold a no-write/no-delete file handle across launch to prevent executable
        // contents being replaced between validation and CreateProcess/ShellExecute.
        var info = new ProcessStartInfo(command.Executable) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(command.Executable)! };
        foreach (var argument in command.Arguments) info.ArgumentList.Add(argument);
        token.ThrowIfCancellationRequested();
        Process? launched;
        using (var ancestors = Cleaner.PinnedCleanerPath.Open(info.WorkingDirectory, true))
        using (var image = new FileStream(command.Executable, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (application.ApplicationType == ApplicationType.Win32 && !UninstallCommandParser.IsSafeExecutable(command.Executable))
                throw new InvalidOperationException("Executable path changed");
            launched = Process.Start(info);
        }
        // Do not hold filesystem leases during the native uninstall itself:
        // uninstallers must be able to remove their installation directory.
        using var process = launched;
        if (process is null) return null;
        await process.WaitForExitAsync(token).ConfigureAwait(false);
        return process.ExitCode;
    }
    public static UninstallCommand BuildDesktopCommand(InstalledApplication application) => application.ApplicationType switch
    {
        ApplicationType.Msi when application.ProductCode is { } product && product != Guid.Empty =>
            new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe"), ["/x", product.ToString("B")]),
        ApplicationType.Win32 => UninstallCommandParser.Parse(application.RegisteredUninstallCommand) ?? throw new InvalidOperationException("Uninstall command unavailable"),
        _ => throw new InvalidOperationException("Desktop uninstall identity unavailable")
    };
}
