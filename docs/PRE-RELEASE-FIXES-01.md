# Pre-release fixes 01 — Sidebar / Startup / Cleaner performance

Scope: FIX 1 dead-code cleanup, FIX 2, FIX 3 scan performance, partial FIX 5 game discovery, and FIX 6 native-application automatic leftovers. Version remains `0.9.0-dev`.
No Phase 10, signing, installer finalization, deployment, or real startup mutation.

## Uninstaller

### Automatic Leftover Flow

FIX 6 native-application flow is implemented and verified; **DONE_WITH_CONCERNS**,
not full game-removal acceptance. Full evidence/report:
`artifacts/current/fix6-verify/task-6-report.md`.

- A matching verified `Removed` receipt automatically enters the existing read-only
  scan while retaining the same Busy/cancellation operation. Uninstall cancellation,
  unverified outcomes, wrong identities and games cannot start it. Late uninstall
  progress cannot overwrite the scan state. New inventory invalidates the UI receipt.
- Cancellation leaves no results/selection and returns to verified removal, not an
  error. Scanner faults leave manual retry available, never call deletion, and never
  authorize partial cleanup. `UnScanLeftovers` and all previous AutomationIds remain.
- Results use paired EN/AR `No leftovers.` or `Leftovers found` plus count/size.
  Existing item details carry confidence and `may contain personal data`; no new
  explanatory result paragraphs. Added a functional Keep action (there was no
  existing standalone Keep button). Confirmation dialogs/registry warnings remain.
- DataPath trees, save/screenshot/mod/profile/config/user-content paths and uncertain
  file formats are Review. Only proven-owned application-binary-extension files can
  remain Safe; the extension is a restriction, never ownership evidence. Registry
  subtrees/non-identity values are Review. All defaults stay off. Review/Ignore are
  unselectable and deletion still rejects them, including forged requests. Empty
  directory deletion remains nonrecursive; reparse/hard-link Ignore takes priority.
- Structural game shared-root/cache/anti-cheat/redistributable/mod/workshop guards
  were added. **Games still receive no removal receipt (FIX 5); no automatic game
  scan exists.** No launcher uninstall or game ownership evidence was invented.
- Native harness changes followed observed failures, not a relaxed product gate:
  `Removal verified by identity` saw the new completed scan state; the old enabled-
  checkbox count saw 1 rather than 3 because DataPath rows correctly became Review.
  The harness now verifies automatic results without pressing Scan, all three
  defaults off, two disabled Review rows, exact registry confirmation, personal-data
  retention, Keep, safe-only bytes and a post-fixture-teardown empty retry. Original
  cancellation/sibling preservation assertions remain. `Test-F1UninstallerUi.ps1`
  itself was unchanged; the synthetic lifecycle lives in `Test-UninstallerPage.ps1`.

Fresh final checks (all exit **0**, no failures):

| Check | Result |
|---|---:|
| Exact candidate build, locked restore + Debug/Release + app/helper publish | 0 warnings / 0 errors in each build |
| Uninstaller default synthetic + scan + delete | 240 assertions |
| Uninstaller regression command | 335 engine assertions + 63 native UI checks |
| Security / Unit / Startup | 56 (+1,000 fuzz inputs) / 198 / 80 |
| Cleaner default / executor / completion / scan-progress / scan-performance | 18 / 22 / 49 / 24 / 43 |
| Game library UI / legacy Uninstaller UI / ten-route smoke | 26 / 57 / 34 |
| Read-only native inventory | Complete; 144 raw / 27 removable / 108 hidden / 9 launcher-managed |

`stderr.txt` was empty in every UI run. The first `Test-F1UninstallerUi.ps1`
attempt aborted with the known capture flake (`Foreground F1 desktop capture
required`) because the F1 window did not reach the foreground within 5 s; the
immediate re-run passed 57/57. The raw inventory count varies between runs
(135 in an earlier run, 144 here) while hidden/removable stayed 108/27.

The brief listed the last two Cleaner modes in the opposite order: actual unchanged
counts are **scan-progress=24**, **scan-performance=43**, not vice versa. No Cleaner
source/test changes were made. This is a reporting-order discrepancy, not a hidden pass.

Automatic native scan measured by the controller on this machine after the
synthetic uninstall: **265.6527 ms lower bound** from the first sampled Scanning
status until results were ready (100 ms polling); wait after confirmation was
**1,678.4646 ms**, excluding the helper's prior 250 ms wait. Not an exact scan
benchmark. No real application/game was uninstalled, nor real application
files/registry keys/install folders targeted for mutation. Tests created/removed
only isolated synthetic fixtures; UI scripts temporarily changed/restored F1
preferences and generated logs, screenshots and synthetic registry backups. A
literal zero-write claim is not made.

**No visual review was performed.** The earlier claim in this section that a
light-theme screenshot was visually inspected is withdrawn: no vision-capable
model is confirmed to have viewed any image in this work, and an agent's
statement is not evidence. The screenshots listed above are evidence that the
run produced images, nothing more. Full visual, accessibility, high-DPI,
clean-VM and real-game certification remain outstanding.

### Game Discovery

FIX 5 is **PARTIAL**, not six-launcher acceptance. That work did not include Phase 10 or FIX 4/6/7; subsequent FIX 6 work is recorded above.

Read-only device evidence: `artifacts/current/fix5-tests/game-inventory.json`, exit 0.

| Source | Launcher detected | Games from verified metadata | Unsupported/unverified |
|---|---:|---:|---|
| Steam | Yes | 10 | 0 |
| Epic | Yes | 1 | 0 |
| Xbox / Store | Not established | 0 classified games | 1 classification gap; existing official MSIX inventory retained |
| EA | No verified registration found | 0 | Adapter intentionally unsupported when installed |
| Ubisoft | No verified registration found | 0 | Adapter intentionally unsupported when installed |
| Battle.net | No verified registration found | 0 | Adapter intentionally unsupported when installed |

**Duplicates merged: 1**. Unified inventory contains 11 game-labelled rows, 10
launcher-managed (no native uninstall), with native inventory Complete=true.
These are discovery measurements, not tests of real uninstall or launcher launch.
No real game, save, launcher configuration or install folder was modified.

Architecture and safety decisions:

- Added `IGameLibraryProvider` and six independent DI providers. Steam resolves its
  registered installation, reads `steamapps/libraryfolders.vdf`, and only enumerates
  top-level `appmanifest_*.acf` in the declared libraries (including other drives).
  Epic reads its known/registered manifest directory and `.item` records. Installed
  state and existing manifest-declared install directory are required. Stale,
  incomplete, malformed, oversized and unsafe records are rejected, not guessed.
- Reads are bounded to 1 MiB/manifest, 4,096 manifests/provider, 64 Steam libraries;
  KeyValues and JSON nesting are bounded. Local fixed-drive paths only in the native
  environment, reparse ancestry rejection and leased file reads. `WindowsApps` is
  rejected before filesystem probing. No drive traversal, recursive game-size scans,
  folder-name discovery, game execution, invented URI or uninstall command.
- `InstalledApplication` keeps its positional constructor and native type intact.
  Optional init-only ItemKind/Source/game metadata extend presentation; separate
  GameInstallLocation never replaces a native registration's ownership path.
