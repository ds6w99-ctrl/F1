# Compatibility — revised scope, 2026-09-24

## Owner-approved platform scope

**Windows 11 only; x64 only. Minimum: 23H2 (build 22631). Recommended: 24H2 or later.**
Editions: **Home, Pro, Enterprise, Education**, including the listed N/Home language variants.
Windows 10 is explicitly unsupported. There is no legacy compatibility layer, deployment
target, workaround or test matrix for it. Server, x86, ARM64, IoT/LTSC and unknown editions
are outside the validated scope. Future releases require validation, not an automatic support claim.

Officially supported releases are the Windows 11 versions still supported for each edition
at the time SystemCore ships. The technical floor does not force testing an expired release.
Home/Enterprise/Education release coverage is **DEFERRED TO RELEASE VALIDATION**.
The only available host is Pro 25H2 26200.9457, not a clean machine.

## Technical minimum versus vendor servicing

The requested 23H2 technical floor does not restore Microsoft's servicing:

| Release | Home/Pro servicing | Enterprise/Education servicing | Product evidence |
|---|---|---|---|
| 23H2 / 22631 | Ended November 2025 | Ends November 2026 | NOT TESTED |
| 24H2 / 26100 | Ends October 2026 | Ends October 2027 | NOT TESTED |
| 25H2 / 26200 | Ends October 2027 | Ends October 2028 | Work-host checks only |

The current .NET 10 supported-OS list includes 23H2 Enterprise/Education, not 23H2 Home/Pro.
The build-22631 floor is a technical implementation detail, not a promise of official support
for expired Home/Pro 23H2. Keep .NET 10; do not downgrade or infer vendor support from a build.
26H1 is listed as stable, but also needs edition/architecture and release validation. Maintain
the release-support matrix as policy/evidence, not hard-coded lifecycle dates in architecture.

## Pinned toolchain

| Item | Pin / meaning |
|---|---|
| .NET SDK | 10.0.401, exact `global.json`, no roll-forward |
| Runtime in published apps | Microsoft.NETCore.App 10.0.12, verified in runtimeconfig |
| Windows App SDK Stable | 2.5.1 component family |
| WinUI / Runtime / InteractiveExperiences | 2.3.9 / 2.5.1 / 2.1.9 |
| SDK build tools | 10.0.26100.9169 (NuGet, build only) |
| Windows SDK .NET Ref | 10.0.26100.87 |
| App TFM | `net10.0-windows10.0.26100.0` |
| Minimum platform properties | `10.0.22631.0` for SupportedOSPlatformVersion and TargetPlatformMinVersion |
| Runtime / architecture | `win-x64`, x64 |
| Experimental installer compiler | Inno Setup 7.1.0 x64, official signed download, portable tooling |

**The `windows10.0` TFM prefix is the Windows API version identifier, including Windows 11;
it does not indicate Windows 10 support.** No fictitious SDK build 22631 package is used.
Runtime eligibility checks require client installation, allowed edition, build >=22631 and x64.
Installer checks the same floor/edition and `x64os`, rejecting emulated ARM64 installations.

## API and appearance detection

`FeatureCapabilities` and `CompatibilityRules` describe the Windows 11 feature-detection model,
not a legacy shim. The shell renders deterministic opaque surfaces and does not request Mica or
desktop glass, so appearance never depends on backdrop availability. A theme-aware solid
brush handles Windows 11 accessibility/settings; live theme updates and activation refresh
are supported. Default native title bar; no undocumented DWM modifications.

The two probe tabs and en/ar resource files are a **phase-0 test fixture** for navigation,
language, theme and focus checks. Production module navigation/MVVM/DI/settings remain phase 1.
Diagnostic fields report actual WinUI `ActualTheme` and `FlowDirection`; UI Automation can
check those properties but cannot prove visual contrast, shaping, clipping or spoken Narrator output.

## Deployment

Unpackaged app (`WindowsPackageType=None`); .NET and App SDK self-contained independently.
`EnableMsixTooling=true` generates the PRI; it does not change the distribution to MSIX.
Publish the entire folder, including `.pri`, native DLLs and `ar` resource satellite.
Avoid global RuntimeFrameworkVersion on the WinUI project: it previously caused an invalid
Windows SDK reference-pack request. Runtime packs are pinned by the exact SDK and verified.
No dependency on developer SDK presence has been excluded until clean-machine tests pass.
Private runtimes require product rebuild/redistribution for updates; all native/CRT/license
closure and runtime network behavior still require review.

## Evidence and sources

Actual results: `TEST-RESULTS.md`, `artifacts/phase0-validation/`.
Sources retrieved/reviewed 2026-09-24:

- https://learn.microsoft.com/en-us/lifecycle/products/windows-11-home-and-pro
- https://learn.microsoft.com/en-us/lifecycle/products/windows-11-enterprise-and-education
- https://raw.githubusercontent.com/dotnet/core/main/release-notes/10.0/supported-os.md
- https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
- https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json
- https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads
- https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps
- https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops
- https://api.nuget.org/v3-flatcontainer/microsoft.windowsappsdk/2.5.1/microsoft.windowsappsdk.nuspec
- https://jrsoftware.org/isdl.php and https://jrsoftware.org/isdl-verify.php
- https://jrsoftware.org/ishelp/topic_setup_architecturesallowed.htm

Phase 0 Development Gate is PASS under docs/GATES.md. Unavailable clean-machine and final
edition/version tests are **DEFERRED TO RELEASE VALIDATION**; Release Gate is PENDING.
Availability/capability APIs take precedence over build-number guesses for optional features.
