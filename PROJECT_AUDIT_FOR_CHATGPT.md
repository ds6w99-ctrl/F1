# PROJECT AUDIT FOR CHATGPT — F1 (SystemCore) `0.9.0-dev`

> Generated: 2026-10-17 — source tree `G:\F1` — branch `pre-release-fixes@6cc6e60`
> Purpose: complete handoff for ChatGPT review. READ-ONLY audit; no code was changed to produce this file.
> Identity: F1 — Made By Falcone — `0.9.0-dev` — Windows 11 x64 only.

## 1. Product identity & version (frozen)

- `src/SystemCore.Core/ProductIdentity.cs:1-13`:
  `Name=F1`, `Author=Falcone`, `Attribution=Made By Falcone`, `Tagline=POWER. CONTROL. CLEAN.`,
  `AppExecutableName=F1.exe`, `Publisher="" // Unassigned`, `Stage=Phase 9 — development`.
- `Directory.Build.props:16`: `<Version>0.9.0-dev</Version>`, `IncludeSourceRevisionInInformationalVersion=false` (no git hash leaks into About/FileVersionInfo).
- `installer/F1.iss:14-15`: `ProductName=F1`, `ProductVersion=0.9.0-dev`.
- `README.md:1-3`: `# F1 — Made By Falcone`, `0.9.0-dev — ليست Production`, gate policy `docs/GATES.md`, Release Gate PENDING.
- Decision carried from previous session: version stays `0.9.0-dev`, no bump.

## 2. Repository & git state (observed 2026-10-17)

- Root `G:\F1` is a git repo. Current branch `pre-release-fixes`.
- `git rev-parse`: `main=c86a24c`, `pre-release-fixes=6cc6e60`.
- `main:c86a24c` message `chore: clean F1 baseline before next OpenCode session`.
- `pre-release-fixes:6cc6e60` message `feat: add product installer and portable distribution paths`.
- `git log --oneline --graph --all -15` top:
  `6cc6e60`, `823f294 fix(installer): put the elevated helper where the app actually looks`,
  `012caa9 fix(installer): repair references the F1 rename broke`,
  `40957ea ui: add one-click verification entry point`,
  `b3e9b41 test(uninstaller): prove observed leftovers really delete`,
  `e7bb10e docs: record every deletion and manual verification checklist`,
  `1baeb78 feat(uninstaller): prove leftover ownership by observing the uninstaller`,
  `7ab4c23 feat(uninstaller): capture application footprint before removal`, etc.
- Other local branches retained: `oce-backup/fix2-20260926@62a0af7`, `fix3@1e952f4`, `fix4-copy-polish@30e4b84`,
  `fix5@a19bcf7`, `fix6@a1adb63`, `fix7@5e21063`, `fix7-task2-wip@53a4d6e`.
- `git status --short --branch` on audit machine: `## pre-release-fixes` clean (no modifications).
- `git remote -v` on audit machine (2026-10-17): empty — prior `origin https://github.com/ds6w99-ctrl/F1.git` not present here and must be re-added before push.
- Target GitHub: `ds6w99-ctrl/F1`. Previous push blocked: private repo `Repository not found` + `Authentication failed` (cloud env `Administrator` ≠ user device `Falcone`, no PAT, no credential helper).
- `.git` size previously noted ~180 MB on cloud env.
- Pre-push requirement still open: reconcile `main` (currently reset to Initial/clean baseline) with real tree before pushing branches `pre-release-fixes`, `main` and tag `pre-phase10-baseline`.

## 3. Architecture — modular monolith (three layers + helper)

Per `ARCHITECTURE.md:130-148`:

- **App**: WinUI resources, navigation, MVVM view-models, DI composition root. No direct Win32 except via contracts.
- **Core**: models, policies, operation coordination, contracts. No WinUI dependency.
- **Infrastructure**: read-only Windows providers first, approved writes later (Registry/MSI/MSIX, PDH, WMI, Shell, Task Scheduler COM).
- **ElevatedHost**: short-lived, protected, typed privileged operations only. Main manifest `asInvoker`.
- Data flow (`ARCHITECTURE.md:164-170`):
  `read-only scan -> immutable versioned review plan -> explicit confirmation -> coordinator lease -> executor revalidates via policy -> per-step journal -> report`.
  Elevated path: `typed operation -> authenticated local broker -> independent host validation vs compiled allowlist + original user identity -> bounded execution -> structured result`.
  No UI-supplied exe/script/arbitrary path deletion/arbitrary registry writes.
