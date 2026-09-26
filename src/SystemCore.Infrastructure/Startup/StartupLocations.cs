using Microsoft.Win32;
using SystemCore.Core.Startup;

namespace SystemCore.Infrastructure.Startup;

internal sealed record StartupRegistryLocation(RegistryHive Hive, RegistryView View, string KeyPath, StartupSourceType Type);
internal sealed record StartupFolderLocation(string Path, StartupScope Scope);
internal interface IStartupLocations
{
    IReadOnlyList<StartupRegistryLocation> Registry { get; }
    IReadOnlyList<StartupFolderLocation> Folders { get; }
}
internal sealed class WindowsStartupLocations : IStartupLocations
{
    public IReadOnlyList<StartupRegistryLocation> Registry => (from hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine }
        from view in new[] { RegistryView.Registry64, RegistryView.Registry32 }
        from type in new[] { StartupSourceType.RegistryRun, StartupSourceType.RegistryRunOnce }
        select new StartupRegistryLocation(hive, view, @"Software\Microsoft\Windows\CurrentVersion\" + (type == StartupSourceType.RegistryRun ? "Run" : "RunOnce"), type)).ToArray();
    public IReadOnlyList<StartupFolderLocation> Folders => [new(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupScope.CurrentUser),
        new(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupScope.AllUsers)];
}