- `RemovableApplications.IsVisible` is **unchanged**: presentation eligibility is a
  subset of native authorization, never a grant. `UnifiedLibraryPresentation` adds
  a distinct **read-only launcher-managed** category to the *same* existing list.
  Such selections say `Managed by ...`, hide the native uninstall button, and expose
  a separate Open launcher button only when a fresh registered executable is safe.
  Opening passes no arguments and constructs no protocol URI. Unknown action is
  disabled with a localized explanation. No real launcher was opened during tests.
- All prior AutomationIds and the status/type/size filter remain. Added compact
  `UnKindFilter` All / Applications / Games and source text in the existing row
  metadata slot; no page, sidebar entry or games section. Search/sort/paging remain
  shared. New copy is paired in English/Arabic resources.
- Dedup uses exact launcher identity, exact Steam registered AppId, or a unique
  exact local install-path match, never names. Ambiguous/conflicting identities are
  retained separately or quarantined with diagnostics rather than guessed. Native
  ID, command, restrictions and permission are preserved; merge cannot grant rights.
- Native completeness retains its Phase 5 meaning; optional source diagnostics are
  separate. Game completion additionally requires a verified, still-detected launcher,
  no matching discovered game, and no unverified source status. Game discovery does not establish save/shared-data
  ownership: it issues **no leftover cleanup receipt for games**, including merged
  games, and invalidates earlier native evidence for a newly game-classified identity.
  Existing application evidence/scanner/deletion behavior is unchanged.
- Icons reuse the bounded resource-only ApplicationIconReader/cache: registered
  icon, trusted Epic manifest executable, registered launcher fallback, existing F1
  fallback. No verified standalone manifest-logo adapter was added. Reliable manifest
  size is displayed; unknown remains Unknown.

Unsupported scope is explicit, not a synthetic claim of support: EA/Ubisoft/Battle.net
fixtures assert that plausible but unverified formats are rejected. These providers
detect known registrations only, and do not claim configured-library/manifest parsing.
Xbox game classification is not inferred from package names: PackageManager,
AppListEntry and deployment authorization remain in the existing MSIX provider;
protected/framework/resource packages stay hidden. A verified game-specific metadata
source is still required for an Xbox badge/game filter. Launcher-specific fallback
assets and verified Steam/Epic native launcher-uninstall mechanisms remain gaps.

Verification: candidate wrapper exit 0; Debug and Release each **0 warnings / 0 errors**,
App/helper publication succeeded. Uninstaller synthetic + leftover scan/delete:
**245 assertions, 0 failures, exit 0** (`fix5-tests/uninstaller.txt`). Initial
model-contract test failed before implementation; two additional edge tests failed
before WindowsApps rejection and managed dedup path preservation were fixed.
Further regression tests failed before stale native evidence invalidation and
fail-closed game completion without launcher verification were fixed.
Regression evidence (`artifacts/current/fix5-tests/console-final-summary.json` for
final assertion-suite reruns; `console-summary.json` for all diagnostic modes):

| Suite / mode | Assertions or cycles | Exit |
|---|---:|---:|
| Uninstaller synthetic + scan + delete | 245 PASS | 0 |
| Security | 56 PASS; 1,000 fuzz inputs | 0 |
| Unit | 198 PASS | 0 |
| Startup default | 80 PASS | 0 |
| Cleaner scanner | 18 PASS | 0 |
| Cleaner executor | 22 PASS | 0 |
| Cleaner completion | 49 PASS | 0 |
| Cleaner scan performance | 43 PASS | 0 |
| Cleaner scan progress | 24 PASS | 0 |
| Cleaner memory engine | 100 workload cycles; no assertion counter | 0 |
| Cleaner memory isolation | 100 workload cycles + cooldown; no assertion counter | 0 |
| Cleaner read-only scan | Incomplete, not a pass | 2 |
| Cleaner read-only benchmark (1 worker) | Diagnostic completed; not an assertion suite | 0 |

Total assertion suites: **735 PASS**. Cleaner real scan remained fail-closed:
UserTemp Incomplete (Limit=5), WindowsTemp Unavailable; browser/WER adapters unavailable.
It took 71,225.5148 ms; this is not a FIX 3 benchmark comparison or regression claim.
No Cleaner source was modified. A later candidate rebuild overlapped the memory
diagnostic and failed with MSB3027/MSB3021 (10 copy warnings, 2 errors), not a compiler
failure. The original log is preserved in `fix5-tests/build-overlap-failed.txt`;
final candidate rerun after that diagnostic finished **passed, exit 0**, with
Debug/Release each **0 warnings / 0 errors** and App/helper publication. Published
Core/Infrastructure SHA-256 values match the Uninstaller test engine exactly.

Final-publication native checks (`fix5-tests/native-final-summary.json`):
`Invoke-F1UninstallerRegression.ps1` **exit 0**, with **208 PASS** engine assertions
(synthetic + delete subset) and **57 PASS** original Uninstaller page checks;
new read-only EN/AR game UI **26 PASS, exit 0**; cleanup smoke **34 PASS, exit 0**
(ten routes, attribution and absent Premium card). All used the current publication.

Independently re-run and re-measured against the same publication by the primary
session, evidence under `artifacts/current/fix5-verify/`: Debug **0 warnings /
0 errors**, Release **0/0**, publish app + helper succeeded; UninstallerTests
**208** `FinalChecks`, 0 failed, exit 0; UnitTests **198** PASS, 0 FAIL, exit 0;
SecurityTests **56** checks, 0 failed, exit 0; StartupTests **80** checks,
0 failed, exit 0; CleanerTests default/executor/completion/scan-progress/
scan-performance **18 / 22 / 49 / 43 / 24** PASS, 0 FAIL, each exit 0 — identical to
the pre-FIX 5 baseline, so no Cleaner regression. Game library UI **26/26**;
uninstaller regression fixture UI **57/57**; cleanup smoke **34/34**
(`artifacts/project-cleanup/runtime`); legacy Uninstaller UI **57/57** after the
harness update below. `stderr.txt` empty in every run.
The actual legacy
script is `Test-F1UninstallerUi.ps1` (`Test-UninstallerUi.ps1` does not exist). It
FAILED, exit 1 after 5 recorded passing checks, and the failure is caused by FIX 5,
not pre-existing. Line 48 assumed the first visible row always exposes an enabled
`UnUninstall`; the first sorted row is now a correctly launcher-managed game whose
destructive button is `Collapsed`, so `Find 'UnUninstall'` returns nothing and the
`[bool]$ok` binding fails. Measured directly: `btnNull=True` for `Among Us`, a
launcher-managed Steam game. The product behaviour is the required one — a managed
game must expose no destructive control.

The owner-approved contract-aware harness update was therefore applied, and it
**strengthened** the script rather than relaxing it (`artifacts/current/fix5-verify/
uninstaller-ui-4/checks.json`, **57 checks, 57 passed, 0 failed**):

- The selection loop now classifies each visible row by what it actually exposes,
  then selects the first row that really holds native removal rights.
- `A visible native app has an enabled native uninstall action` and
  `Selected native app has enabled native uninstall action` retain the original
  intent against a correctly identified native row, and the original confirmation /
  cancel-without-uninstall checks still run against a native selection.
- Three new assertions were added for the new invariant:
  `Launcher-managed game exposes no native uninstall action`,
  `Launcher-managed game exposes no leftover scan action`, and
  `Launcher-managed game still offers a separate launcher control`.
- No fake uninstall button was added and no assertion was removed or loosened.
- A row with no native removable app now fails loudly instead of continuing on `$null`.

