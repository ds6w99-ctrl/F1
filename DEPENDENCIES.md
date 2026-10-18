# Dependency inventory — phase 3

Phase 3 adds **no NuGet packages**: native pdh.dll, existing .NET Process/NetworkInterface APIs
and WinUI Path geometry supply the required functionality. The solution now has eight locked
projects including the performance integration runner. Historical SBOM scopes remain unchanged;
final payload/SBOM review remains Release QA.

Phase-2 additions: **System.Management 10.0.12** (Microsoft, MIT) for WMI firmware/CPU/RAM/
GPU/storage metadata unavailable in base .NET APIs, and its **System.CodeDom 10.0.12**
transitive dependency (Microsoft, MIT). SystemCore does not generate or execute code using
CodeDom. Hashes/source/license metadata: `docs/licenses/`. Seven projects now have lock files; locked restore passed. Earlier SBOMs
are historical scopes, not a complete phase-2 distribution SBOM. Final closure remains Release QA.

Phase-1 additions: CommunityToolkit.Mvvm **8.4.2**, Microsoft.Extensions.DependencyInjection
**10.0.12**, and its Abstractions **10.0.12** dependency, all declaring MIT. Restored nuspec/license files are retained under `docs/licenses/`. Six project lock files are now present.
The older resolved inventory and SBOM below describe phase 0, not the complete phase-1 payload.

This is a phase-0 inventory, not a completed distribution audit. Restore and publish
succeeded; all five projects have generated lock files and locked restore passed.
`docs/dependencies.resolved.json` records all **13 resolved NuGet packages** with source,
version, license metadata and content hash. `docs/sbom.resolved-nuget.cdx.json` contains them
as CycloneDX components (SHA-512 of the restored .nupkg files, regenerated for the locked
`1.0.0` restore). `docs/sbom.native-1.0.0.cdx.json` records the self-contained native payload
for `1.0.0`: product binaries, the .NET 10.0.12 win-x64 runtime, WinUI/AppSDK runtime files and
the WebView2 loader, with SHA-256 hashes of the shipped files; the full per-file inventory
(665 files) is produced at build time in `artifacts/publish-inventory.json`. SDK/runtime packs
and embedded native components still require a final redistribution-terms review on the signed
payload; that row remains open under Release QA.

| Component/version | Purpose | Source | License / required review |
|---|---|---|---|
| .NET SDK 10.0.401 | Build tooling, not app payload | builds.dotnet.microsoft.com release metadata | .NET MIT and bundled third-party notices; preserve relevant notices |
| .NET runtime 10.0.12 | Private managed runtime | Microsoft SDK runtime packs | MIT + runtime THIRD-PARTY-NOTICES; inventory native redistributables |
| Microsoft.WindowsAppSDK.WinUI 2.3.9 | Native WinUI UI | nuget.org / Microsoft | Package license.txt; do not equate GitHub code license to binary redistribution terms |
| Microsoft.WindowsAppSDK.Runtime 2.5.1 | Self-contained native framework | nuget.org / Microsoft | Package license.txt + embedded component terms; review payload |
| Microsoft.WindowsAppSDK.InteractiveExperiences 2.1.9 | Stable aligned composition/window support | nuget.org / Microsoft | Package license.txt |
| Microsoft.Windows.SDK.BuildTools 10.0.26100.9169 | Build tools only | nuget.org / Microsoft | https://aka.ms/WinSDKLicenseURL; no general permission to redistribute SDK tools inferred |
| Microsoft.Windows.SDK.NET.Ref 10.0.26100.87 | Compile-time Windows API projection | nuget.org / Microsoft | Inspect package license and CsWinRT/native runtime closure |
| Microsoft.WindowsAppSDK.Base 2.0.4 | Resolved transitive infrastructure | WinUI/Runtime nuspec | Microsoft package license |
| Microsoft.WindowsAppSDK.Foundation 2.3.12 | Resolved transitive foundation | WinUI nuspec | Microsoft package license |
| Microsoft.Windows.SDK.BuildTools.MSIX 1.7.251221100 | Resolved transitive PRI/MSIX tooling | Base nuspec | SDK license; build-only, review redistributable list |
| Microsoft.Web.WebView2 1.0.3719.77 | WinUI-declared transitive reference | WinUI nuspec | Microsoft binary license; no WebView control used or runtime installer bundled |
| Inno Setup 7.1.0 x64 | Phase-0 test installer compiler, portable developer tool | Official jrsoftware/issrc is-7_1_0 release | Custom permissive license; official commercial purchase request also requires production review; preserve notices/origin/modified-version markings |

Exact package license/notice files retrieved during restore are preserved under
`docs/licenses/<package>/<version>/` (12 files). App SDK binary license allows NuGet-binplaced
files to be redistributed, but requires significant primary app functionality, protective
end-user/distributor terms and other conditions including indemnity; retain notices and avoid
implied Microsoft endorsement. This probe is for development/testing, not public redistribution.
License sections also discuss possible data collection/updates: runtime network observation
and dependency behavior review are still required for the product's offline guarantee.

No redistributed fonts, icons or logos are added; system fonts are used. MVVM/DI are
implemented phase-1 dependencies. No GPL/AGPL/LGPL component has been
deliberately selected; embedded third-party notices have not yet received a full license review.

## Required before release

Generate full dependency graph from locks/project.assets.json and the actual native payload;
record package name/version/purpose/source/license/redistribution obligations, retain license
texts, review mixed licenses, and produce a complete CycloneDX/SPDX SBOM with hashes and
relationships. Include .NET native components, App SDK framework, CRT if distributed,
installer and any artwork. An empty or declared-only SBOM is not a release SBOM.
The resolved NuGet SBOM is separate and explicitly does not claim to cover all embedded native components.
`docs/sbom.tooling.cdx.json` records the exact experimental installer compiler archive,
source and SHA256. Its exact license is retained under `docs/licenses/InnoSetup/7.1.0`.
