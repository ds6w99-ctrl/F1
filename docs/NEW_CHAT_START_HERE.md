# F1 — New Chat Handoff

## Product and status
- F1 · **Made By Falcone** · Windows 11 x64 only, not Windows 10.
- C# / .NET **10.0.401 SDK**, WinUI 3 / Windows App SDK; MVVM/DI. Gaming/Premium/Neon design, Arabic and English, left navigation in both languages; RTL content and LTR technical values.
- Version **0.9.0-dev**. CURRENT DEVELOPMENT BUILD, not production.
- Phases 0–9: recorded Development PASS. Phase 9 owner review and Release/Production remain PENDING. Phase 10 has NOT started.
- Cleanup changed no application behavior. Existing partial pre-release edits were preserved, not completed by cleanup.

## Read next
1. `CURRENT-STATUS.md` — verified results and unresolved work.
2. `CURRENT-FILE-MAP.md` — source/evidence locations and commands.
3. `PRE-RELEASE-REMAINING-SPECIFICATION.md` — full pending user requirements; distinguish requests from implemented features.
4. `PROJECT-CLEANUP-REPORT.md` — cleanup evidence and archive lookup.

## Current features
- Dashboard: real CPU/RAM/storage/activity and shared monitoring summaries.
- System Information: native hardware/software data.
- Performance/Memory: live measurements, bounded histories, shared monitor. Production Memory optimization is an honest no-op: no proven releasable cache owners.
- Cleaner: safety-first scan, confirmed clean, session/identity/reparse protections. **FIX 3 complete: 78.831 s fresh before / 37.947 s after; live-dataset drift disclosed.** Two bounded category workers; native safety unchanged. Live category/count/bytes progress coalesced at 150 ms, EN/AR. Not centrally auto-scanned (FIX 7 pending).
- Uninstaller: unified native-removable applications plus explicitly read-only launcher-managed games; All/Applications/Games filter, source labels, 20 items/page. Managed rows never authorize uninstall or leftovers. Phase 5 native gate is unchanged.
- FIX 6: native applications automatically scan leftovers after verified removal. Same cancellable read-only scanner, manual retry retained, compact EN/AR results and Keep; no automatic deletion. Personal/uncertain candidates are unselectable Review, all defaults off, Safe-only explicit confirmation remains. Games still receive no receipt and cannot scan leftovers.
- Startup: reversible mechanisms/helper retained; **preliminary manageable-list filter added, incomplete full capability verification; real icons still pending**.
- Tools, Settings, About: existing native functions, System/Light/Dark, localization. Official F1 logo and attribution retained.
- Sidebar F1 Premium card removed; small About footer access retained.

## Game sources: actual support
| Source | Current status |
|---|---|
| Steam / Epic | Bounded launcher-metadata discovery implemented; device probe 10 / 1, one native duplicate merged. Native uninstall only from existing native registration; otherwise explicitly managed, no invented URI. |
| EA / Ubisoft / Battle.net | Separate provider seams; metadata formats unsupported/unverified, never fabricated. Registered applications remain in native inventory. |
| Xbox / Microsoft Store | Existing removable MSIX application inventory; dedicated game classification NOT implemented. |

No separate Games page is allowed. Discovery is inside the existing Uninstaller unified list; trustworthy metadata only, no whole-disk guessing or undocumented destructive commands. FIX 5 remains PARTIAL: see Game Discovery in `PRE-RELEASE-FIXES-01.md` for exact evidence and gaps.

## Product rules and requested UX
- Local-only; no telemetry/cloud, fake optimization, automatic destructive actions or tests against real user data.
- Native uninstall first; fresh identity verification and explicit confirmation for destructive actions.
- **Requested, NOT yet implemented:** verified game-removal/leftover ownership, app-wide startup orchestration/cache/background refresh, Cleaner auto scan/rescan, broad minimal-copy pass, remaining game adapters/classification. Native-application auto leftover scan is implemented (FIX 6); deletion remains confirmed.
- Therefore current Refresh/Scan/Analyze requirements still exist on some pages. Do not claim the requested no-mandatory-refresh experience is complete.
- Preserve KB/MB/GB/TB display labels, Arabic/English, safe errors/confirmations/accessibility text, logo, current version, namespaces, helper identities, `%LOCALAPPDATA%\SystemCore`, schemas and recovery paths.