Wrapper scripts only gained explicit current AppPath/EvidencePath
parameters, with existing assertions unchanged.

No screenshot visual review is claimed.

## Sidebar

- F1 Premium card removed: YES (existing XAML removal retained; unused view-model members and both resource pairs removed).
- Made By Falcone preserved: YES (source inspection; native smoke result recorded below).
- About still accessible: YES (existing `NavAbout` footer retained; native smoke result recorded below).

## Startup Inventory

Read-only device measurement (`artifacts/current/fix2-tests/inventory.txt`):

```text
Raw startup inventory: 47
Protected/non-manageable hidden: 41
Visible manageable items: 6
Registry: 6
Startup folders: 0
Scheduled tasks: 0
```

Inventory complete: YES. Unavailable sources: none. Measured inventory plus icon
hydration: 1006.4563 ms (not a general performance claim). The console probe used
the published helper payload in its installation-relative `ElevatedHost` folder;
it did not launch the helper or change any real startup entry.

`CanManage` is an inventory-only, JSON-ignored capability. Main rows, selection,
page counts and dashboard totals use manageable items. Preflight checks supported
concrete providers, source identity, permissions through AccessCheck, helper
availability for elevation, safe paths, same-volume folder moves and recovery
receipts. Read-only/unsupported tasks are hidden. Existing mutation authorization,
revalidation, recovery schemas, paths and read-back remain intact. Capability
checks are not a guarantee against changes between scan and a later operation.

## Startup Icon Sources

Measured real-device extraction results:

```text
Registry: 6
Startup Folder: 0
Scheduled Tasks: 0
Fallback: 0
```

Synthetic tests additionally exercise executable resource extraction, explicit
shortcut icon preference, shortcut target fallback and trusted task action icons.
The synthetic test executable embeds the existing F1 icon as a resource; no brand
asset was modified. Missing/malformed/untrusted sources retain a themed startup
glyph, never a blank icon slot. Extraction uses resource APIs, not execution.

Rows use 36×36 icons, name, publisher/source and status. Selection uses the existing
blue accent and selected-background brushes. Icon work is page-lazy, serialized
off the UI thread, cancelled on refresh/unload/disposal, and cached as PNG bytes
with a 128-entry bound. Resource identity/metadata changes invalidate cached
results; unchanged requests reuse extracted bytes across refreshes.

## Cleaner Performance

FIX 3, 2026-09-26. Real-device operations were **read-only**. Destructive
regressions used newly created synthetic fixtures or a fake Recycle Bin only.
Historical baseline: **53.51 s / 75,910 User Temp entries**, not the comparison
denominator. Fresh measurements used the existing console benchmark, extended
with overall process CPU/private-memory and Shell-query measurements before
changing the production scanner.

**Measured bottleneck: User Temp**, 75.344 s in the fresh category profile;
the other filesystem categories together took about 38 ms. The original scanner
already streamed names, but fetched attributes separately and opened/pinned the
whole native path even for recent files that would subsequently be rejected.
Replacing that preliminary work with directory-record attributes/write time and
early conservative rejection gave a 39.157 s overall / 38.714 s User Temp
single-worker measurement before adding category concurrency. Native call timing
was not individually instrumented; the category dominance and single-change
measurement, not guessed per-call percentages, support this optimization.

| Metric | Before | After (2 workers) | Change |
|---|---:|---:|---:|
| Total duration, including Shell query | 78.831 s | 37.947 s | -51.86% observed |
| Items/sec | 1,163.56 | 2,591.69 | +122.74% observed |
| User Temp, separately timed | 75.344 s | 38.077 s | -49.46% observed |
| Slowest category | User Temp | User Temp | unchanged |
| CPU avg, normalized over logical processors | 7.59% | 8.29% | +0.71 percentage points |
| Approximate private-memory delta | 35.72 MiB | 41.00 MiB | +5.28 MiB |

Overall visited/eligible/skipped: **91,724 / 42,914 / 42,732** before;
**98,348 / 45,257 / 46,986** after. This is the same live set of trusted roots,
**not an immutable dataset**: retained synthetic fixtures and ongoing machine
activity changed it. Cold/warm cache and scheduling noise also apply. These are
observed runs, not a guaranteed 2x speedup. The first category-only cold baseline
was 116.793 s for User Temp (91,395 visited), further illustrating variability.
Directories count as visited but need not be eligible or skipped.

| Category | Before ms | After ms | Before visited / eligible / skipped | After visited / eligible / skipped |
|---|---:|---:|---|---|
| User Temp | 75343.935 | 38076.881 | 91707 / 42963 / 42680 | 98271 / 45248 / 46935 |
| Windows Temp | 0.752 | 0.548 | 0 / 0 / 1 | 0 / 0 / 1 |
| Shader Cache | 20.816 | 9.974 | 36 / 0 / 18 | 36 / 0 / 18 |
| Crash Dumps | 5.722 | 0.687 | 10 / 0 / 10 | 10 / 0 / 10 |
| Thumbnails | 10.970 | 9.160 | 32 / 9 / 23 | 32 / 9 / 23 |
| Recycle Bin Shell query | 50.608 | 41.039 | 0 Shell items | 0 Shell items |

User Temp was **Incomplete both before and after**, and Windows Temp unavailable;
these statuses remain fail-closed, not performance failures to hide. Browsers and
WER remain unsupported; no profile traversal or `$Recycle.Bin` enumeration added.
Category measurements are separate serial probes, not additive concurrent timings.
Raw JSON includes category bytes, status, throughput, CPU and memory deltas.

| Workers | Overall duration | Items/sec | CPU avg | Private-memory delta |
|---:|---:|---:|---:|---:|
| 1 | 44.707 s | 2199.83 | 7.12% | 38.89 MiB |
| 2 (selected) | 37.947 s | 2591.69 | 8.29% | 41.00 MiB |
| 4 | 37.991 s | 2588.75 | 8.31% | 40.78 MiB |

Two workers had the lowest observed total and avoid the extra concurrency of four;
the 2-vs-4 difference is within likely measurement noise. No claim that concurrency
itself caused the whole improvement: one category dominates. Worker settings are
internal, clamped to 1..4; total visited/byte budgets are synchronized and category
output ordering is stable. Deletion was not parallelized or changed.

### Safety and functional regression

`FileSystemEnumerable` is explicitly nonrecursive, includes hidden/system/reparse
records for explicit rejection, and does not silently ignore root access errors.
It materializes one path plus attributes/write-time per entry, no `FileInfo`, size
query, hash or per-file log. Enumeration metadata is a rejection hint only:
every accepted file still receives the **unchanged** native exclusive lease,
ancestor/reparse/resolved-path checks, hard-link/type checks, identity/size and fresh
age validation. `CleanerSafety`, root allowlists, executor, freshness/session
authorization and recovery paths are unchanged. Root provider resolution remains
once per scan; canonical safety/ancestor validations were deliberately not cached
away. Thus "single metadata read" applies to enumeration metadata, **not** removal
of the mandatory fresh native identity read.

New synthetic tests cover array-oracle/streaming equivalence, recent locked-file
rejection before exclusive opening (observed RED then GREEN), stable counts/bytes,
single target resolution, 1/2/4 and out-of-range worker bounds, in-flight cancellation
under two seconds, incomplete category authorization rejection, and Dashboard
complete/partial summaries. All required regressions were rerun: traversal,
protected roots, reparse/junction escape, root survival, age filtering, incomplete
scan protection, session integrity and changed-since-scan **PASS**.

