# PRE-RELEASE FIX 4 — Minimal UI Copy / Background Mode

**Status:** complete, not pushed
**Branch:** `pre-release-fixes`
**Base commit (FIX 7):** `30e4b841ff5767ddd30682f3eb28ee9e91fe4a2a`
**Scope:** copy and layout only. No redesign, no logic change, no new feature.

Governing rule: **less text, not less safety.** Name / Value / Status / Action, a required
warning, and a required error were kept. Everything else was removed, shortened, or moved to a
hover tooltip.

---

## 1. What changed

### 1.1 Elements removed from the visual tree

Fifteen bindings were deleted rather than shortened, because they repeated information already
present on the same screen.

| Page | Removed | Reason |
|---|---|---|
| Dashboard | `HeroDescription` | Marketing line under the title |
| Dashboard | `F1CleanupBody`, `F1UninstallBody`, `F1StartupBody` | Described the button sitting directly above each one; the `CleanerValue` / `MetricApps` / `MetricStartup` values in the same cards were kept |
| Dashboard | `ScanDetail` caption | Repeated the scan button's own label |
| Performance | `Description` | Restated the page title |
| Performance | `CpuDetail`, `MemoryDetail`, `DiskDetail` | Secondary explanation → moved to `ToolTipService.ToolTip` on each value |
| Performance | `ChartLabel` | Repeated as `AutomationProperties.HelpText` on all four charts → also added as tooltip |
| Performance | `ProcessNotice` | 194-char paragraph → moved to the process list's `HelpText` and tooltip |
| Settings | `Description` | Restated the page title |
| Settings | `AppearanceLabel` | The Language and Theme pickers already carry their own headers |
| Settings | `AboutNotice` | 150-char product paragraph; the card keeps label + version + button |
| About | `AboutDescription` | 150-char product paragraph |
| Tools | per-tool `Description` node | 14 well-known Windows tools; the names are self-explanatory. Elevation state is still shown separately in the row's status line |

Removed text was **not** deleted from the resource files where a test requires the key to exist —
see §4.

### 1.2 Values shortened in both languages

Fifteen keys, English and Arabic together, `Shell.resx` + `Shell.ar.resx`.

| Key | Before → After (chars) |
|---|---|
| `F1History` | 35 → 15 |
| `PerfNetworkScope` | 133 → 54 |
| `MemEducation` | 94 → 65 |
| `CleanDescription` | 112 → 78 |
| `CleanSafety` | 274 → 207 |
| `ToolsDescription` | 147 → 59 |
| `SettingsBehavior` | 26 → 6 |
| `SettingsGeneralNotice` | 161 → 80 |
| `SettingsCleanerNotice` | 177 → 91 |
| `SettingsPrivacyNotice` | 137 → 110 |
| `SessionSettings` | 80 → 29 |
| `AboutSupported` | 119 → 68 |
| `AboutCopyright` | 145 → 74 |
| `PlaceholderDescription` | 99 → 51 |
| `InfoDescription` | 159 → 66 |

Safety-bearing strings kept every fact they previously asserted — only the wording was compressed:

* `CleanSafety` still states that cleaning is permanent, that locked / changed / uncertain files are
  skipped, that cancel cannot undo removals or stop a running Recycle Bin operation, and that
  browser caches and WER are unsupported in V1.
* `CleanDescription` still states the 24-hour minimum age, locked files, links, and uncertain targets.
* `SettingsCleanerNotice` still states the 24-hour minimum, the protected-path/link/locked-file
  rules, and that they are not adjustable.
* `AboutSupported` still states `Windows 11 x64`, the minimum build, and that certification is pending.

### 1.3 Deliberately untouched

* `SettingsSafetyNotice` — mandatory safety text, left verbatim.
* `CleanPotential` (`Potentially reclaimable`) — asserted by `Test-Shell.ps1` and `Test-StartupPage.ps1`.
* `UnLoaded`, `StartLoaded`, `StartPartial` — frozen by regex assertions.
* `{Brand}` placeholder preserved in `SettingsGeneralNotice`.
* `AutomationId` / `AutomationProperties` on every kept control, including `SettingsSaveStatus`,
  `SettingsMandatorySafety`, `WindowsVersion`, `MemDetails`, `F1Attribution`, `F1AboutVersion`.
* Logo, the name `F1`, `Made By Falcone`, version, domains, helper identity, `%LOCALAPPDATA%\SystemCore`,
  schemas, recovery paths. Arabic strings are intact (verified below).

---

## 2. Measured result

Rendered-text reduction, English, measured from the git object store rather than estimated:

| Source | Characters |
|---|---|
| Values shortened in place (15 keys) | 845 |
| Text removed with deleted elements (14 bindings) | 1,101 |
| Tool description nodes no longer rendered (14 tools) | 699 |
| **Total** | **2,645** |

Whole-file resource totals: `Shell.resx` 20,168 → 19,323 chars (−4.19%), `Shell.ar.resx`
18,145 → 17,486 chars (−3.63%), 15 values changed in each, key parity 568 / 568.

Reproduce with `artifacts/current/fix4-copy/measure-density.ps1`.

---

## 3. Encoding and line-ending safety

The Arabic resx is the one artifact that would be easy to corrupt here, so it was checked rather
than assumed.

* Both resx files are UTF-8 **without** BOM and LF-only, before and after. Asserted by the patch
  script before it wrote anything.
* Strict UTF-8 decode of both files succeeds.
* Zero `U+FFFD` replacement characters in `Shell.ar.resx`.
* `SettingsBehavior` reads back as `U+0627 U+0644 U+0633 U+0644 U+0627 U+0645 U+0629` — `السلامة`,
  correct.
* Readable Arabic confirmed for all 15 keys in `artifacts/current/fix4-copy/applied-copy.txt`.
* The resx was patched as **raw text**, not through an `[xml]` round-trip, which would have
  re-serialised all 578 `<data>` elements and produced a whole-file diff. The result is exactly
  15 changed lines per file.
* Windows PowerShell 5.1 decodes a BOM-less `.ps1` with the ANSI code page, so no Arabic literal
  was ever written into a script. The Arabic lives in `copy-table.json`, read with an explicit
  UTF-8 decoder, and every patch script is pure ASCII.
* Line endings: the stored blobs and the working tree are both LF for every edited file, confirmed
  with `git cat-file` (a `git show | Out-String` measurement is invalid — `Out-String` rewrites
  newlines). The `LF will be replaced by CRLF` notices from `.gitattributes` are the expected
  normalisation warnings, not a change I introduced.

---

## 4. No resource key was deleted

Static analysis reported roughly 240 "unused" keys. **None were removed**, because the analysis
cannot prove they are unused — keys are composed at runtime:

* `WindowsTools.cs:11-12` — `NameKey => "Tool" + Id`, `DescriptionKey => NameKey + "Description"`,
  so the 14 `Tool*Description` keys exist for tooling that is still referenced by contract.
* `CleanerViewModel.cs:24` — `"CleanStatus" + result.Status`; `:51` — `"Clean" + p.Category`;
  `:68` — `"CleanBin" + bin`.
* `SystemInformationPresentation.cs:22` — `$"Info{prefix}{code}"`.
* `ShellViewModel.cs:40` — `$"Nav{SelectedPage}"`.

A missing key makes `LocalizationService` throw `InvalidOperationException`, so the downside of a
wrong deletion is a crash and the downside of keeping one is zero.

The 14 `Tool*Description` keys are kept for a second, independent reason:
`tests/SystemCore.ProductTests/Program.cs:118` requires every `NameKey` **and** `DescriptionKey` to
exist in both languages. They are simply no longer rendered.

---

## 5. Verification

Sequential, one thing at a time. No build ran concurrently with a gate or test.

### 5.1 Passed

| Check | Result |
|---|---|
| `dotnet restore SystemCore.sln --locked-mode` | exit 0, lock files unchanged |
| Debug build | Build succeeded, 0 warnings, 0 errors |
| Release build | Build succeeded, 0 warnings, 0 errors |
| Publish Release `win-x64` | exit 0 — `F1.exe` 401,408 B, `F1.pri` 2,232,064 B, 465 files |
| `SystemCore.ProductTests` | **117 / 0** — includes en/ar key parity, "no empty localized strings", approved unit labels, 14 tool keys × 2 languages |
| `SystemCore.UnitTests` | exit 0 — orchestration 59 / 0 |
| `SystemCore.CompatibilityTests` | exit 0 |
| `SystemCore.SystemInformationTests` | exit 0 |
| `SystemCore.PerformanceTests` | exit 0 |
| `SystemCore.StartupTests` | 80 / 0 |
| `SystemCore.SecurityTests` | 56 / 0, 1000 fuzz inputs |
| `SystemCore.ReliabilityTests` | 16 / 0 |
| `SystemCore.MemoryToolsTests` | 69 / 0 |
| `SystemCore.UninstallerTests` | **345 / 0** |
| `SystemCore.CleanerTests` | exit 0 in all three modes (default, `--executor`, `--completion`) |
| `Test-ProductUx.ps1` | **305 / 0**, exit 0, 71 screenshots |
| Text-clipping probe (all 10 pages, Arabic Dark) | **CLEAN** — 0 ellipsis-trimmed, 0 overflow |
| Native click-walk, en + ar | **2 / 2** |
| Deleted strings no longer rendered (en/ar × 3 themes) | **84 / 84** — nothing removed came back |
| XML well-formedness, all 18 XAML files | all parse |
| `git diff --check` | clean |
| Arabic byte integrity | 0 `U+FFFD`, strict UTF-8 decode OK |