- `OperationCoordinator` leases by resource keys (app identity, cleanup root, registry source); uninstall vs leftover for same app conflict; UI cancel stops future steps only, no false rollback.
- Modules: Dashboard, Cleaner, Uninstaller, SystemInfo, Performance, Memory, Startup, Tools, Settings.

## 4. Source layout & sizes

- `src/` contains 4 production projects:
  - `src/SystemCore.App`: 41 × `*.cs` (WinUI, MVVM, DI, navigation, pages).
  - `src/SystemCore.Core`: 31 × `*.cs` (contracts, math, policies, coordination).
  - `src/SystemCore.Infrastructure`: 72 × `*.cs` (providers, scanners, executors).
  - `src/SystemCore.ElevatedHost`: 1 × `*.cs` (`Program.cs` — typed helper endpoint).
- Total `src/**/*.cs` ≈ 145; with `tests/` total `**/*.cs` = 195 (measured via `Get-ChildItem -Recurse`).
- DI composition: `src/SystemCore.App/App.xaml.cs:81,83` registers
  `ICleanerTargetProvider -> WindowsCleanerTargets`, `ICleanerService -> WindowsCleanerService` directly (no V1 filter in production path).
- Solution: `SystemCore.sln` (4 prod + 12 test projects).

## 5. App layer (WinUI 3)

- Window `asInvoker` with MVVM + DI + nav + Dashboard (real reads: CPU/RAM/system-volume capacity/uptime/installed-count/startup-count).
- Pages: System Information (7 providers, fault-isolating aggregator, manual async refresh), Performance (CPU/RAM/disk/net/process, last-60s bounded charts),
  Cleaner (scan/review/manual confirm, Shell Recycle Bin, limited Windows Temp helper), Uninstaller (Win32/MSI/MSIX inventory, native removal, identity re-check, documented leftover scan + selective delete with Registry backups),
  Startup Manager (Run/RunOnce/Startup folders/Logon-Boot tasks, reversible disable/restore, per-item confirm), Memory Tools (RAM/Commit/cache metrics, 60s history, top consumers, pressure eval, explicit manual optimize decision),
  Tools (14 native Windows-tool cards, closed catalog, availability check on entry/Refresh, ID-only launch), Settings/About (`0.9.0-dev`, local prefs only, bounded JSON, atomic persist).
- i18n: `Resources/Shell.resx` + `Shell.ar.resx`, RTL/LTR, sidebar fixed left, Light/Dark/System, opaque surfaces, local fixed background (clean mountain asset unavailable, visual-match still Pending per `README.md:7-8`).
- Preferences: language/theme/restore data locally. Startup user data `%LOCALAPPDATA%\SystemCore\StartupState`; elevated-source state `%PROGRAMDATA%\SystemCore-StartupState-v1` with restricted write ACL.
- No background service, tray, or login task (`ARCHITECTURE.md:184`).

## 6. Core layer (contracts & policy)

- `Core/Cleaner`: scanner-owned bounded sessions, GUID-only selection, exact-handle revalidation/deletion, Shell Recycle Bin, typed Temp helper.
- `Core/Uninstaller`: installed identities, inventory/removal/leftover sessions, ID-only requests, per-item outcomes; ownership established before removal, never guessed after.
- `Core/Startup`: normalized source identities, status/protection, ID-only single-item requests; mutation re-resolves source then persists versioned DPAPI-protected receipt before changing anything; Registry preserves raw typed bytes via KTM; files move same-volume via exact dir handles; task defs revalidated before/after `Enabled`.
- `Core/Performance`: `IPerformanceMonitorService`, unified snapshots, scalar bounded history, delta trackers, sole sampling task. `DashboardMonitor` timer-free adapter, not second engine.
- `Core/SystemInformation`: immutable snapshots, provider contracts, fault-isolating aggregator, shared size/percentage formatting.
- `Core/MemoryTools`: metric math, sustained pressure/hysteresis, analysis, manual optimization decisions; optimizer serializes requests, verifies fresh metrics, accepts only trusted in-process cache-owner contracts. Prod registers zero cache owners (nothing proven discardable); valid no-op displayed explicitly.
- `Core/Tools`: closed tool catalog + typed availability/launch results.
- `Core/Diagnostics`: typed events, manual export/clear/folder; no perf sampling, no process inventory, no network.
- `Core/Compatibility`: `FeatureCapabilities.cs:12` `MinimumBuild=22631`; `IsSupportedEdition` client-only allowlist.
- `ProductIdentity.cs:10` `Publisher=""` (unassigned) — contrasts with installer `AppPublisher=Falcone` (see §8).