## Current build and tests
- FIX 6 final candidate: locked restore/build/publish exit 0, Debug/Release 0 warnings/errors; Uninstaller default 198/full 362, regression 293 engine + 62 UI, Security 56, Unit 198, Startup 80, Cleaner default/executor/completion/performance/progress 18/22/49/43/24, game UI 26, legacy Uninstaller UI 57, ten-route smoke 34, all exit 0. The synthetic lifecycle is in `Test-UninstallerPage.ps1`, updated after evidence demonstrated the old manual-flow/DataPath-selectability assumptions were obsolete; no deletion gate was weakened. Full report: `artifacts/current/fix6-verify/task-6-report.md`. Games and exact real-application scan timing remain unverified; observed synthetic uninstall auto-scan lower bound 365.7234 ms. Only the English/light leftover screenshot was visually inspected.
- FIX 5 partial candidate: final build 0 warnings/errors; 208 Uninstaller engine assertions PASS; Steam/Epic read-only device counts 10/1, one native duplicate merged. `Test-F1UninstallerUi.ps1` was repaired contract-aware, not weakened: it targets a row that actually holds native removal rights and now also asserts a managed game exposes no uninstall, no leftover scan and a separate launcher control (57/57 PASS). Do not grant a managed game destructive rights to satisfy any test. Full counts/gaps: Game Discovery in `PRE-RELEASE-FIXES-01.md`.
- Latest FIX 3 follow-up: exact candidate build command PASS, Debug/Release 0 warnings/errors; 580 console assertions, 26 native synthetic lifecycle checks and 25 read-only EN/AR progress checks PASS. Memory-engine/isolation modes completed 100 cycles each. Historical read-only scan exit 2 and capture exit 1 are expected fail-closed safety, not defects: depth-limited User Temp stays incomplete and Clean stays disabled for that selection. Same signature exists in phase4a/4b evidence. No screenshot visual review or clean-VM/UAC certification claimed.
- Run from repository root: `& ".\artifacts\current\F1\F1.exe"`; retain entire F1 folder including ElevatedHost.
- Cleanup locked restore, Debug, Release, x64 App/helper publication: PASS, zero build warnings/errors.
- Cleanup native runtime smoke: **34 PASS** (launch, all ten routes, attribution, absent Premium card, normal close). Unit smoke passed; exact totals in current state/report.
- Evidence: `artifacts/project-cleanup/`. Direct native run used after desktop-token launcher failed; no Standard User/UAC certification is implied.
- Earlier Uninstaller delivery: 150 engine checks and 110 UI checks, 12 fully visible rows at 1360×1000/96 DPI; 135 raw/108 hidden/27 visible on that historical inventory. Not a new count from cleanup.
- Earlier F1 rebrand: 1,739 assertions + 1,000 malformed inputs; historical screenshot/performance evidence archived, not rerun by cleanup.

## Remaining work / next step
Resume unfinished pre-release requirements only when instructed. Cleaner FIX 3 is implemented and verified, including throttled live progress; see `PRE-RELEASE-FIXES-01.md` (Cleaner Performance). Historical baseline **53.51s / 75,910 User Temp entries** is not the fresh comparison denominator. Fresh overall **78.831s before / 37.947s after**; dataset changed 91,724 to 98,348 visited entries. Pre-existing depth-limited User Temp remains incomplete, and Clean disabled by design for that selection. This is not an outstanding FIX 3 defect. FIX 5 remains partial; see Game Discovery for evidence and unsupported formats. FIX 6 native-application flow is implemented, with the game receipt gap retained; see Automatic Leftover Flow. FIX 4/7 and Phase 10 were not started by this work.

Full photographic-reference acceptance still pending (clean reference background missing; authorized temporary SVG used). Higher DPI/full Narrator/high contrast, clean-machine/edition matrices, alternate-credential UAC and broader Release QA remain deferred.

**Do not redo completed phases. Do not start Phase 10 without explicit user approval.** Phase 10 means installer finalization, signing, Release QA and Production build; none is authorized by cleanup. Git branch: `pre-release-fixes`; inspect current status before editing.
