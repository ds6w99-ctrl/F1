using SystemCore.Core.Compatibility;
using SystemCore.Core.Dashboard;

// Dependency-free phase-0 test runner; a failure produces a nonzero exit code.
var cases = new (string Name, bool Actual, bool Expected)[]
{
    ("23H2 minimum accepted", CompatibilityRules.IsCandidate(new(10, 0, 22631), true, true), true),
    ("24H2 accepted", CompatibilityRules.IsCandidate(new(10, 0, 26100), true, true), true),
    ("25H2 accepted", CompatibilityRules.IsCandidate(new(10, 0, 26200), true, true), true),
    ("Below minimum rejected", CompatibilityRules.IsCandidate(new(10, 0, 22630), true, true), false),
    ("ARM64 or x86 excluded", CompatibilityRules.IsCandidate(new(10, 0, 26200), false, true), false),
    ("Server excluded", CompatibilityRules.IsCandidate(new(10, 0, 26100), true, false), false),
    ("Unknown OS version rejected", CompatibilityRules.IsCandidate(new(11, 0, 30000), true, true), false),
    ("Home accepted", CompatibilityRules.IsSupportedEdition("Core"), true),
    ("Pro accepted", CompatibilityRules.IsSupportedEdition("Professional"), true),
    ("Enterprise accepted", CompatibilityRules.IsSupportedEdition("Enterprise"), true),
    ("Education accepted", CompatibilityRules.IsSupportedEdition("Education"), true),
    ("N edition accepted", CompatibilityRules.IsSupportedEdition("ProfessionalN"), true),
    ("Unknown edition rejected", CompatibilityRules.IsSupportedEdition(null), false),
    ("Server edition rejected", CompatibilityRules.IsSupportedEdition("ServerStandard"), false),
    ("Unsupported Mica falls back", CompatibilityRules.CanUseMica(false, false, true), false),
    ("High contrast takes priority", CompatibilityRules.CanUseMica(true, true, true), false),
    ("Disabled effects fall back", CompatibilityRules.CanUseMica(true, false, false), false),
    ("Supported enabled Mica", CompatibilityRules.CanUseMica(true, false, true), true),
    ("CPU kernel includes idle", DashboardMath.CpuPercent(new(100, 150, 50), new(160, 230, 70)) == 40, true),
    ("CPU idle sample", DashboardMath.CpuPercent(new(0, 0, 0), new(100, 100, 0)) == 0, true),
    ("CPU busy sample", DashboardMath.CpuPercent(new(0, 0, 0), new(0, 50, 50)) == 100, true),
    ("CPU zero delta unavailable", DashboardMath.CpuPercent(new(1, 2, 3), new(1, 2, 3)) is null, true),
    ("CPU reset rejected", DashboardMath.CpuPercent(new(2, 3, 4), new(1, 4, 5)) is null, true),
    ("CPU invalid idle rejected", DashboardMath.CpuPercent(new(), new(200, 100, 0)) is null, true),
    ("Memory excludes available", DashboardMath.Memory(100, 30)?.UsedBytes == 70, true),
    ("Invalid memory unavailable", DashboardMath.Memory(100, 101) is null, true),
    ("Zero memory unavailable", DashboardMath.Memory(0, 0) is null, true),
    ("Volume preserves user quota", DashboardMath.Volume("C:\\", 100, 40, 20) is { UsedBytes: 60, AvailableBytes: 20 }, true),
    ("Invalid volume unavailable", DashboardMath.Volume("C:\\", 100, 40, 50) is null, true),
    ("Minimized pauses", DashboardMath.ShouldMonitor(true, true, true), false),
    ("Other page pauses", DashboardMath.ShouldMonitor(false, true, false), false),
    ("Hidden pauses", DashboardMath.ShouldMonitor(true, false, false), false),
    ("Visible dashboard samples", DashboardMath.ShouldMonitor(true, true, false), true),
    ("MSI identity independent of view", DashboardMath.DesktopIdentity("{12345678-1234-1234-1234-123456789012}", "App", "1", "") == DashboardMath.DesktopIdentity("12345678-1234-1234-1234-123456789012", "App", "2", ""), true)
};
foreach (var test in cases)
    Console.WriteLine($"{(test.Actual == test.Expected ? "PASS" : "FAIL")}: {test.Name}");
var settingsPassed = SettingsTests.Run();
var informationPassed = await SystemInformationTests.Run();
var performancePassed = await PerformanceTests.Run();
var cleanerPassed = CleanerSafetyTests.Run();
var orchestrationPassed = await OrchestrationTests.Run();
return cases.All(test => test.Actual == test.Expected) && settingsPassed && informationPassed && performancePassed && cleanerPassed && orchestrationPassed ? 0 : 1;