| FIX 3 verification | Observed result | Exit |
|---|---|---:|
| Cleaner default | 18 PASS | 0 |
| Cleaner executor | 22 PASS | 0 |
| Cleaner completion / broker / fake Shell | 49 PASS | 0 |
| Cleaner scan-performance | 43 PASS | 0 |
| Cleaner live-progress (workers 1 / 2 / 4, synthetic file + fake Shell categories) | 24 PASS | 0 |
| Security | 56 PASS plus 1,000 fuzz inputs | 0 |
| Unit, including Dashboard summary lifetime/status | 198 PASS | 0 |
| Startup / synthetic mutation | 80 / 74 PASS | 0 / 0 |
| Reliability | 16 PASS | 0 |
| Memory engine | 100 synthetic scan/clean/rescan cycles, 300 invariant checks; no printed PASS counter | 0 |
| Memory-engine isolation | 100 cycles / 300 invariants, then 300 s cooldown; completed at 371.739 s | 0 |
| Benchmark before / after / workers 1, 2, 4 | Read-only measurement completed; no assertion counter | 0 each |
| Real-scan mode | EXPECTED FAIL-CLOSED: User Temp Incomplete, 42.448 s / 106,553 User Temp entries; depth bound retained | 2 (observed) |
| Final locked restore / Debug / Release | PASS; both builds 0 warnings / 0 errors | 0 |
| Final x64 production App / helper / separate validation publish | PASS; 0 warning/error log entries; Core/Infrastructure hashes match between production and validation | 0 |
| Native synthetic Cleaner lifecycle | 26 checks, 0 failed; scan/cancel/rescan/select/105 deletions/111 actual bytes | 0 |
| Production capture script | EXPECTED FAIL-CLOSED UI: 7 checks passed, then harness attempted disabled Clean; not a FIX 3 defect | 1 (observed) |
| Read-only native EN/AR live progress / cancel / rescan | 25 checks, 0 failed | 0 |

The native lifecycle used `artifacts/current/CleanerValidation/F1.exe`, not the
production binary. Production capture used `artifacts/current/F1/F1.exe`; its
confirmation assumption encountered `ElementNotEnabledException` because Clean
was disabled by design. No confirmation was accepted, no real file deleted, and
no test was weakened to bypass this.
Screenshots were captured; **no visual review performed**.
Final progress-follow-up lifecycle evidence:
`artifacts/current/review/cleaner-fix3-progress-lifecycle/`
(26 checks, 0 failed, 17.514 s). Final console regression total: **580 printed
PASS assertions**, excluding native checks, fuzz inputs and memory invariants.
`--real-scan` deliberately returns 2 when User Temp is incomplete; this is **not a
claim of a complete scan**, and is expected safety behavior, not failed FIX 3
acceptance. The original observed exit codes are retained, not rewritten to zero.

### Historical fail-closed comparison (follow-up correction)

The archived `docs/archive/evidence/phase4a-validation/real-scan.txt` and
`docs/archive/evidence/phase4b-validation/real-scan.txt` both show User Temp
**Incomplete**, `Skipped.Limit = 2`, top-level `Complete = false`; the FIX 3 scan
has the same signature. The incomplete state therefore predates this optimization.
In the current scan, 106,553 User Temp entries are below the unchanged 200,000
category / 500,000 total caps. Two temp subtrees hit the unchanged
`depth > MaximumDepth` guard (depth 32); that guard still marks the category
Incomplete and records Limit. **Clean remains disabled by design for the default
incomplete category selection.** Incomplete categories never authorize discovered
file IDs, as the regression test confirms; independently complete categories retain
their existing eligibility rules. Neither the bound nor this safety rule was
weakened to satisfy a capture script. These two observations are not outstanding
FIX 3 defects or acceptance failures.

### Throttled live progress — implemented and verified

The scanner now reports the current category, aggregate visited count, eligible
count and bytes through `CleanerScanProgress`. A **shared 150 ms monotonic gate**
coalesces updates across category workers; only category start/end transitions are
immediate. Worker switching per entry cannot bypass this gate. Snapshots use the
existing synchronized scan counters; no parallel enumeration mechanism, per-file
UI dispatch, timer or polling worker was added. Work remains on `Task.Run`, and
cancellation checks surround reporting. The service forwards progress and adds
Shell-query transitions/counts without enumerating the Recycle Bin.

The existing Cleaner view adds a single concise localized Name/Value/Status line
(`CleanLiveProgress` in English and Arabic), leaving all existing summaries,
category statuses, safety notices and confirmations intact. `Progress<T>` is
created on the UI context; cancelled, completed, disposed and superseded scans
cannot apply queued stale progress. The line clears when the operation ends.

New console tests were observed RED before implementation, then 24 PASS: all
category transitions (including unavailable categories), nondecreasing totals,
final-result agreement, delivery bounds/150 ms spacing, cancellation while progress
flows, and service forwarding through a synthetic file category plus fake Shell.
Native `scripts/Test-CleanerProgress.ps1` is read-only and invokes only scan/cancel:
**25 checks PASS**, English and Arabic, with 20/21 observed changing progress
values during each three-second sample; cancel-to-idle was **129.751 / 88.057 ms**.
It also verifies labels, UI responsiveness, disabled Clean during scanning and no
stale progress after cancellation. The existing synthetic lifecycle separately
passed **26 checks**, including actual synthetic deletion/bytes and rescan.

Follow-up verification reran the exact candidate build (locked restore, Debug,
Release, x64 App/helper: **0 warnings/errors, exit 0**), all listed console suites,
the two native scripts, benchmark with two workers (exit 0), and memory-engine
isolation (100 synthetic cycles plus cooldown, exit 0). The original benchmark
comparison table remains the original measured comparison; the later live-dataset
probe is saved separately at `artifacts/current/fix3-tests/progress-benchmark.txt`.

**Final FIX 3 status: implemented and verification PASS.** No outstanding FIX 3
functional blocker identified. Screenshots were captured but not visually reviewed;
fixed-dataset/repeated-trial performance certification and clean-VM/UAC certification
are not claimed. No automatic scan/orchestration (FIX 7) or other pre-release feature
was added; Phase 10 has not started.

Evidence: `artifacts/fix3-before-categories.txt`, `artifacts/fix3-before.txt`,
`artifacts/fix3-early-rejection.txt`, `artifacts/fix3-workers-{1,2,4}.txt`,
`artifacts/current/fix3-tests/`, `artifacts/current/review/cleaner-fix3-lifecycle/`,
`artifacts/current/review/cleaner-fix3-production/`. A transient Debug rebuild
failed (10 MSB3026 warnings / 2 copy errors) while the separate memory-isolation
experiment held its test DLL; logs retained as `*-locked.txt`, not hidden.
After that experiment exited normally (0), the exact requested build command was
rerun successfully with zero warnings/errors, followed by final regressions and
the native lifecycle. No process was killed and no build/test was disabled.
Progress evidence: `artifacts/current/fix3-tests/CleanerProgress.txt`,
`progress-red.txt`, `progress-additional-exits.json`,
`artifacts/current/review/cleaner-fix3-progress/`, and
`artifacts/current/review/cleaner-fix3-progress-lifecycle/`.

## Verification (FIX 1 / FIX 2 historical evidence)

