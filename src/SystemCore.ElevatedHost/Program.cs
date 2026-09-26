using System.Security.Principal;

// No-argument token probe and separately typed, authenticated endpoints.
if (args.Length != 0 && args[0] == "--startup")
    return await SystemCore.Infrastructure.Startup.StartupHelperHost.RunAsync(args);
if (args.Length != 0)
    return await SystemCore.Infrastructure.Cleaner.CleanerHelperHost.RunAsync(args);

using var identity = WindowsIdentity.GetCurrent();
var principal = new WindowsPrincipal(identity);
return principal.IsInRole(WindowsBuiltInRole.Administrator) ? 0 : 5;