## 7. Infrastructure layer (Windows providers)

- `Infrastructure/Cleaner`: `WindowsCleanerTargets` (targets), `WindowsCleanerScanner` (scan), `WindowsCleanerService` (execute). Pinned ancestry/exact-handle FS validation; Shell official delete; Temp via typed helper.
- `Infrastructure/Uninstaller`: official Registry/MSI/MSIX inventory, native exec/completion, internal pre-uninstall evidence, bounded discovery, FS validation, transactional Registry deletion. File delete reuses Cleaner pinned-handle impl + empty-dir disposal. Registry mutation requires typed durable backup + matching transactional snapshot incl. last-write metadata. Current-token permission failures are per-item results.
- Two-layer leftover proof (`ARCHITECTURE.md:83-93`): (1) pre-uninstall receipt (install location, registry data path, app-data dirs under shared roots); (2) observe dirs beneath those roots immediately before/after native uninstaller — moved dir changes parent mtime, proving uninstaller touched it. Name-linked/observed = Review + explicit personal-data consent; only exact receipt fact can be Safe. Admin trees (Program Files, ProgramData) via elevated helper closed allow-list; helper never gets user-writable tree.
- `Infrastructure/Startup`: separate Registry/known-folder/Task Scheduler COM providers feed async inventory shared with Dashboard; privileged sources via separate 4-KiB typed helper endpoint (resolves IDs/fingerprints vs prod sources + ACL machine state; accepts no command/Registry address/task path/filenames). User-source ops stay in app.
- `Infrastructure/Performance`: native PDH disk I/O, .NET network/process counters, isolated collection; detailed collection only when Performance or Memory page visible; provider reads pause with no visible consumers; UI updates coalesced.
- `Infrastructure/SystemInformation`: 7 Windows providers + bounded WMI reader; session-cached static inventory; Refresh bypasses; AsyncRelayCommand prevents overlap; leave/minimize cancels caller; native WMI may finish later bounded by 3 workers + 5s enum timeout; per-provider 20s caller timeout; no WMI on UI thread/live timer.
- `Infrastructure/Tools`: checks documented URI support or pinned trusted system files; UI submits catalog ID only.
- `Infrastructure/Diagnostics`: 128-entry async queue, per-dir mutex, 10 bounded local logs, handle-verified cleanup, allowlist-reserialized ZIPs; app listener excludes EventSource payload strings.

## 8. ElevatedHost & installer identity

- `ElevatedHost/Program.cs`: short-lived typed helper; 4-KiB endpoint for Startup privileged sources; closed op allow-list for admin leftover trees.
- `installer/F1.iss:1-90` (distributable product installer; do not confuse with `SystemCore.Phase0.iss` test fixture):
  - `AppId={{81C8CF82-BB21-4324-A51C-22C7C9F14222}` (`F1.iss:19`), distinct per `F1.iss:3-7` + `DECISION.md`.
  - `AppPublisher=Falcone` (`:23`), `DefaultDirName={autopf}\F1` (`:25`, overridable), `ProductName=F1`, `ProductVersion=0.9.0-dev`.
  - `PrivilegesRequired=admin`, `ArchitecturesAllowed=x64os`, `MinVersion=10.0.22631` (mirrors Core `MinimumBuild`).
  - Layout: `App\* -> {app}\App` incl. `{app}\App\ElevatedHost` (`:9-13`, `:53`); docs pin in `CLEANER-PROTOCOL.md:33-42`; `SecurityTests`+`CompatibilityTests` pin relationship.
  - `SetupIconFile=src/SystemCore.App/Assets/Brand/F1.ico`, `OutputBaseFilename=F1-0.9.0-dev-win-x64-setup`, `lzma2`, `UninstallFilesDir={app}\Uninstall`, no post-install elevated launch (`asInvoker` runs as caller).
  - `[Code]` `IsAllowedEdition`: `InstallationType=Client` + `EditionID` in `Core/CoreN/CoreSingleLanguage/CoreCountrySpecific/Professional[N]/Enterprise[N]/Education[N]`; else `InitializeSetup` refuses with messagebox.
  - Ships `THIRD-PARTY-NOTICES` + `docs/licenses/*` into `{app}\Notices`.