| Check | Result | Exit |
|---|---|---:|
| Startup console suite | PASS: 80 checks, 0 failed | 0 |
| Startup synthetic mutation suite (`--mutation`) | PASS: 74 checks, 0 failed | 0 |
| UnitTests | PASS: 198 PASS assertions | 0 |
| SecurityTests | PASS: 56 checks, 0 failed; 1,000 fuzz inputs | 0 |
| Real inventory + icon probe | PASS, read-only | 0 |
| Final locked restore | PASS: locked restore, existing packages up-to-date | 0 |
| Final Debug / Release solution builds | PASS: 0 warnings, 0 errors in each | 0 |
| Final x64 App + ElevatedHost publication | PASS: both payloads, no warning/error log entries | 0 |
| Startup native page regression, production binary | PASS: 42 checks | 0 |
| Startup synthetic script, requested production binary | NOT APPLICABLE: production excludes `STARTUP_VALIDATION`, so the synthetic fixture does not exist by design. Evidence retained; use the validation binary for this script. | 0 |
| Startup synthetic script, separate validation binary | PASS after harness repair: 71 checks, 0 failed, three consecutive runs | 0 |
| Sidebar / ten-route smoke | PASS: 34 checks | 0 |
| Read-only EN/AR selected-row and About captures | PASS: 12 checks; no visual review | 0 |

Logs: `artifacts/current/fix2-tests/`; build logs:
`artifacts/project-cleanup/build/`.

The HKLM/Common Startup filter-contract checks exercise already-classified helper
capabilities; they are not real-device mutation tests. No real startup Enable or
Disable was dispatched. Existing synthetic mutation tests retain their safety
assertions. New preflight tests include rejected read-only file attributes and
write-denied task descriptors and a full recovery store; `CanManage` is explicitly
absent from recovery JSON. A failing full-store regression was observed before
correcting source-disabled tasks' receipt-capacity preflight.

### Native synthetic UI: root cause and harness repair

The production binary intentionally excludes `STARTUP_VALIDATION` and therefore
cannot supply `SystemCore Synthetic Registry`. Running the synthetic script against
it is not a valid check; the evidence is retained in
`artifacts/current/review/startup-synthetic-production-fix2/`.

An additional publication using the existing `-p:StartupValidation=true` switch
was built separately at `artifacts/current/StartupValidation/F1.exe`. Against that
binary the synthetic script originally threw when looking up `PrimaryButton` after
reopening the cancelled confirmation. The cause was investigated to root cause
before any change, and it is a **test-harness defect, not a product defect**:

- Instrumented polling showed the reopened confirmation never appeared at all
  within 5,000 ms, so it was not a slow-render race.
- The view already refuses re-entrancy: `StartupView.ConfirmAsync` returns early
  while `confirming` is set, which is released only after `await dialog.ShowAsync()`
  completes. Measured on this host over three runs: the dialog leaves the UI
  Automation tree after ~25-28 ms, but the guard releases after ~338-363 ms. The
  script re-invoked Disable after a fixed 150 ms sleep, inside the guard window.
- The same failure was reproduced on an unmodified `pre-phase10-baseline`
  publication (5,062 ms, no dialog), so it predates all FIX 2 work.
- With a 2,000 ms wait, and again after re-selection, navigation and refresh, the
  confirmation opened normally every time. No wedged state, no leaked dialog.

A second, independent defect then surfaced once the first was removed: the check
`DisabledBySystemCore after refresh` asserted the literal `Disabled by SystemCore`.
That is pre-rebrand product copy. `{Brand}` resolves to `ProductIdentity.Name`,
which is `F1`, so the rendered text is `Disabled by F1` and the assertion could
never match again. Backend evidence for the same run was correct
(`ItemStatus: 1` = `DisabledBySystemCore`, `Outcome: 0`).

The harness was repaired, not weakened:

- `OpenConfirmation` performs a bounded retry until the confirmation actually
  exists, replacing the fixed 150/200/250 ms sleeps. Extra invokes are no-ops
  because the same re-entrancy guard rejects them, so retrying cannot open a
  second dialog.
- `WaitFor` replaces the remaining fixed waits around the primary button and adds
  an explicit `Cancelled confirmation closes completely` check.
- Failures that previously surfaced as an opaque null-reference now surface as
  named checks: `Disable confirmation reopens after cancel`,
  `Disable confirmation primary becomes available`,
  `Enable confirmation primary becomes available`.
- The stale brand literal is replaced by the label resolved from the shipped
  `Shell.resx` and the current `ProductIdentity.Name`, so a future rename cannot
  silently break the check. The failure detail now reports expected and actual.

Result: 71 checks, 0 failed, on three consecutive runs against
`artifacts/current/StartupValidation/F1.exe`, and 71 checks, 0 failed against an
unmodified `pre-phase10-baseline` publication, which confirms the repair is
product-independent. Backend audit: 6 operations, all `Outcome: 0`, each disable
retained recovery state and each enable finalized it. Completion evidence: task
enabled, folder restored, zero remaining receipts. App stderr empty.

Evidence directories, all under `artifacts/current/review/`:
`startup-synthetic-harness-fix3/`, `synth-stability-run2/`, `synth-stability-run3/`,
`synth-baseline-fixedharness/`, `diag-confirmation-timing/`,
`diag-confirmation-timing-BASELINE/`, `diag-confirmation-stuck/`,
`diag-guard-threshold/`, `diag-guard-threshold-2/`, `diag-guard-threshold-3/`.
The original failures remain in `startup-synthetic-validation-fix2/` and
`startup-synthetic-validation-retry-fix2/`.

## Screenshots / remaining verification

Actual native screenshots (captured, **no visual review performed**):

- `artifacts/current/review/startup-icons-fix2-final/startup-en-selected.png`
- `artifacts/current/review/startup-icons-fix2-final/startup-ar-selected.png`
- `artifacts/current/review/startup-icons-fix2-final/sidebar-about-ar.png`

Capture metadata and published assembly hash are in the adjacent `capture.json`.
The existing Startup regression also captured its English/Arabic empty-filter
states in `artifacts/current/review/startup-page-fix2-final/`.

Remaining gap: the existing synthetic native script has not completed the six
UI-driven operations. Visual quality has not been reviewed. No commit or push.
Cleaner performance and FIX 3–7 are outside this change and were not measured.

## Changed files

- `src/SystemCore.App/ViewModels/ShellViewModel.cs`
- `src/SystemCore.App/Resources/Shell.resx`
- `src/SystemCore.App/Resources/Shell.ar.resx`
- `src/SystemCore.App/Startup/StartupView.xaml.cs`
- `src/SystemCore.Core/Startup/StartupModels.cs`
- `src/SystemCore.Core/Startup/ManageableStartupItems.cs`
- `src/SystemCore.Infrastructure/Startup/StartupAccessProbe.cs` (new)
- `src/SystemCore.Infrastructure/Startup/StartupManageability.cs` (new)
- `src/SystemCore.Infrastructure/Startup/StartupIconCache.cs` (new)
- `src/SystemCore.Infrastructure/Startup/StartupInventoryService.cs`
- `src/SystemCore.Infrastructure/Startup/StartupFolderProvider.cs`
- `src/SystemCore.Infrastructure/Startup/TaskSchedulerStartupProvider.cs`
- `src/SystemCore.Infrastructure/Uninstaller/ApplicationIconReader.cs` (shared resource-only extraction entry point)
- `tests/SystemCore.StartupTests/Program.cs`
- `tests/SystemCore.StartupTests/Fix2Tests.cs` (new)
- `tests/SystemCore.StartupTests/SystemCore.StartupTests.csproj`
- `scripts/Capture-StartupFix2.ps1` (new, read-only native capture)
- `docs/PRE-RELEASE-FIXES-01.md` (this report)

