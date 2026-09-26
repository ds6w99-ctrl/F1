# Test results index

The owner-approved two-gate policy is defined in [docs/GATES.md](docs/GATES.md).

## F1 UI rebrand — independent development delivery

**Build PASS; Runtime/Regression PASS; Branding/Data Compatibility PASS (development
scope); All-Pages UI Coverage PASS; Visual Reference PENDING; owner review PENDING.**
**1,739 assertions + 1,000 malformed inputs** passed. Locked restore, zero-warning
Debug/Release and the paired x64 publication are in `artifacts/f1-ui/publish/F1.exe`.
132 genuine runtime screenshots cover AR/EN, three themes, minimum size and populated
read-only states; reference comparisons and disclosed temporary artwork are included.
Native-only measurement: six cycles / 422.067 s, including two minutes minimized,
normal close/restart, empty app stderr and zero interval throws. Final private memory
was 199.594 MiB after cooldown and 198.781 MiB minimized (+14.840 / +13.195 MiB over
baseline); minimized CPU 0.094% versus baseline 0.097%. Null ExitCode is not verified
zero. This is short development evidence, with higher DPI/full Narrator/high-contrast
coverage and Release QA limits documented.

Evidence: `artifacts/f1-ui/summary.json`; [implementation report](docs/F1-UI-IMPLEMENTATION-REPORT.md).
The clean photographic background is missing; the owner-authorized original SVG is
temporary. Phase 9 owner review and Release / Production remain PENDING; Phase 10
has not started. Historical results below retain their original scope.

## Historical phase results

**Phase 9: 9A PASS; 9B PASS; 9C PASS; Development PASS; owner review PENDING; Release PENDING.**
**1,291 checks plus 1,000 seeded fuzz-like inputs passed.** Clean Debug/Release and
x64 App/helper publication, 24 native cycles / 569.165 seconds, normal close/restart.
No unhandled/unobserved/unexpected boundaries in the separate final exception audit;
two expected lifecycle events per instance. Primary runtime interval throw sum: zero.
Private memory late-cycle range 195.945–205.938 MB, cooldown 201.496→191.363 MB;
handles and threads decreased, managed samples all nonnegative. ExitCode remains null.
Current version `0.9.0-dev`; `artifacts/phase9/summary.json` and
[docs/PHASE-9-REPORT.md](docs/PHASE-9-REPORT.md) record scope and Release QA limitations.
Stopped for owner review; Phase 10 not started.
Phase 8 evidence below remains the accepted historical publication.

**Phase 8: Development PASS; subsequently owner-approved; Release / Production PENDING.**
**1,217 checks passed**, including 121 Product, 293 Product UI, 8 theme-localization,
48 Memory UI and 116 native UI regressions, plus prior engine suites and compatibility.
Locked restore, clean Debug/Release and x64 App/helper publication passed; tested
engine hashes match. Native-input stability: **24 cycles / 569.283 seconds**, normal
close/restart, empty stderr and 111 nonnegative managed readings. Cycle private memory
166.891 → 202.223 MB, later-cycle range 197.793–207.285 MB, cooldown 201.168 → 201.957 MB.
Runtime recorded 5,567 thrown exceptions including handled ones without type attribution;
exit code was null. Functional UIA and native-input measurements are separate.
Accepted evidence: `artifacts/phase8/completion/`, `artifacts/phase8/summary.json`,
[docs/PHASE-8-REPORT.md](docs/PHASE-8-REPORT.md). Preliminary/failed attempts remain
under `development/` and `attempts/`; previous phase evidence remains intact.
The owner subsequently approved Phase 8 and authorized Phase 9 only.

**Phase 7: 7A PASS; Development PASS; Release / Production PENDING.**
Analysis gate evidence: `artifacts/phase7/7a-verified/` (62 metrics/pressure/integration
checks plus native UI and regressions). Final: **88 metrics/pressure/decision/integration
checks, 48 production Memory UI, 24 synthetic Memory UI, 101 native regression**, all
existing Core regressions and 9 compatibility checks passed. **40 Core cycles / 160
checks** and **441.558-second / 20-cycle native-input GUI stability** passed without
UIA, forced GC or memory trimming. GUI cycle private memory **125.520 → 125.129 MB**
(1024-based); managed readings nonnegative. Production registers no releasable cache
owner and legitimately returns no-op; the injected fixture released exactly 16 MB
while preserving active data. Engine hashes match. Accepted evidence:
`artifacts/phase7/completion/`, `artifacts/phase7/summary.json` and
[docs/PHASE-7-REPORT.md](docs/PHASE-7-REPORT.md). Historical controller/test failures
are preserved under `artifacts/phase7/attempts/` and `7b/`. The owner subsequently
accepted Phase 7 and authorized Phase 8 only.