- Note: `ProductIdentity.Publisher=""` vs installer `AppPublisher=Falcone` — code asserts no commercial publisher, installer declares display publisher. Not a bug, but ChatGPT should note the split.

## 9. Dependencies (locked, minimal)

- `global.json`: SDK `10.0.401`, `rollForward:disable`, `allowPrerelease:false` — exact SDK required.
- `NuGet.Config`: single source `nuget.org` after `<clear/>`.
- `Directory.Packages.props` (`ManagePackageVersionsCentrally=true`) — 7 direct versions:
  `System.Management 10.0.12`, `CommunityToolkit.Mvvm 8.4.2`, `Microsoft.Extensions.DependencyInjection 10.0.12`,
  `Microsoft.WindowsAppSDK.WinUI 2.3.9`, `Microsoft.WindowsAppSDK.Runtime 2.5.1`,
  `Microsoft.WindowsAppSDK.InteractiveExperiences 2.1.9`, `Microsoft.Windows.SDK.BuildTools 10.0.26100.9169`.
- `Directory.Build.props`: `Nullable+ImplicitUsings enable`, `LangVersion 14.0`, `EnableNETAnalyzers+AnalysisLevel latest+EnforceCodeStyleInBuild`,
  `TreatWarningsAsErrors` in Release only, `Deterministic true`, `RestorePackagesWithLockFile true`, `Version 0.9.0-dev`.
- Transitive/documented (`DEPENDENCIES.md`, `COMPATIBILITY.md`): `System.CodeDom 10.0.12`, `DI.Abstractions 10.0.12`,
  `Windows.SDK.NET.Ref 10.0.26100.87`, `WindowsAppSDK.Base 2.0.4`, `Foundation 2.3.12`, `BuildTools.MSIX 1.7.251221100`,
  `Web.WebView2 1.0.3719.77` (declared by WinUI, unused), runtime `NETCore.App 10.0.12`, TFM `net10.0-windows10.0.26100.0`, `win-x64`.
- Tooling (not NuGet): `Inno Setup 7.1.0 x64` for installer/portable builds.
- Prior summary phrase "5 external packages" was shorthand; exact direct count is 7. No GPL/AGPL/LGPL chosen; licenses under `docs/licenses/<pkg>/<ver>/` (12 files); SBOM `docs/sbom.resolved-nuget.cdx.json` + `sbom.tooling.cdx.json`; full CycloneDX/SPDX with hashes required before Release.

## 10. Build reproducibility (commands from README)

Requires .NET SDK `10.0.401` on Win11 x64, from repo root:

```powershell
dotnet restore SystemCore.sln --locked-mode
dotnet build SystemCore.sln -c Release --no-restore
dotnet run --project tests/SystemCore.UnitTests -c Release --no-build
dotnet publish src/SystemCore.App -c Release --no-restore -o artifacts/publish/App
dotnet publish src/SystemCore.ElevatedHost -c Release --no-restore -o artifacts/publish/App/ElevatedHost
dotnet run --project tests/SystemCore.CompatibilityTests -c Release --no-build -- .
dotnet run --project tests/SystemCore.SystemInformationTests -c Release --no-build
dotnet run --project tests/SystemCore.PerformanceTests -c Release --no-build
dotnet run --project tests/SystemCore.UninstallerTests -c Release --no-build -- --synthetic --leftover-delete
dotnet run --project tests/SystemCore.StartupTests -c Release --no-build
dotnet run --project tests/SystemCore.StartupTests -c Release --no-build -- --mutation
dotnet run --project tests/SystemCore.MemoryToolsTests -c Release --no-build -- --integration
dotnet run --project tests/SystemCore.ProductTests -c Release --no-build -- --integration
```

