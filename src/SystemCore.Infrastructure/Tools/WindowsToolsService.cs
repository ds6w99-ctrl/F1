using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SystemCore.Core.Tools;
using SystemCore.Infrastructure.Cleaner;

namespace SystemCore.Infrastructure.Tools;

public interface IWindowsToolPlatform
{
    string SystemDirectory { get; }
    Task<bool> AvailableAsync(WindowsToolItem tool, CancellationToken token);
    Task<ToolLaunchResult> LaunchAsync(WindowsToolItem tool, CancellationToken token);
}
public sealed class WindowsToolsService(IWindowsToolPlatform platform) : IWindowsToolsService
{
    public async Task<IReadOnlyList<WindowsToolItem>> ReadAsync(CancellationToken token)
    {
        var catalog = WindowsToolCatalog.Create(platform.SystemDirectory);
        return await Task.WhenAll(catalog.Select(async tool => tool with { Availability = await Available(tool, token) ? ToolAvailability.Available : ToolAvailability.Unavailable })).ConfigureAwait(false);
    }
    public async Task<ToolLaunchResult> OpenAsync(WindowsToolId id, CancellationToken token)
    {
        // Resolve only a closed ID against a fresh trusted catalog. Never accept a UI target/argument.
        var tool = WindowsToolCatalog.Create(platform.SystemDirectory).SingleOrDefault(t => t.Id == id);
        if (tool is null || !await Available(tool, token).ConfigureAwait(false)) return ToolLaunchResult.Unavailable;
        try { return await platform.LaunchAsync(tool, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return ToolLaunchResult.Cancelled; }
        catch (UnauthorizedAccessException) { return ToolLaunchResult.AccessDenied; }
        catch (Win32Exception e) { return e.NativeErrorCode switch { 5 => ToolLaunchResult.AccessDenied, 1223 => ToolLaunchResult.Cancelled, 2 or 3 => ToolLaunchResult.Unavailable, _ => ToolLaunchResult.Failed }; }
        catch (Exception e) when (e is IOException or COMException or InvalidOperationException) { return ToolLaunchResult.Failed; }
    }
    private async Task<bool> Available(WindowsToolItem tool, CancellationToken token)
    {
        try { return await platform.AvailableAsync(tool, token).ConfigureAwait(false); }
        catch (Exception e) when (e is IOException or Win32Exception or UnauthorizedAccessException or COMException or TimeoutException) { return false; }
    }
}
public sealed class WindowsToolPlatform : IWindowsToolPlatform
{
    public string SystemDirectory => Environment.SystemDirectory;
    public async Task<bool> AvailableAsync(WindowsToolItem tool, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (tool.LaunchType == ToolLaunchType.WindowsUri)
            return await Windows.System.Launcher.QueryUriSupportAsync(new Uri(tool.Target), Windows.System.LaunchQuerySupportType.Uri).AsTask(token).WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false) == Windows.System.LaunchQuerySupportStatus.Available;
        return await Task.Run(() =>
        {
            using var executable = PinnedCleanerPath.Open(tool.Target, false, shareRead: true);
            foreach (var path in tool.Arguments) { using var argument = PinnedCleanerPath.Open(path, false, shareRead: true); }
            return true;
        }, token).ConfigureAwait(false);
    }
    public async Task<ToolLaunchResult> LaunchAsync(WindowsToolItem tool, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (tool.LaunchType == ToolLaunchType.WindowsUri)
            return await Windows.System.Launcher.LaunchUriAsync(new Uri(tool.Target)).AsTask(token).ConfigureAwait(false) ? ToolLaunchResult.Launched : ToolLaunchResult.Failed;
        return await Task.Run(() =>
        {
            using var executable = PinnedCleanerPath.Open(tool.Target, false, shareRead: true);
            var arguments = new List<PinnedCleanerPath>();
            try
            {
                foreach (var path in tool.Arguments) arguments.Add(PinnedCleanerPath.Open(path, false, shareRead: true));
                var start = new ProcessStartInfo(tool.Target) { UseShellExecute = true, WorkingDirectory = SystemDirectory };
                foreach (var argument in tool.Arguments) start.ArgumentList.Add(argument);
                // Windows tools own their elevation policy; SystemCore never elevates itself.
                using var process = Process.Start(start);
                return process is null ? ToolLaunchResult.Failed : ToolLaunchResult.Launched;
            }
            finally { foreach (var argument in arguments) argument.Dispose(); }
        }, token).ConfigureAwait(false);
    }
}