**Phase 6: 6A PASS; Development PASS; Release / Production PENDING.**
Complete real Startup inventory, independent Dashboard summary, reversible Registry/
file/task operations and durable state are implemented. Final evidence: **37 inventory,
72 mutation/security, 59 synthetic UI, 30 production Startup UI, 101 native regression**
checks passed, plus all existing Core regressions and compatibility. Six synthetic
UI mutations completed with zero remaining receipts. GUI idle: 120.494 seconds,
private memory after warm-up **109.676 → 107.016 MiB**. Core: 20 cycles / 380 checks,
**26.910 → 23.027 → 21.488 MiB** after cycle 1 / cycle 20 / cooldown. No invalid managed
samples in this final run. App/test/helper engine hashes match. Evidence:
`artifacts/phase6/completion/`, `artifacts/phase6/summary.json` and
[docs/PHASE-6-REPORT.md](docs/PHASE-6-REPORT.md). All prior failed attempts are retained.
The owner subsequently accepted Phase 6 and authorized Phase 7 only.

**UI unit-label follow-up (after Phase 5 acceptance): PASS.** Shared `InfoFormatting`
now displays B/KB/MB/GB/TB and B/s/KB/s/MB/s/GB/s while retaining 1024-based scaling.
Uninstaller sizes and its large-application filter use the shared formatter too.
Release solution build: zero warnings/errors; existing unit suite: **198 PASS**;
updated App published to `artifacts/phase5/app`. Evidence: `artifacts/ui-unit-formatting/`.
Earlier Phase 5 hashes and screenshots describe the accepted pre-label-update artifact.

**Phase 5 (2026-09-25): 5A PASS; 5B Safety PASS; Development PASS;
Release / Production PENDING.** Native original uninstall, receipt-backed read-only
scan and explicit selection/backup/revalidation deletion are implemented. Synthetic
native E2E passed 47 UI checks (3 selected leftovers, 8 bytes, backup, zero-result
rescan); native regression passed 100 checks. Final safety suite: 122 PASS under a
non-elevated desktop token. Passive GUI idle: 120 seconds, private memory after warm-up
98.219 -> 98.570 MiB. Separate core: 20 cycles / 1,140 passing checks, 18.832 MiB after
cycle 1 -> 17.840 after cycle 20 -> 17.453 after 120-second cooldown. Engine hashes match.
Hardening and passive stability
evidence is indexed by [docs/PHASE-5-REPORT.md](docs/PHASE-5-REPORT.md). Failed historical
driver attempts remain preserved. All destructive tests used newly created synthetic
registrations/files, never a user application. Phase 6 was subsequently authorized by the owner.

**Authoritative final isolation (2026-09-25): Phase 4 Development PASS; 4A Safety
PASS; Release / Production PENDING.** Owner-requested no-UIA tests completed:
Release app idle 600 seconds, 100 synthetic core cycles, then same-Core-process
cooldown over five minutes. Private Memory: GUI 98.699 -> 101.145 MiB; Core
15.777 after cycle 1 -> 17.238 at cycle 100 -> 16.773 after cooldown. All tracked
old session/item weak targets reached zero naturally. Engine hashes match the
published production assemblies. No recorded crashes/unhandled exceptions.
Three invalid negative cooldown GC.GetTotalMemory readings are preserved and
excluded; independent last-GC heap/commit metrics are documented. Classification:
**UIAutomation/test-harness growth; no continuing production leak detected**.
Evidence: `artifacts/debug/no-uia-final/`. Longer validation is in Release QA.
Earlier failures and BLOCKED decisions below are historical, not overwritten.

