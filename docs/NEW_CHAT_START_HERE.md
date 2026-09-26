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
- Cleaner: safety-first scan, confirmed clean, session/identity/reparse protections. **Not yet optimized or centrally auto-scanned.**
- Uninstaller: removable-only main list; real Win32/MSI/MSIX icons and fallback; virtualized 52px rows, 20 items/page; native removal verification and safe leftover review.
- Startup: reversible mechanisms/helper retained; **preliminary manageable-list filter added, incomplete full capability verification; real icons still pending**.
- Tools, Settings, About: existing native functions, System/Light/Dark, localization. Official F1 logo and attribution retained.
- Sidebar F1 Premium card removed; small About footer access retained.

## Game sources: actual support
| Source | Current status |
|---|---|
| Steam / Epic / EA / Ubisoft / Battle.net | Dedicated discovery providers NOT implemented. Existing ordinary uninstall registrations may appear. |
| Xbox / Microsoft Store | Existing removable MSIX application inventory; dedicated game classification NOT implemented. |

No separate Games page is allowed. Pending discovery belongs in the existing Uninstaller unified list; use trustworthy manifests/identities, no whole-disk guessing or undocumented destructive commands.

## Product rules and requested UX
- Local-only; no telemetry/cloud, fake optimization, automatic destructive actions or tests against real user data.
- Native uninstall first; fresh identity verification and explicit confirmation for destructive actions.
- **Requested, NOT yet implemented:** auto leftover scan after verified removal (deletion still confirmed), app-wide startup orchestration/cache/background refresh, Cleaner auto scan/rescan, broad minimal-copy pass, game library integration.
- Therefore current Refresh/Scan/Analyze requirements still exist on some pages. Do not claim the requested no-mandatory-refresh experience is complete.
- Preserve KB/MB/GB/TB display labels, Arabic/English, safe errors/confirmations/accessibility text, logo, current version, namespaces, helper identities, `%LOCALAPPDATA%\SystemCore`, schemas and recovery paths.

## Current build and tests
- Run from repository root: `& ".\artifacts\current\F1\F1.exe"`; retain entire F1 folder including ElevatedHost.
- Cleanup locked restore, Debug, Release, x64 App/helper publication: PASS, zero build warnings/errors.
- Cleanup native runtime smoke: **34 PASS** (launch, all ten routes, attribution, absent Premium card, normal close). Unit smoke passed; exact totals in current state/report.
- Evidence: `artifacts/project-cleanup/`. Direct native run used after desktop-token launcher failed; no Standard User/UAC certification is implied.
- Earlier Uninstaller delivery: 150 engine checks and 110 UI checks, 12 fully visible rows at 1360×1000/96 DPI; 135 raw/108 hidden/27 visible on that historical inventory. Not a new count from cleanup.
- Earlier F1 rebrand: 1,739 assertions + 1,000 malformed inputs; historical screenshot/performance evidence archived, not rerun by cleanup.

## Remaining work / next step
Resume unfinished pre-release requirements only when instructed. The source currently includes Sidebar removal, initial Startup filtering, and a Cleaner baseline profiler. Measured historical baseline: **53.51s** overall; User Temp visited 75,910 entries. No after-optimization result exists.

Full photographic-reference acceptance still pending (clean reference background missing; authorized temporary SVG used). Higher DPI/full Narrator/high contrast, clean-machine/edition matrices, alternate-credential UAC and broader Release QA remain deferred.

**Do not redo completed phases. Do not start Phase 10 without explicit user approval.** Phase 10 means installer finalization, signing, Release QA and Production build; none is authorized by cleanup. No Git repository/commits exist in this workspace.
