# Pre-release fixes 01 — Sidebar / Startup

Scope: FIX 1 dead-code cleanup and FIX 2 only. Version remains `0.9.0-dev`.
No Phase 10, signing, installer finalization, deployment, or real startup mutation.

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

## Verification

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