## FIX 7 - Orchestration and auto-load (passed, stability gate closed)

This section is a running report. FIX 7 is not complete and nothing in it may be
read as a pass.

### Task 1 - orchestration foundation (committed `c380a05`)

`ApplicationDataOrchestrator`, `OrchestrationModels` and `SnapshotCacheStore` in
`src/SystemCore.Core/Orchestration/`, registered in DI and started after
`window.Activate()` so the window appears before any Tier 2 or Tier 3 work. 57 new
assertions. Measured window appearance: 794 / 751 / 691 ms warm against a `5e21063`
baseline of 8135 / 705 / 718 ms - no regression; the 8135 ms figure is a cold file
cache, not a warm start.

The R1 safety guarantee was revised after the implementer correctly refused to
assert something it could not test. It has three honest parts: the orchestration
layer has no mutation-service dependency (tested), the producers are trusted code
whose call graphs are reviewed separately (reviewed by hand, not by test), and the
destructive services are exercised directly (tested).

### Task 2 - read-only inventory pages (implemented, committed)

Auto-load for Uninstaller, Startup, SystemInformation and Tools, routed through the
orchestrator, with the FIX 6 session-safety properties preserved: an
`inventoryActionable` gate, `Publish(..., TimeSpan.Zero)` to expire the read cache
before the executor consumes a session, and a forced refresh after every mutation.
Every producer is read-only enumeration, and the persisted cache holds counts only,
never an actionable session. Four new localization keys were added per language
(`UnLoadFailed`, `StartLoadFailed`, `ToolsLoadFailed`, `InfoRefreshFailed`); no
existing resource value was changed, which matters because `LocalizationService`'s
indexer throws on a missing key rather than degrading quietly.

10 of the 11 UI scripts pass, 801 checks. The two destructive-action assertions
("Uninstaller requires explicit selection", "Startup requires explicit selection")
were moved into `Test-Shell.ps1` and made null-safe; the same property is now
asserted in six files rather than two. `Check` lines: 23 added, 11 removed.

**`Test-ProductStability.ps1` now PASSES at 24 cycles and the earlier failures are
classified.** Two different defects were behind them, and neither was the gate's
assertions. First, the run that exited unexpectedly in the `minimized` phase used a
build carrying the D-b defect: activation invoked view-model refreshes from a
thread-pool thread, and a `PropertyChanged` raised into live XAML from that thread
wedged the UI with `Busy` set outside the `try`. Both are fixed at the source -
`OnUiAsync` in `MainWindow.xaml.cs`, and `Busy` inside the `try` at all five sites in
`UninstallerViewModel.cs` - and `App/ProviderFailure` at startup went from 2 to 0.
Second, the zero navigation events for Performance and About were the harness defect
that was only suspected then: `Test-ProductUx.ps1` calibrated the arithmetic centre of
a navigation row's reported rectangle, and for a row that owns child rows, or one
padded by the branded footer style's margins, that centre lies outside the row's hit
area, so the click selected nothing and the route could never register. The
calibration now takes the upper part of the rectangle for navigation rows and keeps
the centre for buttons and pickers, which moved only the ten navigation coordinates
and left every other calibrated control on the value that had already passed. Proven
by a click-walk through all ten routes using the gate's own `Foreground` and `Click`
primitives: 10 of 10 navigate. No route assertion was removed to make any of this go
away. The interactive-load confound is gone: the passing run completed 24 of 24 cycles
with no other build or test process running alongside it.

One intermediate run then failed at cycle 14 with a single `SettingsSaveFailed` - a
real preference write failed, so the language did not persist and the cycle's
assertion caught it. `LocalUserSettingsService` and `SettingsViewModel` are
byte-identical to `c380a05`, it was 1 failure in 27 rapid consecutive writes, and it
did not recur: the passing run recorded 47 saves and 0 failures. It is recorded as a
follow-up rather than smoothed away.

### Task 2b - refresh lifecycle and exception classification (implemented, committed)

The owner ordered a bounded fix for three findings from the Task 2 code review. D4
and D5 were deferred unless direct evidence appeared; none did.

- **Refresh lifecycle.** Shutdown, minimized/inactive, view-model disposal and an
  already-running refresh each now suppress a refresh, with separate guards. Auto-load
  and freshness refresh are untouched.
- **Exception classification.** `OperationCanceledException` is expected only when
  the lifetime token is genuinely cancelled; `ObjectDisposedException` is expected
  only when the view model is disposed or shutdown began. Anything else is recorded
  as `DiagnosticEvent.ProviderFailure` with its HResult. No path turns an unknown
  exception into a silent "could not load".
- **Activation through the orchestrator.** `OnActivated` makes one call instead of
  touching four view models. The orchestrator decides staleness, admits one run per
  dataset, and supplies its own lifetime token.

Nine new lifecycle assertions cover activation during shutdown, activation after
lifetime cancellation, a disposed view model, 24 restore cycles, stale-triggers-one,
fresh-does-not-refresh, duplicate activation, expected cancellation, and an
unexpected `ObjectDisposedException` being surfaced and recorded.

The R1 allowlist was widened to admit the new orchestrator method. That admission
was explicit and the guard was strengthened at the same time: a new assertion
requires the method to accept no forbidden mutation service, directly or as a
generic argument.

### Task 2c - load-bearing proof round (implemented, committed)

Four of the Task 2b lifecycle checks were shown by mutation testing to be
**vacuous** - they would have passed with the guard they claim to cover deleted.
They are now backed by assertions that actually fail.

**One production change.** `RefreshStaleAsync` released every in-flight mark only in
the outer `finally`, after the whole pass. One stalled dataset therefore held the
marks of the other three for the entire pass, and on a hung provider held them
indefinitely, after which activation could never refresh anything again. The
release is now per item, immediately after that dataset finishes; the outer
`finally` is kept to release items that were admitted but never reached when the
caller's token was cancelled mid-pass. Both go through `Release(dataset, ticket)`,
which removes the mark only when the ticket still matches - otherwise the first
pass's cleanup would wipe the mark a second activation had legitimately taken over.

**Four checks replaced or added.**

- `8d` proves the orchestrator's terminal `catch (Exception e)` records a failing
  caller-supplied delegate instead of discarding it. It waits on a
  `TaskCompletionSource` in the diagnostics double rather than polling a counter,
  and reports a failed check on timeout instead of crashing the harness.
- `8e` drives two concurrent passes over one dataset and asserts a third is
  refused. It waits for the hand-off to be signalled by the second action itself,
  because that hand-off needs the first pass's thread to resume and that latency
  is unbounded on a loaded machine.
- `8f`, one per page, covers the `orchestrator.IsShutdown` filter on
  `UninstallerViewModel` and `SystemInformationViewModel`, which was asserted
  nowhere before.
- `8c` now waits for the cancelled pass to actually drain instead of sleeping, so
  "the queued dataset never started" is a decision rather than a timing guess.