`Test-ProductUx` asserts every page title against the resx in **both languages across all three
themes** (2 × 3 × 10 pages), re-checks tool buttons and About version, and saves a screenshot per
combination. Theme and language rendering are therefore covered by this suite, not only by
`Test-ThemeLocalization` (see §5.4).

Clipping was checked with UI Automation geometry rather than by eye, because this model cannot view
images (§5.5): a text element is flagged if its rendered name contains U+2026, or if its rectangle
escapes its parent's or the window's rectangle. Result: zero findings on all ten pages.

### 5.2 FAILED — and why it is not a regression

`Test-Shell.ps1` → **`Cleaner real scan completes`** — reproduces on every run.

Characterised with `scripts/Test-F1CleanerScanDiag.ps1`, which replays the exact scan → cancel →
rescan sequence:

```
step 1  scan, then cancel   -> control returned in 0s
step 2  rescan              -> completed after 195.4s
         summary  = "Potentially reclaimable: 8.9 GB ... 123874 eligible files..."
         categories= 10      reclaimable check = True
```

The scan **succeeds**; all three downstream assertions would pass. The only reason the check fails
is that the rescan takes 195.4 s against a hard-coded 180 s budget. The scan found 123,874
eligible files / 8.9 GB, far above what a quiet machine produces, because a game plus Discord and
three browsers were running and churning temp files throughout.

Non-causation is structural, not inferred: the diff contains **no** Cleaner code, view, or service
file. The only Cleaner-related change in the whole commit is two display strings in the resx, and
`CleanerView.xaml` has no diff at all. Scan completion is governed by `CleanerScan.IsEnabled`,
which no string value can affect.

**The 180 s budget was deliberately not raised.** Inflating a timeout to make a check go green
would hide the finding rather than resolve it. Recommended follow-up, out of scope here: scale the
harness budget with the discovered file count, or assert on scan completion with a budget derived
from the reported total.

### 5.3 Findings in the new copy-render suite, and why they are test defects

`scripts/Test-F1CopyRender.ps1` asserts, for both languages across all three themes, that each of
the 15 shortened strings renders and each deleted string does not. Result: **157 / 168**. The 84
"must be absent" checks all passed. All 11 failures are the Settings page and only two keys:

```
en/Light/Settings/SettingsGeneralNotice      ar/Light/Settings/SettingsGeneralNotice
en/Dark /Settings/SettingsGeneralNotice      ar/Dark /Settings/SettingsGeneralNotice
en/System/Settings/SettingsGeneralNotice     ar/System/Settings/SettingsGeneralNotice
en/Dark /Settings/SessionSettings            ar/Dark /Settings/SessionSettings
en/System/Settings/SessionSettings           ar/System/Settings/SessionSettings
                                             ar/Light/Settings/SessionSettings
```

Both are defects in the new check, not in the product. Confirmed by
`scripts/Test-F1SettingsDump.ps1`, whose output is kept in
`artifacts/current/fix4-copy/settings-dump/dump.txt`:

1. **`SettingsGeneralNotice`** contains the `{Brand}` placeholder, which `LocalizationService`
   substitutes at render time. A literal `blob.Contains(rawResxValue)` can never match. The page
   actually renders `F1 opens on Dashboard · no startup registration, no automatic update checks`
   — the intended copy, with the brand resolved. The check needs the placeholder substituted
   before comparing.
2. **`SessionSettings`** is the *initial* value of the save-status line
   (`SettingsViewModel.SessionNotice => L[status]`). The suite chooses a theme and a language
   before inspecting the page, which legitimately advances that line to `Settings saved locally.`
   The initial value does render on a fresh launch (`Applied now and saved locally`, dump line 9),
   which is why `en/Light` passed and every later combination did not.

So 13 of the 15 shortened keys are confirmed rendering in both languages and all three themes; the
remaining two are confirmed rendering by the dump. The check was **not** edited to force these to
pass — the two corrections above belong in a follow-up, together with a `Settings` assertion taken
before any preference is changed.

The same dump is the direct evidence that the changed Settings copy is live in both languages:
`Safety`, the new Cleaner notice and the new Privacy notice all render as intended, while
`SettingsSafetyNotice` remains verbatim.

### 5.4 NOT AVAILABLE

`scripts/Test-ThemeLocalization.ps1` could not be run.