- Stop on any non-zero exit. Alt `scripts/Build.ps1 -Dotnet <path>` (no machine policy change). Runners are console runners; `dotnet test` is not a substitute.
- Native UI smoke: `./scripts/Invoke-F1ProductUx.ps1 -AppPath artifacts/publish/App/F1.exe -EvidencePath artifacts/f1-ui/local-validation/ui`.
- Installer/portable: `./scripts/Build-Installer.ps1 -Compiler <ISCC.exe 7.1.0>`; `./scripts/Build-Portable.ps1`.
- Dev artifact: `artifacts/current/F1/F1.exe` (`0.9.0-dev`, CURRENT DEVELOPMENT BUILD).

## 11. Security posture (no secrets, strict mutation)

- Secret scan (2026-10-17, `src/` + `scripts/`):
  Broad `api_key|password|secret|token|connection string|AccountKey` hits (~100) all false positives: `CancellationToken token`, `CreateLinkedTokenSource(token)`, `OpenProcessToken/GetTokenInformation`.
  Counter-scan zero hits: `DefaultEndpointsProtocol|Server=.*;Password=|AKIA[0-9A-Z]{16}|ghp_|gho_|xox[bap]-|-----BEGIN (RSA )?PRIVATE KEY-----` in `src/`; `AKIA|ghp_|password\s*=|secret\s*=|connection.?string` in `scripts/`.
  Only `token` in scripts is Win32 elevation probe (`scripts/Get-ProcessElevation.ps1:9,10,16-19`, `scripts/Test-UacProbe.ps1:23`). Conclusion: no API key/password/connection-string/AWS/GitHub/Slack/private-key in code.
- `SECURITY.md` (186 lines): assets = user files/Registry/backups/elevated token/signing keys; adversary = local unprivileged (incl. same-user); no defense vs compromised admin/kernel.
  Proposed prod IPC (strict, incomplete): local pipe ACL'd to origin SID+SYSTEM, PID/handle/token/publisher verify, typed ≤64KiB, one op per host, fail-closed. Current probe is not prod broker.
- Mutation policy: deny root/UNC/device/traversal/ADS/reparse/multi-hard-links; constrained handles + canonical path/volume/file-ID verify; mandatory backup before commit.
- Per-phase enforced bounds: Startup revalidates, no permanent delete without backup/transaction; Uninstaller/Leftover exact identity + pinned handles; Memory Tools no privilege broker; Diagnostics no PII/paths.
- Release security validation still PENDING; 2026-09-24 refs for pipe/UAC/DLL security.
- Diagnostics export is manual, redacted, no upload.

## 12. Compatibility (Windows 11 x64 only)

Per `COMPATIBILITY.md` (96 lines) + `README.md:10-12`:

- Supported: Windows 11 x64, floor build 22631 (23H2), editions Home/Pro/Enterprise/Education. 24H2+ recommended.
- Explicitly out: Windows 10, Server, x86, ARM64, IoT, LTSC.
- `windows10.0` in `TFM net10.0-windows10.0.26100.0` is Windows API identifier incl. 11, not Win10 support.
- Publish: unpackaged (`WindowsPackageType=None`), self-contained, PRI; no global `RuntimeFrameworkVersion` on WinUI project.
- Only host so far: Pro 25H2 26200.9457 (not clean machine) — Home/Enterprise + clean-machine coverage deferred to Release Validation (`docs/RELEASE-QA.md`).
- Toolchain pinned: SDK 10.0.401 exact, runtime 10.0.12, AppSDK 2.3.9/2.5.1/2.1.9, BuildTools 10.0.26100.9169.

## 13. Tests (12 runners)

Under `tests/` (12 `.csproj`):

`SystemCore.CleanerTests`, `CompatibilityTests`, `F1ParentTests`, `MemoryToolsTests`, `PerformanceTests`, `ProductTests`,
`ReliabilityTests`, `SecurityTests`, `StartupTests`, `SystemInformationTests`, `UninstallerTests`, `UnitTests`.

- All are console runners (see §10). `dotnet test` not a substitute.
- Notable suites: `UninstallerTests --synthetic --leftover-delete` (proves observed leftovers delete), `StartupTests --mutation`, `MemoryToolsTests --integration`, `ProductTests --integration`, `CompatibilityTests -- .`, UI smoke `Invoke-F1ProductUx.ps1` (installed build 118/118 on work machine — not proof of clean-machine install/uninstall).
- Test installer `installer/SystemCore.Phase0.iss` is for retrievable-VM smoke only (`docs/SMOKE-TESTS.md`).