**Mutation matrix, 10 of 10 detected.** Deleting each guard in turn - per-item
token re-check, terminal record, in-flight dedup, ticket-matched release, per-item
release, and the user-cancel and `IsShutdown` filters on all three pages - makes at
least one assertion fail. The suite is not a set of checks that pass regardless.

### Task 2d - the orchestration filters were uncovered (implemented, committed)

A closer read showed the orchestrator's own `OperationCanceledException` and
`ObjectDisposedException` clauses were asserted nowhere. Task 2b's check named
"expected cancellation is not an error" had no diagnostics assertion at all, and its
orchestrator was built with `NullDiagnostics`, so that half of the name could not
fail. Four checks were added: a cancellation nobody asked for is recorded, a
cancellation the caller did ask for is not, a disposal of something that was never
disposed is recorded, and a refresh that reaches the disposal window is teardown
rather than a defect. The last one calls the real read on a disposed orchestrator, so
the disposal it hits is genuine.

**One speculative change was reverted.** `DisposeAsync` sets the disposed flag and
cancels the lifetime on a background task, so reading `IsShutdown` in the
`ObjectDisposedException` filter looked like the faithful reading of D2's "shutdown
began **or** the service is genuinely disposed". It was removed on measured
evidence: `IsShutdown` takes `lock (sync)`, and locking inside an exception filter
made one check fail on four consecutive runs because the record never arrived. It
was also unnecessary - every disposal path cancels the lifetime, so a genuine
teardown is already covered. The filter now discriminates on the lifetime alone,
which is what the rest of the file already does.

**Mutation matrix, 14 of 14 detected.** A filter that is deleted and a filter that can
never be true are both detected, in both directions. That is the difference between a
guard that classifies exceptions and one that hides them.

### Task 2e - gate closure: uninstaller classification and harness isolation (implemented, committed)

The four `UninstallerViewModel` exception sites left as follow-ups after Task 2d are
now fixed, together with a root-cause change that the fix required.

**Root cause found first.** `ApplicationDataOrchestrator.IsShutdown` took `lock (sync)`
(`ApplicationDataOrchestrator.cs:171`). That is the same hazard Task 2d measured and
avoided in the orchestrator's own filters, and `UninstallerViewModel` consults
`IsShutdown` in an exception filter. Rather than add a second shutdown flag to the
interface - which would have broken the R1 allowlist assertion at
`OrchestrationTests.cs:245` - the existing flag was made lock-free: `disposed` is now
`private volatile bool` and `IsShutdown => disposed`. `DisposeAsync` writes it once
under the lock at `:398`; every reader now sees that write without contending. No
public API changed, so the R1 allowlist is untouched.

**Why the shutdown signal is still needed.** `App.DisposeAsync` disposes the
orchestrator at `App.xaml.cs:178`, but the view models are disposed later in
`MainWindow.OnClosed` at `MainWindow.xaml.cs:203`. There is therefore a real window in
which the orchestrator is gone and the view model is not yet disposed, and an
`ObjectDisposedException` met in that window is expected. Dropping the flag would have
regressed the shutdown classification, so it was made cheap rather than removed.

**Classification.** `GenuineCancel(token)` and `ShuttingDown` are now the single
decision points. A cancellation is genuine only when the caller's token, the view
model's lifetime, or the orchestrator is actually cancelled. Six sites changed:

| Site | Before | After |
| --- | --- | --- |
| `OpenLauncherAsync` | bare OCE swallowed, nothing recorded | genuine cancel ignored; anything else sets `launcherFailed` and records |
| `UninstallConfirmedAsync` | bare OCE reported as a user cancel | genuine cancel is `Cancelled`; a stray OCE is `Failed` and recorded |
| `UninstallConfirmedAsync` | no ODE handler, escaped the handler | expected only while tearing down, otherwise recorded |
| `UninstallConfirmedAsync` | no terminal handler | unexpected faults are `Failed` and recorded |
| `ScanLeftoversCoreAsync` | bare OCE set state to `Removed` | genuine cancel keeps the verified removal; a stray OCE never claims a verified removal |
| `DeleteLeftoversConfirmedAsync` | bare OCE, no ODE handler | same policy as the uninstall path |

The scan case was the most serious of the six: an `OperationCanceledException` that
nobody requested reported **"Application removal verified."** to the user for an
application that was never removed. `catch (Exception)` at the scan site also recorded
nothing, so a scanner failing for real was invisible.

**`BeginOperation` is now the single choke point for late clicks.** It returns an
already-cancelled token when the view model is disposed, because `lifetime.Token` throws
after `Dispose` and a click landing during teardown would otherwise fault the
dispatcher with an unhandled `ObjectDisposedException`.

**Not changed, deliberately.** No UX or native uninstall flow changed. A successful
launch, a user cancel and a native uninstaller failure all read exactly as before; the
only visible difference is that a fault which was previously mislabelled as a user
cancel now reads as a failure.

### Task 2f - test harness isolation (implemented, committed)

Every temp root in every test project is already `Guid`-unique, registry fixtures are
`Guid`-named, and the orchestrator's heavy-provider permit is an instance field, not a
static. There is therefore **no shared mutable resource** between two concurrent runs
of a suite. The two intermittently failing checks documented after Task 2d are
load-sensitive by construction: they wait on a record delivered by a detached
`Task.Run`, and two copies of the same suite competing for the same cores make a
healthy observation look failed.

The fix chosen is serialized execution, not per-run isolation, because there is nothing
left to isolate. `tests/SystemCore.UninstallerTests/Program.cs` now takes a named
cross-process `Semaphore` (`SystemCore-Suite-UninstallerTests`) for the whole main
suite, with a bounded wait. A `Semaphore` rather than a `Mutex` because the suite spans
`await` points, so release can happen on a different thread than acquisition and
`ReleaseMutex` rejects that - a defect found by running the gate, not by reading it. The
argument modes return before the gate is taken, so the child process the
parent-uninstall case spawns never waits on a lock its own parent holds.

No product code was changed for this.

### Task 2g - stability harness reporting (implemented, committed)

`Test-ProductStability.ps1` discarded the evidence needed to classify its own failure.
On the failure path `$p` was disposed in the `finally` without ever reading `ExitCode`,
so an unexpected application exit was reported only as "exited unexpectedly" with no
code, no stage and no stderr. It now captures the exit code before disposal and writes
`stability-report.json` on both paths, carrying duration, cycles, exit code, whether the
application crashed, the last stage, memory start/end/peak, handles start/end, threads
start/end, the final responding state, and an inventory of every per-launch stderr file
with its tail. Navigation verification now counts events per route before asserting, so
a route that never registered reports how many events it produced instead of only
falling below the bound.

One correction worth recording: the failure message "Memory Tools app exited
unexpectedly" is a literal string inherited from `Test-MemoryToolsStability.ps1:30`. It
is not evidence that the Memory Tools page was involved. The application did exit
during the minimized phase, but nothing about the message identifies a route.

### Verification