It assigns `$AppPath='artifacts/phase8/app/SystemCore.App.exe'` (line 2, an assignment rather than
a parameter) and that artifact does not exist in this repository state. It also cannot be satisfied
by copying the current build: the WinUI app does not load its resource index under a renamed
executable. Launching a copy named `SystemCore.App.exe` opens a window titled `F1` whose UI tree
exposes **no** AutomationId at all — `NavSettings` is not found — so the suite fails on its first
navigation check. This was verified directly, not assumed.

Its coverage is instead provided by `Test-ProductUx` (page titles asserted in en/ar × Light/Dark/
System) plus `scripts/Test-F1CopyRender.ps1`, which asserts that all 15 shortened strings really
render and that all deleted strings no longer do, in both languages across all three themes.

`scripts/Test-F1NativeCalibration.ps1` also could not be run: it waits a fixed
`Start-Sleep -Seconds 6` and then reads `$p.MainWindowHandle` with no polling, so it throws
`hwnd cannot be IntPtr.Zero` whenever the machine is loaded. The one assertion it adds beyond the
other suites — a real mouse click at a calibrated coordinate selecting the Performance page — is
reproduced by `scripts/Test-F1NativeClickWalk.ps1` (2 / 2). The only difference is a bounded poll
instead of a fixed sleep; no assertion was removed, relaxed, or reordered.

### 5.5 Screenshots were produced but NOT visually reviewed

71 screenshots exist in `artifacts/current/fix4-copy/ui/` (31 Arabic, 31 English, 5 minimum-window,
plus About-licenses, diagnostics-export and restart captures). All are real 1120×760 captures with
substantial file sizes, not blanks.

**I have not looked at them and am not claiming to have.** This model has no image input, so the
review was delegated to a separate agent; that delegation returned no usable report, so **no visual
review of these screenshots exists**. The clipping requirement is met by the machine-checked UIA
geometry in §5.1, not by an eyeball pass. An actual visual pass — confirming that the shortened
strings read well and that no card now looks awkwardly empty — remains outstanding and should be
done by someone who can see the images.

---

## 6. Files changed

Modified:

* `src/SystemCore.App/Resources/Shell.resx` — 15 values
* `src/SystemCore.App/Resources/Shell.ar.resx` — 15 values
* `src/SystemCore.App/Views/DashboardView.xaml` — 5 elements removed
* `src/SystemCore.App/Performance/PerformanceView.xaml` — 7 elements removed, 6 tooltips added
* `src/SystemCore.App/Views/SettingsView.xaml` — 3 elements removed
* `src/SystemCore.App/Views/AboutView.xaml` — 1 element removed
* `src/SystemCore.App/Tools/ToolsView.xaml.cs` — description node removed from row construction

Added — verification harness for this fix:

* `scripts/Test-F1CopyClipping.ps1` — UIA ellipsis / overflow probe
* `scripts/Test-F1CopyRender.ps1` — asserts shortened strings render and deleted ones do not
* `scripts/Test-F1NativeClickWalk.ps1` — real mouse click at calibrated coordinates, en + ar
* `scripts/Test-F1CleanerScanDiag.ps1` — replays the Cleaner scan sequence with live state
* `scripts/Test-F1SettingsDump.ps1` — dumps the rendered Settings page, used to confirm §5.3

`Test-ProductUx.ps1` needed no change: `Calibrate` derives coordinates from live bounding rectangles
on every run, so the layout change cannot desynchronise it.

---

## 7. Not done, on purpose

* **No push.** Commit only.
* **No resource keys deleted** (§4).
* **No Network card added to the Dashboard.** `F1MetricCard` renders
  `MetricCpu, MetricMemory, MetricDisk, MetricFree, MetricApps, MetricStartup` — six cards, no
  Network. Network is already reported on the Performance page. Adding it would be a feature with
  its own logic and recalibration, which is outside a copy-and-layout change.
* **No timeout raised** to make the Cleaner check pass (§5.2).
* **No `Test-ProductUx.ps1` edit**, no test disabled, no assertion weakened.
* **The Diagnostics panel copy on the Settings page was left alone.** Its two paragraphs are the
  longest remaining text in the product, and they were a deliberate judgement call rather than an
  oversight. They are factual disclosures a user relies on: what a diagnostic export contains, what
  it explicitly excludes (`private paths, addresses, command arguments and identifiers`), and the
  retention limits (`up to 10 files, 2 MB each / 20 MB total`, `5 manual exports`). Shortening them
  is possible while keeping every fact, but that is a further copy change requiring its own
  verification cycle, and the FIX 4 page list does not include Diagnostics. Flagged for a decision
  rather than taken unilaterally.