**Preceding native diagnostic (2026-09-25): Development BLOCKED; no production
native leak confirmed.** WPR succeeded with diagnostics elevated and app token
unelevated. Zero lost events; allocation/free accounting reconciled. UIAutomationCore
UiaNode Find allocations were identified; a separate client-disconnect control
showed 395,797 -> 26,946 outstanding bytes while the app remained alive. Final
confirmation completed **913.216 seconds / 36 cycles / 796 passing checks**, no
recorded crashes/exceptions. Private Memory 100.258 -> 174.305 MiB; last-half slope
**+0.649 MiB/min**, final-five-minute slope +0.188. Native heap commit still rose
79.348 -> 85.836 MiB from cycle 20 to end. The main WPR lifecycle was interrupted
by one UI wait assertion, so its short stable live-byte interval cannot settle
the complete later trend. Evidence and the final decision are in the report.
All entries below describe preceding rounds and remain preserved.

Latest native-memory confirmation: **1,813.909 seconds / 72 cycles / 1,587 passing
checks**, no recorded app crashes/exceptions, normal shutdown. Private Memory
99.586 -> 184.129 MiB; final-half slope **+0.307 MiB/minute**. VMMap identifies
late native heap commit growth (92.832 MiB at cycle 40 -> 96.082 MiB at end),
with Private Data/images/stacks comparatively stable. Exact allocation ownership
and sustained plateau remain unresolved: **Phase 4 Development BLOCKED**.
A–F isolation and diagnostic cooldown GC are complete; rooted historical Cleaner
graphs remain absent in the separate cooldown graph. No production code changed
in this pass. Evidence: `artifacts/debug/native/uninterrupted-confirmation/`.
Separate production native regression remains incomplete. Earlier results below
are preserved as historical evidence, not substituted for this final decision.

| Phase | Development Gate | Release / Production Gate | Report |
|---|---|---|---|
| 0 | **PASS** | **PENDING** | [Accepted phase-0 evidence](docs/PHASE-0-REPORT.md) |
| 1 | **PASS** | **PENDING** | [Phase 1 report](docs/PHASE-1-REPORT.md) |
| 2 | **PASS** | **PENDING** | [Phase 2 report](docs/PHASE-2-REPORT.md) |
| 3 | **PASS** | **PENDING** | [Phase 3 report](docs/PHASE-3-REPORT.md) |
| 4 | **PASS** (4A safety **PASS**) | **PENDING** | [Phase 4 memory investigation](docs/PHASE-4-MEMORY-DEBUG-REPORT.md) |
| 5 | **PASS** (5A/5B safety **PASS**) | **PENDING** | [Phase 5 report](docs/PHASE-5-REPORT.md) |
| 6 | **PASS** (6A **PASS**) | **PENDING** | [Phase 6 report](docs/PHASE-6-REPORT.md) |
| 7 | **PASS** (7A **PASS**) | **PENDING** | [Phase 7 report](docs/PHASE-7-REPORT.md) |

Phase 4: 195 unit, 17 scanner, 22 executor/security, 49 completion/helper/Shell,
9 compatibility, 6 System Information integration, 11 Performance integration,
88 production native UI and 10 focused visual/runtime checks passed.
The final 26-minute/42-cycle soak passed 885 functional checks with clean shutdown.
Private memory continued trending upward; memory stabilization remains a development
blocker. No final Development PASS is claimed. See the report for exact measurements
and the retained evidence of earlier interrupted driver attempts.

Memory-debugging follow-up: Dashboard summary and COM/WinRT row ownership were
corrected. Bounded native row reuse passes the GC-graph lifetime regression:
zero historical Cleaner sessions/items/rows after ten cycles, one view/viewmodel.
The resumed 30-minute run completed 72 cycles and 1,587 functional checks, but
last-half private-memory slope remained +0.574 MiB/minute. Plateau is unproven.
The optional extended run stopped in its UIA driver at cycle 29; final production
native regression also remains incomplete. Neither is counted as PASS. See the
memory report's final-decision section for the current gate and exact evidence.

All unavailable release-only cases: **DEFERRED TO RELEASE VALIDATION**.
See [docs/RELEASE-QA.md](docs/RELEASE-QA.md). Original phase-0 logs and historical gate
snapshot remain unchanged under artifacts/phase0-validation; that snapshot predates the
owner's new policy and is not the current gate decision.