| Check | Result |
| --- | --- |
| Debug build, full solution | 0 errors, 0 warnings |
| Release build, full solution | 0 errors, 0 warnings |
| `SystemCore.UnitTests` (orchestration) | 59 / 59 |
| `SystemCore.UninstallerTests` | 345 / 345, exit 0, including 5 new `BusyFaultSafety` checks written before the D-a fix (RED `{"FinalChecks":345,"Failed":5}`, then GREEN `{"FinalChecks":345,"Failed":0}`) |
| `SystemCore.SecurityTests` | 56 / 56, 1000 fuzz inputs |
| `SystemCore.StartupTests` | 80 / 80 |
| `SystemCore.MemoryToolsTests` | 69 / 69 |
| `SystemCore.ProductTests` | 117 / 117 |
| `SystemCore.ReliabilityTests` | 16 / 16 |
| `git diff --check` | clean (CRLF normalization notices only, pre-existing) |
| `SystemCore.SystemInformationTests` | exit 0, no failures, via `scripts/Build.ps1` |
| `SystemCore.CompatibilityTests` | PASS, via `scripts/Build.ps1` against the canonical `artifacts/publish/App` |
| `SystemCore.F1ParentTests` | NOT APPLICABLE - requires an already-elevated validation controller and three arguments by design; it is an elevation probe, not a regression suite |
| x64 publish | PASS via `scripts/Build.ps1`; `F1.exe`, `F1.pri`, `F1.runtimeconfig.json` and `ElevatedHost/SystemCore.ElevatedHost.exe` all present |
| `Test-ProductStability.ps1` | **PASS at 24 cycles**, gate exit 0, `GateReasons` empty. 573.3 s; `AppExitCode` 0, `MainExitCode` 0, `RestartExitCode` 0, `AppCrashed` false, `RespondingEnd` true; 70 verified checks including all 10 routes, 24 theme and 24 language verifications, both normal closes, the restart and the counter collector. 0 `ProviderFailure`, 0 `UnhandledFailure`, 0 unobserved tasks, 0 audit overflow, no unexpected audit boundaries, no bad audit files. Private memory 168.7 MB start to 155.9 MB end (298.3 MB peak), handles 1486 to 1469, threads 70 to 69. `NavigationCounts` 24 on nine routes and 23 on Performance against a required `Cycles-1` of 23. `LeftoverExitCode` reads NOT AVAILABLE, which on a clean run means the app never exited unexpectedly. |
| 10 UI scripts | not re-run; last known result 801 checks green |

Two checks - "a disposal of something that is not disposed is recorded as
unexpected" and the information-page disposal check - failed twice, three runs each,
while another process was building and running the same suites on the same machine.
The source was unchanged between the failing and passing runs, and with tracing in
the refresh loop the record is observed arriving within milliseconds. The mechanism
was not pinned down. These two checks wait on a record delivered by a detached
`Task.Run`, so they are timing-sensitive by construction; making them deterministic
would need a completion signal from the orchestrator, which is a production API
change and out of scope here. This is stated rather than reported as a clean result.

### Changed files for Task 2 / 2b

- `src/SystemCore.Core/Orchestration/OrchestrationModels.cs` (`DatasetRefreshRequest`, `RefreshStaleAsync`)
- `src/SystemCore.Core/Orchestration/ApplicationDataOrchestrator.cs` (`RefreshStaleAsync`, `refreshing` set)
- `src/SystemCore.App/MainWindow.xaml.cs` (shutdown flag, activation lifetime, single orchestrator call)
- `src/SystemCore.App/Uninstaller/UninstallerViewModel.cs`
- `src/SystemCore.App/Startup/StartupViewModel.cs`
- `src/SystemCore.App/Tools/ToolsViewModel.cs`
- `src/SystemCore.App/SystemInformation/SystemInformationViewModel.cs`
- `tests/SystemCore.UninstallerTests/InventoryAutoLoadTests.cs` (new lifecycle suite; diagnostics double now records)
- `tests/SystemCore.UnitTests/OrchestrationTests.cs` (R1 allowlist admission plus the new parameter assertion)

### Open follow-ups

- **D4** - `StartupViewModel.Cancel()` can race `operation.Dispose()` in `LoadAsync`'s
  `finally`, raising `ObjectDisposedException` on the UI thread. Recorded only; not
  verified against the original code, so it may be pre-existing.
- **D5** - `MainWindow.StartupTimeline` is public mutable static state.
- **`SystemInformationFreshness` is 2 minutes** on an activation-triggered path. The
  remediation brief listed four suppression conditions and a constant change was not
  among them, so the value is unchanged and still recorded as a candidate
  contributor to the stability failure. Not verified either way.
- **`ToolsViewModel.StartLoad`'s `disposed` guard is redundant** with the guard at
  the top of `LoadAsync`. Deleting either one alone is not detectable by the suite,
  because the other still blocks. Both are kept as cheap defence in depth; this is
  stated rather than left to look covered.
- **`SystemInformationViewModel` still contains `if (!value) cancellation?.Cancel();`**
  introduced by Task 2 rather than by the D1/D2/D3 work. Out of scope, reported only.
- **CLOSED - `UninstallerViewModel` exception classification.** The five sites previously
  listed here are fixed in Task 2e and covered by eight new checks, including the
  genuine-cancellation, stray-cancellation, shutdown-disposal, unexpected-fault, native
  failure and UAC-cancel cases. Two of the new checks fail if the old classification is
  restored, and removing the terminal handler crashes the harness with an escaping
  `OperationCanceledException`, so all three directions are load-bearing.
- **Transient preference-save failure.** One intermediate gate run recorded a single
  `SettingsSaveFailed` at cycle 14: `LocalUserSettingsService.Save` swallowed a storage
  failure into a `Trace.TraceWarning` and the language did not persist. It was 1 failure
  in 27 rapid consecutive writes, the store is byte-identical to `c380a05`, and it did
  not recur (47 saves, 0 failures in the passing run). The swallow is deliberate, so this
  is recorded as an observability follow-up - a user losing a preference this way gets no
  durable trace - not as a defect fixed here.
- **The gate pins a window size the application resizes itself past.**
  `MainWindow.xaml.cs:98` calls `AppWindow.Resize` to 1280x900 during startup, while
  `Test-ProductStability.ps1:24` pins 1120x760. Whichever lands last wins, and at
  1280x900 every calibrated coordinate is wrong and all ten routes read 0 events. The
  gate's elevation check happens to delay its resize past the application's, so this
  currently works by ordering rather than by assertion. Verified by measuring row
  geometry at both sizes; not changed here because the assertion belongs to the gate.
- **The two destructive-action assertions do not verify that the control exists.** An
  independent check flagged the null-safe rewrite of `Uninstaller requires explicit
  selection` and `Startup requires explicit selection` as a relaxation. It is not: with
  no `Set-StrictMode` in any of the three scripts and `Find` built on
  `AutomationElement.FindFirst`, a missing control yields `$null`, property access on
  `$null` yields `$null`, and `!$null` is already true at `c380a05`. Both forms were
  measured across all three reachable states - absent, present-and-disabled, and
  present-and-enabled - and agree in every one, including failing in the dangerous case
  where a destructive control is enabled without explicit selection. The pre-existing
  gap is that neither form asserts presence, so a control that failed to render would
  pass. Strengthening it to require presence is undecided and deliberately not done here.
- **`LeftoverExitCode` reads NOT AVAILABLE on a clean run.** It is only populated when
  the application exits unexpectedly, so its absence is the healthy value, but in a
  report it reads like missing evidence.
- **`Test-ProductUx.ps1` receives its expected version as a parameter.** The SDK appends
  `+<SourceRevisionId>` to `AssemblyInformationalVersion` for any build inside a git work
  tree, so a literal expected version cannot match. The tracked script stays
  parameterised and the run used a wrapper that derived the expected value from the
  binary under test. Whether the script should derive it itself is undecided.