## 14. Documentation map

- Root: `README.md`, `ARCHITECTURE.md`, `COMPATIBILITY.md`, `DEPENDENCIES.md`, `SECURITY.md`, `ROADMAP.md`, `THIRD-PARTY-NOTICES`, `NuGet.Config`, `global.json`, `Run-UI-Verification.cmd`.
- `docs/` (18 entries): `CLEANER-PROTOCOL.md`, `DELETIONS.md`, `dependencies.resolved.json`, `F1-ASSETS.md`, `F1-DESIGN-TOKENS.md`, `F1-REBRAND-COMPATIBILITY.md`, `GATES.md`, `INSTALL.md`, `MANUAL-VERIFICATION.md`, `RELEASE-NOTES-0.9.0-dev.md`, `RELEASE-QA.md`, `sbom.resolved-nuget.cdx.json`, `sbom.tooling.cdx.json`, `SIGNING.md`, `SMOKE-TESTS.md`, `STARTUP-PROTOCOL.md`, `design/reference/f1-approved-reference.png`, `licenses/`.
- Key gates: `docs/GATES.md` (Release Gate PENDING), `docs/SIGNING.md` (signing deferred to Release/Production), `docs/INSTALL.md` (product installer + portable guide), `docs/F1-ASSETS.md` (asset log).

## 15. P1 — Browser-cache contradiction (load-bearing, documented)

- Code ENABLES browser caches by default:
  `src/SystemCore.Infrastructure/Cleaner/WindowsCleanerTargets.cs:11` `CacheAge=MinimumAgeFloor=1h`;
  `:64-66` `AddChromium(EdgeCache)`, `AddChromium(ChromeCache)`, `AddDirectoryLeaves(FirefoxCache)`;
  `:73-82` `AddChromium`: if `Directory.Exists(userData)` adds each `ChromiumCacheLeaves:13-17` (`Cache, Code Cache, GPUCache…`) with `DefaultSelected=true`;
  `:84-92` same for `FirefoxCacheLeaves:18` (`cache2, startupCache, shader-cache`);
  `:41-43` `IsAllowed` explicitly allows `EdgeCache/ChromeCache/FirefoxCache`;
  `WindowsCleanerScanner.cs:27,65-69` + `WindowsCleanerService.cs:155` do not exclude them (`Unavailable` only if no browser folder → `accepted.Length==0`).
- Strings/docs DISABLE them:
  `src/SystemCore.App/Resources/Shell.resx:223` `CleanEdgeCache=…unsupported in V1`, `:224` Chrome, `:225` Firefox, `:227` User WER, `:232` `CleanSafety=…Browser caches and WER are unsupported in V1` (+ `Shell.ar.resx:223-225` Arabic `غير مدعومة في V1`);
  `docs/CLEANER-PROTOCOL.md:116-117` `Browser caches and user WER are UNSUPPORTED IN V1: disabled rows…`;
  `docs/MANUAL-VERIFICATION.md:314-315,351` requires them ineligible.
- Impact: UI/docs promise disabled rows, production DI uses `WindowsCleanerTargets` unfiltered, so real scans select and delete browser caches. ChatGPT must decide: either flip code to disabled to match contract, or update strings/docs/protocol to Supported and re-verify safety. Do not ship with contradiction.

## 16. P2 — Games receive no removal receipt (intentional, must stay explicit)

- `src/SystemCore.Infrastructure/Uninstaller/ApplicationRemovalEvidence.cs:21-30`:
  ```cs
  if (app.ItemKind == ItemKind.Game) { lock(sync) pending.Remove(app.Id); return; }
  // FIX 5 never issues a cleanup receipt for a discovered game.
  // Game save/shared-launcher ownership is not established by discovery.
  ```
  Any `InstalledApplication` with `ItemKind.Game` is dropped from `pending` with no `Directories/Registry/Receipt`.
- Rationale: game saves + shared launchers (Steam/EA/etc.) ownership cannot be proven by discovery; issuing a receipt would risk deleting saves.
- This is an architectural decision, not a bug, but UX/docs must state it plainly so users do not expect game-leftover cleanup. ChatGPT: keep exclusion, ensure UI copy + `MANUAL-VERIFICATION` reflect it.

