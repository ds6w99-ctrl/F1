# Pre-Phase 10 verification harness

Scope: test scripts and verification evidence only. No product source, UX, resources,
Cleaner implementation, recovery paths or schemas changed. FIX 4 remains commit
`77347a5`. No Phase 10 work or push is authorized.

## Method

- Real-device scan is read-only. The previous 180-second speed gate was unsuitable
  for variable user data and machine load. Observed runs were 195.4 seconds for
  123,874 files, 279.3 seconds for 122,259 files, and 147 seconds for 121,617 files.
  These are different datasets/load conditions, not evidence of a product regression
  or a valid comparison against FIX 3.
- A 900-second operational watchdog bounds the real scan. It reports an unfinished
  scan, not proof of a deadlock. No throughput threshold is imposed. Cancellation
  retains a separate 30-second responsiveness bound.
- `Test-CleanerRealScan.ps1` records elapsed time, eligible file count, approximate
  bytes, eligible files/second, process CPU milliseconds and one-core-equivalent CPU
  percentage, final working set and private bytes. Bytes are reconstructed from the
  rounded UI size and explicitly marked approximate, not exact. Memory is a final
  process sample, not a peak. Zero eligible files is a valid parsed result.
- `Test-CleanerBenchmark.ps1` uses the existing separately compiled validation app:
  111 files / 147 bytes, five scans, ten seconds maximum per scan, five cancellation
  probes with a 30-second bound. It checks actual summary counts, not only the fixture
  preparation marker, and requires cancellation to have been invoked. This is a
  small end-to-end regression guard including UIA/dispatch overhead and the fixture's
  150ms delay, not an isolated scanner throughput benchmark or a hardware-independent
  performance guarantee. No soak and no parallel build/test execution.

## Artifact selection and native readiness

- Explicit paths are validated. Discovery searches only canonical publish, current
  artifacts and app build output, ranking by managed payload timestamp rather than
  the reusable native apphost timestamp. It requires F1.exe, F1.dll, F1.pri and runtime
  configuration, product identity/version and the correct production/validation marker.
- Historical phase directories are excluded from discovery. Theme localization accepts
  parameters; callers no longer rewrite its source to substitute an old phase path.
- Native calibration waits for a window, requested dimensions, expanded parent,
  visible child navigation and the expected page title. The focused follow-up replaces
  heuristic mouse positioning with native Enter on an exactly focused UIA element;
  mouse hit-test coverage is not claimed. No historical coordinates file is required.
- One final calibration attempt failed at child visibility after navigation; the
  original immediate lookup had not waited for that condition. A bounded visible-child
  poll was added with readiness diagnostics. Subsequent result is recorded below.

## Verification evidence

Sequential checks before the final harness review:

| Check | Result |
| --- | --- |
| Locked restore | exit 0 |
| Debug build | exit 0; 0 warnings/errors |
| Release build | exit 0; 0 warnings/errors |
| x64 production publish | exit 0 |
| Localization | 12/12 |
| UI Smoke | 118/118 |
| Synthetic benchmark | 18/18 |
| Real-device scan | 8/8; 147s; 121,617 eligible files; cancellation 0.6s |
| Native calibration retry | 16/16 |
| Product UX | exit 0; 305/305 JSON assertions |

Post-review rechecks: localization exit 0; synthetic benchmark 18/18, median 0.565s;
real scan 8/8, 207.9s, 121,703 eligible files, approximately 8.9 GB, 585.4 files/s,
197,687.5 CPU ms, 238.5 MB private memory; cancellation 0.6s.

Native calibration also had an intermittent native-click failure after child readiness
passed. A diagnostic rerun passed both languages at screen position (195,295), with the
app owning the foreground before/after the click. This does not establish the cause of
the earlier failure. Foreground ownership is now an explicit bounded prerequisite;
no click retries or UIA substitute are used to hide failure. Native mouse calibration
requires an uninterrupted interactive desktop. **Final foreground-gated check FAILED**:
English passed, but after switching to Arabic the Performance parent existed while
the Memory child remained absent for 20.016 seconds. Thus neither a longer launch
wait nor foreground ownership alone establishes stable calibration. The underlying
navigation/expansion state still needs investigation; do not claim this gate ready.
No further blind retries, product edits or assertion relaxation were performed.

This historical blocker is investigated separately in
`PRE-PHASE10-NAVIGATION-BLOCKER.md`. The focused correction establishes expansion
before lookup and replaces unchecked mouse calibration with native keyboard
activation (explicitly not mouse hit-test coverage). See that report for the
controlled reproduction, evidence limitations and final gate status.

Results are stored in
`artifacts/current/theme-localization`, `cleaner-benchmark`, `cleaner-real-scan`,
`calibration` and `pre-phase10/{smoke,product-ux}`.

## Visual review package

`artifacts/current/ui-review/index.html` and `index.json`: ten distinct 1280x860 PNGs
for Dashboard, Cleaner, Uninstaller, Startup, Performance, Memory, System Information,
Tools, Settings and About, Arabic Dark, captured from the post-FIX-4 app. The manifest
records the binary hash and version. Artifacts are intentionally gitignored.

Image decoding, dimensions and distinct hashes were checked; these do not establish
visual correctness. The delegated visual review hit a provider rate limit; no paid
fallback was used. A later focused English/Arabic Memory direction review was performed
directly (see the navigation blocker report); this does not approve the full package.
Visual acceptance remains **WAITING FOR USER VISUAL REVIEW**.
