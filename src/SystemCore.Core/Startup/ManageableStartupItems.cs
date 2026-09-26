namespace SystemCore.Core.Startup;

// UI eligibility never authorizes a mutation: the service revalidates identity.
public static class ManageableStartupItems
{
    public static bool IsVisible(StartupItem item) => item.CanManage && HasSupportedState(item);

    public static bool HasSupportedState(StartupItem item) => !item.IsProtected
        && item.ProtectionReason == StartupProtection.None
        && !string.IsNullOrWhiteSpace(item.Identity)
        && !string.IsNullOrWhiteSpace(item.Fingerprint)
        && !string.IsNullOrWhiteSpace(item.ExecutablePath)
        && Enum.IsDefined(item.SourceType)
        && item.Scope is StartupScope.CurrentUser or StartupScope.AllUsers
        && (item.Status is StartupStatus.Enabled or StartupStatus.DisabledBySystemCore
            || item.Status == StartupStatus.DisabledByWindowsOrSource
                && item.SourceType is StartupSourceType.ScheduledTaskLogon or StartupSourceType.ScheduledTaskBoot);
}
