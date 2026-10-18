namespace SystemCore.Core.Licensing;

// Plan tiers. Free keeps every page reachable but with reduced capability; Pro unlocks the
// advanced operations. The gate is enforced in Core so no page can bypass it by omission.
public enum PlanTier { Free, Pro }

public enum Feature
{
    // Cleaner
    CleanerBasic,          // temp/cache areas the free plan allows
    CleanerAdvanced,       // Windows Update cache, Explorer traces, deep scan, bulk clean
    CleanerDeepScan,
    // Uninstaller
    UninstallerBasic,      // single removal
    UninstallerBulk,       // multi-select queue
    UninstallerQuiet,      // silent uninstaller
    UninstallerOrphans,    // orphaned Program Files folders
    UninstallerRestore,    // restore from backup archives
    // Memory
    MemoryReadOnly,        // metrics and analysis
    MemoryRelease,         // native memory release
    // Diagnostics / tools
    DiagnosticsExport,
    ToolsLauncher,
    SystemInformation,
    StartupManager,
    StartupMutation        // enable/disable startup items
}

// A single table decides every gate. Adding a feature here is the only place a plan is described.
public static class FeaturePlans
{
    public static bool IsAvailable(Feature feature, PlanTier tier) => tier == PlanTier.Pro || feature is
        Feature.CleanerBasic or Feature.UninstallerBasic or Feature.MemoryReadOnly
        or Feature.DiagnosticsExport or Feature.ToolsLauncher or Feature.SystemInformation
        or Feature.StartupManager;

    public static PlanTier RequiredTier(Feature feature) => IsAvailable(feature, PlanTier.Free) ? PlanTier.Free : PlanTier.Pro;

    // Human-facing plan name used by the UI badge ("Pro").
    public static string TierLabel(PlanTier tier) => tier == PlanTier.Pro ? "Pro" : "Free";
}