## 17. Gaps 5+7 + backup (prior priority, awaiting explicit user choice)

- Prior session priority: gaps 5+7 then remote backup — gated on explicit user decision.
- Version frozen at `0.9.0-dev`; no code changes made in this audit.
- Remote backup still blocked (see §2). No push attempted from this read-only session.

## 18. Technical debt & hygiene (zero)

- `TreatWarningsAsErrors` in Release, `EnforceCodeStyleInBuild`, `AnalysisLevel latest`, `Deterministic`, lockfiles (`RestorePackagesWithLockFile`).
- No TODO/FIXME debt claimed in prior audit; no network/telemetry code in app; no background workers beyond bounded sampling; no secrets (see §11).
- Signing/commercial readiness explicitly deferred, not claimed (`docs/SIGNING.md`, `README.md:84`).

## 19. What was done in prior cloud session (for continuity)

- `PROJECT_AUDIT_FOR_CHATGPT.md` created (650 lines/12,598 bytes) + copied to Desktop (previous env).
- Comprehensive report (zero debt, no secrets, minimal externals, 3-layer arch).
- P1 + P2 documented with file:line evidence.
- `origin https://github.com/ds6w99-ctrl/F1.git` added at that time (now absent on this machine — see §2).

## 20. Environment mismatch warning

- Audit env is cloud server `Administrator`; user device is `Falcone`. Paths, SDK presence, and `.git` size (~180 MB noted) differ.
- Do not infer user-device state from cloud paths. Re-run `dotnet --info` (expect 10.0.401), `git status`, `git remote -v` on `Falcone` before push.

## 21. Risks before Release

1. P1 contradiction must be resolved (code vs strings/docs).
2. Clean-machine install/uninstall never proven (only work-machine 118/118 UI vs installed build).
3. Home/Enterprise + clean-VM matrix pending.
4. Full SBOM closure (native/SDK/CRT) + signing + publisher identity pending.
5. `main` vs `pre-release-fixes` reconciliation + tag `pre-phase10-baseline` pending push.

## 22. Files ChatGPT should open first

1. `G:\F1\PROJECT_AUDIT_FOR_CHATGPT.md` (this file)
2. `src/SystemCore.Infrastructure/Cleaner/WindowsCleanerTargets.cs:11-92`
3. `src/SystemCore.App/Resources/Shell.resx:223-232` + `Shell.ar.resx:223-225`
4. `src/SystemCore.Infrastructure/Uninstaller/ApplicationRemovalEvidence.cs:21-30`
5. `installer/F1.iss:1-90`
6. `ARCHITECTURE.md:68-100`
7. `SECURITY.md`, `COMPATIBILITY.md`, `DEPENDENCIES.md`
8. `docs/CLEANER-PROTOCOL.md:33-42,116-117`, `docs/MANUAL-VERIFICATION.md:314-315,351`, `docs/GATES.md`, `docs/INSTALL.md`

---

## HANDOFF TO CHATGPT

> يا ChatGPT: هذا المشروع F1 `0.9.0-dev` — نموذج تحقق WinUI3/C#/.NET10، ليس إنتاجًا. بوابة الإصدار PENDING.
> التناقض P1 حقيقي (كاش المتصفحات مفعّل كودًا ومعطّل نصًا) — أصلحه باختيار واحد: إما تعطيل الكود ليوافق العقد، أو توثيق الدعم وإثبات الأمان. لا تُبقِ التناقض.
> الاستثناء P2 مقصود (الألعاب بلا إيصال) — أبقِه ووضّحه في الواجهة والتوثيق.
> تحقق من: عدم وجود أسرار (مؤكد صفر)، 7 حزم مباشرة مقفلة، معمارية 4 مشاريع، 12 عداء اختبار، مثبت `F1.iss` بهوية مستقلة.
> قبل الرفع: واءم `main@c86a24c` مع `pre-release-fixes@6cc6e60` ثم ادفع الفروع والوسم `pre-phase10-baseline` إلى `ds6w99-ctrl/F1` من جهاز Falcone (بيئة التدقيق بلا صلاحيات دفع).
> ابدأ من §22 ثم P1 (§15) ثم P2 (§16) ثم المخاطر (§21).
