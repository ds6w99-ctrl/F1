# F1 — Gaming / Premium implementation report

Development version: **0.9.0-dev**. Attribution: **Made By Falcone**.
Authority: the supplied `artifacts/f1-ui/specification.md` and
`docs/design/reference/f1-approved-reference.png`.

## Runnable development build

Launch `artifacts/f1-ui/publish/F1.exe` from the repository, or run:

```powershell
& .\artifacts\f1-ui\publish\F1.exe
```

Keep the complete publication directory together. It includes the self-contained
runtime, WinUI resources, Arabic satellite, local artwork and freshly published
`ElevatedHost/SystemCore.ElevatedHost.exe`. This is an unpackaged x64 development
publication. The manifest remains `asInvoker`.

The accepted Phase 9 publication remains at `artifacts/phase9/app/SystemCore.App.exe`.
Its historical evidence and Development PASS / owner-review PENDING / Release
PENDING status have not been replaced by this task.

## Gates

Final machine-readable evidence: **`artifacts/f1-ui/summary.json`**.
Final image geometry, source/publication fingerprints, preference restoration and
process-cleanup reconciliation: `artifacts/f1-ui/test-results/delivery-review.json`.

| Gate | Result | Scope |
|---|---|---|
| Build | **PASS** | Locked restore, zero-warning Debug/Release, x64 App/helper publication |
| Runtime & Regression | **PASS** | 1,739 assertions + 1,000 malformed inputs; final six-cycle native run |
| Branding & Data Compatibility | **PASS** | Development scope; resources, preferences, recovery formats, helper-parent identity |
| All-Pages UI Coverage | **PASS** | Ten pages, dialogs/flyouts, AR/EN, System/Light/Dark, 100% DPI |
| Visual Reference | **PENDING** | Missing clean photographic mountain asset; disclosed differences below |
| Owner Review | **PENDING** | No owner visual acceptance inferred |
| Release / Production | **UNCHANGED** | Not approved by this UI task |

The owner authorized the disclosed temporary original SVG background. There are
**96 primary runtime screenshots + 36 populated-state screenshots**, with matching
delivery assembly hashes. Phase 9 remains Development PASS / owner review PENDING /
Release PENDING; Phase 10 has not started.

## Implemented presentation

- Shared theme dictionaries, typography, surfaces, native inputs, actions, lists,
  popups and default-Cancel destructive dialogs.
- Stable left navigation in both languages; Arabic content RTL and technical
  values LTR. Memory remains nested under Performance and available in page search.
- Native caption controls, title dragging, maximize/restore, Alt+F4, Settings action
  and bounded route search. About is available from the Premium identity and Settings.
- Real Dashboard CPU / RAM / system-volume-capacity rings with bounded measured
  histories and explicit unavailable readings. The existing shared monitor is used;
  no additional timer or sampler was introduced. Disk capacity history is separate
  from aggregate disk I/O history.
- Three real Dashboard actions: read-only Cleaner scan, Uninstaller navigation,
  Startup navigation. Actual inventory counts and last-scan state replace reference
  sample data; readiness does not claim security or device health.
- All operational pages use the same surfaces and controls, retaining bounded row
  pools, selection identities, cancellation, mandatory confirmations and native actions.
- Settings preserve System/Light/Dark and language. Unknown preference fields survive.
  Data roots, recovery formats, backups, IPC and helper technical identity are stable.

The file/command map is in [the plan](F1-UI-IMPLEMENTATION-PLAN.md), resource values
in [design tokens](F1-DESIGN-TOKENS.md), asset provenance in
[F1 assets](F1-ASSETS.md), and naming details in
[rebrand compatibility](F1-REBRAND-COMPATIBILITY.md).

## Page coverage

Runtime page/theming evidence is collected from the ordinary published F1 build.
The screenshot matrix uses `ar-Dark-*`, `en-Light-*` and the other four language/theme
combinations under `artifacts/f1-ui/screenshots/`. Each entry in `metadata.json`
records page, language, theme, client pixels, DPI, version, UTC and assembly SHA-256.

The primary capture is a **1360×1000 outer window**, with **1344×992 client pixels**
at **96 DPI / 100%**. Minimum-size captures are **900×600 outer / 884×592 client**.
`screenshots-populated/` supplements initial/unscanned states with a completed
read-only Cleaner scan, real Uninstaller and Startup inventories, selected details,
and longer Performance/Memory histories. Selection alone performs no mutation.
The populated captures verify that scan categories and real inventories exist before
capture. Final screenshot capture copies the actual foreground window rectangle;
it does not call PrintWindow or force a restore on each capture. Scroll-bottom images
are intentionally framed on the review/details/consumer sections.

### Visual comparison review

`comparisons/side-by-side.png` places the cropped reference on the left and actual
runtime on the right. `overlay-50-percent.png` and `difference.png` expose the
remaining differences. The reference crop is `(44,49,1360,1000)`; the runtime crop
removes only the 8px invisible Windows side/bottom resize gutters. The method is in
`comparisons/method.json`; raw images are retained and nothing inside either frame
is masked. No similarity percentage is claimed.

Visual review confirms the left rail, Hero, three equal metric cards, action row,
status strip and blue/purple/teal assignment. The content/card columns are close to
the reference geometry. The ring shapes follow measurements, so arc length and
history necessarily differ. The added titlebar spacing moves the logo/navigation,
and the shorter Premium content places its card lower. Typography, icons, opacity,
glow and especially the temporary background remain visibly different. Arabic/Dark
and English/Light contact sheets show the same surfaces across all pages and dialogs.

| Page | Presentation | Runtime / AR+EN / themes | Screenshot stem | Notes |
|---|---|---|---|---|
| Dashboard | Hero, three rings, actions, status | Verified functional matrix | `Dashboard` | Measured data; temporary background |
| System Information | Main Overview, detailed expanders | Verified functional matrix | `SystemInfo` | Actual provider availability, selectable values |
| Performance | Adaptive metric cards, charts, process table | Verified functional matrix | `Performance` | Shared 60-second history and existing process modes |
| Memory | Physical/commit/cache/history/consumer surfaces | Verified functional matrix | `MemoryTools` | Native Top Memory/Top CPU selector retained; production optimization is truthful no-op |
| Cleaner | Action/summary/category/review surfaces | Verified functional matrix + isolated clean fixture | `Cleaner` | Preview remains bounded to 100; cancel and partial results preserved |
| Uninstaller | Search/filter/list/detail/action surfaces | Verified functional matrix + isolated native removal fixture | `Uninstaller` | 20-row pool; confirmed removal and selected leftovers with backup protections |
| Startup | Search/filter/list/details/recovery actions | Verified functional matrix + isolated reversible fixtures | `Startup` | 20-row pool; confirmed disable/restore for registry, folder and task sources |
| Tools | Responsive grouped cards | Verified functional matrix | `Tools` | Fourteen allowlisted tools; availability controls Open |
| Settings | Grouped language/theme/privacy/diagnostics | Verified functional matrix | `Settings` | Immediate localization; keyboard and persisted preferences |
| About / Diagnostics | F1 mark/attribution, real version, notices | Verified functional matrix | `About` | Original license text, manual redacted ZIP, default Cancel |
| Dialogs / flyouts | Shared themed native surfaces | Functional cancel/keyboard and screenshot review | `Confirmation`, `ThemePopup`, `SearchFlyout` | No native control semantics removed |

## Validation scope

Build uses the pinned SDK **10.0.401** and existing .NET 10 / Windows App SDK
dependency lock graph. `Build-F1Candidate.ps1` performs locked restore, Debug and
Release solution builds, App/helper x64 publications and payload assertions.

The workspace is not a Git repository and Git was unavailable. The pre-edit source
archive is `artifacts/f1-ui/baseline/source-before.zip`: it was made before editing
existing sources but already contains the new, then-unmerged design resources.
It is a filesystem recovery snapshot, not a Git commit. No commit or push was made.
`test-results/source-change-review.json` verifies that all archived sources remain,
all existing dependency lockfiles match, and only four Core/Infrastructure files
changed: central identity, the two helper parent-path expectations and the Startup
validation fixture executable leaf. The 42 modified archived files are listed with
before/after SHA-256. New presentation resources, probes and docs are additional files.

The core regression completed **826 checks** including compatibility, plus **1,000
malformed inputs** in the security suite. Relevant coverage includes actual host
providers, Cleaner ownership and session safety, Uninstaller verification and
leftovers, reversible Startup state/backup handling, Memory analysis/cache no-op,
privacy/export, helper launch hardening and reliability boundaries.

A further **8 live helper parent-path/pipe checks** passed using byte-identical helper
copies in an isolated fixture. Both endpoints accept the exact installation-relative
F1 parent and reject the old executable name, another name and the right name in the
wrong directory. Every connected request deliberately uses Version 0 and is rejected
before an operation. This closes the rename-specific parent compatibility check
without executing privileged cleanup or Startup mutations.

UI functional validation is separate from passive process measurement. Synthetic
mutations use known isolated fixture roots/registrations only. The Cleaner lifecycle
driver retains its historical controller-only GC calls; those collect the UIA test
controller, never the app, and its memory observations are not performance evidence.
The ordinary F1 passive run uses neither UIAutomation nor forced GC nor working-set
trimming. Its durations, sampling and six navigation cycles match the baseline.

Evidence lineage: `build-06` is the delivered build. The final bilingual product UX
run is `test-results/ui-06`; the full and populated screenshot matrices and final
passive run use that publication. Earlier dedicated page/interaction/fixture checks
were run on the F1 `build-02` candidate. The later product edits only correct the
About/Settings version presentation and its supporting label/value properties;
the final focused Arabic probe, product UX and screenshot matrix cover that change.
Core/Infrastructure and copied helper hashes match their accepted test evidence.
The final Memory history/consumer images were reframed in a separate targeted
`test-results/memory-framing` run, using the same delivered assembly. Their individual
capture timestamps are retained in the merged populated-image metadata.

### Verified assertions

| Suite | Passed assertions |
|---|---:|
| Core/security/product/compatibility | 826 |
| Final bilingual product UI | 306 |
| Final screenshot matrix / populated states | 145 / 58 |
| Dedicated Memory / theme / shell regressions | 48 / 8 / 116 |
| Search/titlebar/window interactions | 10 |
| Native-route calibration / focused Arabic version | 15 / 4 |
| Live helper parent identity | 8 |
| Isolated Cleaner / Uninstaller / Startup / Memory fixtures | 26 / 57 / 59 / 25 |
| Targeted Memory screenshot framing | 28 |
| **Total** | **1,739** |

The security suite additionally processed **1,000 malformed inputs**. Diagnostic ZIP
review passed for `manifest.json` and `logs/recent.jsonl` (2,607 allowlisted records),
version `0.9.0-dev`, X64, and no current username/profile/machine identifiers. Nothing
was uploaded. Final preferences exactly match the bytes saved before `ui-06`; the
test-owned F1 processes are closed.

## Matched passive runtime comparison

Baseline: the preserved Phase 9 publication, **421.186 s**. F1: delivered `build-06`,
**422.067 s**. Both used the same current Windows 11 Pro x64 host, 1120×760 window,
six complete navigation/language/theme cycles, 30 seconds initial Dashboard,
two minutes Dashboard cooldown and **24×5-second minimized samples**. Both recorded
60 primary process samples and 82 managed heap counter samples. The first cycle
includes real read-only scan/inventory work; the cycle CPU column measures intervals
between end-of-cycle samples. Total normalized CPU across primary samples was
1.660% baseline / 1.199% F1.

Private memory is **MiB (1024² bytes)**; CPU is normalized over 12 logical processors.
Arrows show first → last sample in each stage. First-to-last sampling spans exclude
the initial five-second wait, so the minimized sample span is about 115 seconds
within the 120-second minimized workload.

| Stage | Baseline private MiB | F1 private MiB | CPU baseline / F1 | Handles baseline | Handles F1 |
|---|---|---|---|---|---|
| Initial Dashboard | 111.105 → 113.496 | 151.023 → 152.152 | 0.861% / 0.394% | 1051 → 1043 | 1203 → 1197 |
| Navigation cycles | 173.906 → 190.316 | 194.770 → 214.137 | 2.474% / 1.430% | 1416 → 1373 | 1528 → 1510 |
| Dashboard cooldown | 182.871 → 184.754 | 215.277 → 199.594 | 0.941% / 0.164% | 1367 → 1333 | 1501 → 1428 |
| Minimized | 184.844 → 185.586 | 198.891 → 198.781 | 0.097% / 0.094% | 1330 → 1330 | 1434 → 1426 |

F1 has a higher retained UI footprint: +38.656 MiB at the end of initial Dashboard,
+14.840 MiB after cooldown and +13.195 MiB at the end of minimization. It is not
memory-neutral. Its cycle samples were 194.770, 208.098, 212.215, 205.715, 212.473,
214.137 MiB; cooldown then fell to 199.594 MiB and minimization stayed near 199 MiB.
Handles/threads decreased during cooldown, and minimized CPU remained near baseline.
This run shows initial allocation and subsequent fluctuation/recovery rather than
uninterrupted per-cycle retention. The single before/after run does not establish a
speed improvement or certify long-term leak freedom; changing host workload and
inventory conditions also affect these observations.

System.Runtime heap counter range: baseline 2.759–20.413 MB; F1 3.824–23.076 MB
(counter-emitted decimal MB, separate from private MiB above). Both runs report
**zero interval throws**, no unresponsive process samples and normal close/restart.
F1's active timer counter was 1 throughout. App stderr files are empty. `ExitCode`
remains **null**, so normal close is observed but zero exit code is not asserted.
Raw process/counter/navigation evidence is in `baseline/stability/` and
`test-results/stability/`; this process never loaded UIAutomation and used no forced
application GC or working-set trimming.

### Publication fingerprints

Key payload fingerprints and screenshot SHA-256 entries are in the summary and
`test-results/image-hashes.json`. Key delivered files:

```text
F1.exe  C9B0D8326DA4F29CEC5F104A5C31757D7BA471DE002D6F9E4A93F256A9004C05
F1.dll  774A5F8A613E1F818DD60B670B3660FAFDD067C543A5467A2320C5362D79C809
```

### Reproduction entry points

Run UI/native checks one at a time in an interactive non-elevated desktop session:

- `scripts/Build-F1Candidate.ps1`: ordinary App and paired helper publication.
- `scripts/Invoke-F1Core.ps1`: real-host and isolated service regressions.
- `scripts/Invoke-F1ProductUx.ps1`: bilingual/theme/keyboard/preferences/product checks.
- `scripts/Test-F1Visual.ps1`: published screenshots, actions, minimum-size matrix.
- `scripts/Test-F1PopulatedScreens.ps1`: read-only scans/inventories and measured
  histories, with supplementary screenshot metadata.
- `scripts/Test-F1MemoryFraming.ps1`: targeted top/history and bottom/consumer frames
  in the completed populated set, preserving per-image metadata.
- `scripts/Invoke-F1PageRegression.ps1`: Memory, immediate theme localization, shell.
- `scripts/Test-F1Interactions.ps1`: route search, titlebar actions, native windowing.
- `scripts/Build-F1Fixtures.ps1` then `scripts/Invoke-F1Fixtures.ps1`: separate fixture
  publication for Cleaner, Startup and Memory; known Uninstaller fixtures use the
  ordinary publication.
- `scripts/Invoke-F1Stability.ps1`: separate passive native process with saved
  functional coordinates; six cycles and two minutes minimized.
- `scripts/Test-F1NativeCalibration.ps1`: verifies clicks on the expanded Performance
  header separately from its nested Memory row before native-only measurement.
- `scripts/Verify-F1Export.ps1`: allowlisted archive content and metadata review.
- `scripts/Test-F1HelperIdentity.ps1`: eight non-mutating path/pipe checks against
  copies of the real helper. Requires an already elevated controller; it does not
  request UAC. Its standalone test project adds no external dependency.
- `scripts/Verify-F1Source.ps1`: archive/source/dependency and historical-publication
  hash compatibility review.
- `scripts/New-F1Comparisons.ps1`, `scripts/New-F1ContactSheet.ps1`: comparisons.
- `scripts/Write-F1Summary.ps1`: final evidence assertions and aggregate.

The harness used `Invoke-DesktopTokenValidation.ps1` to launch controllers with the
interactive Explorer token. This establishes unelevated execution on this host,
not testing with an independent Standard User account or alternate UAC credentials.

## Remaining visual differences

1. **Clean background asset: MISSING.** The local original geometric mountain
   illustration replaces the unavailable photographic asset by the owner's explicit
   choice. The approved flattened reference is never shipped as a runtime background.
2. Native controls, system fonts and a newly drawn geometric logo preserve functionality
   and accessibility. They are not pixel-identical to the reference's rendered artwork.
   Icons and text measurements differ; no third-party font/icon library was added.
3. Opaque cards and subtle ring glow prioritize legibility and the host's disabled
   transparency setting. The reference has stronger glass/halo effects and gradients.
   The retained native selection treatment is less luminous.
4. Memory adds an explicit subroute; navigation spacing and the Premium card respond
   to window height. The Dashboard is intentionally scrollable when there is less room.
5. Real values use existing 1024-based B/KB/MB/GB/TB formatting and localized numeric
   conventions. Long technical values wrap. Metadata only displays data available from
   current services; speculative SSD health, frequency/type values are not substituted.
6. Marketing claims are replaced with supported copy: manual review before cleaning,
   available tools, About F1, readiness and actual scan state. There is no subscription,
   fabricated healthy/protected status or performance-gain promise.
7. The existing native two-choice Memory mode dropdown is retained in place of a new
   tab strip. Top Memory/Top CPU semantics, bounded rows and keyboard selection work.
8. Windows owns the resize border, rounded corners, caption controls and Snap behavior.
   It may differ from the flattened reference frame. Runtime captures include the
   outer window; metadata separately records client pixels.

## Preserved failures and corrections

- `iterations/r2`: startup render crash and diagnostic isolation. Suppressing images
  and rebuilding did not fix it. Separate native geometry ownership for the ring and
  glow did. The bounded reusable geometry replaced invalid shared ownership; errors
  were not swallowed. Earlier failed publications and logs are retained.
- `test-results/ui-01`: nested Memory entry was collapsed by Performance selection;
  the parent now expands after navigation.
- `test-results/ui-02` and `focus-probe`: long-driver focus precondition failure;
  focused Tab/Shift+Tab worked. The final driver verifies foreground/focus before
  sending keyboard input.
- `test-results/ui-03`: transient popup item not yet available; the chooser now waits
  for the real native selection item, asserts availability and then selects it.
- `iterations/visual-01`: initial size was overwritten by startup layout; final capture
  resizes after Loaded and resets scroll position. `iterations/visual-02`: attempted
  scrolling of a non-scrollable page; the driver now checks VerticallyScrollable.
- `iterations/visual-04`: an immediate UIA check ran during dialog-close animation;
  the final capture waits up to five seconds for the native dialog to disappear after
  Escape. `iterations/about-startup-timing`: a fixed launch delay was insufficient;
  the focused probe now waits for the actual window handle.
- `iterations/native-route-01`: the expanded Performance item's UIA bounding box
  included Memory. Its center was not inside the clickable header, so post-run native
  route validation correctly rejected that measurement. A separate functional
  calibration measures the parent-header band above the child and verifies a real
  mouse click in both languages. That failed measurement is not used as final evidence.
- Image review caught Arabic bidi reordering of the mixed `0.9.0-dev` About version.
  Unicode isolate and explicit paragraph direction alone did not render correctly.
  About/Settings now use separate localized-label and LTR technical-value text
  elements with explicit reading order; clipboard/metadata keep the literal value.
- `iterations/populated-02-scroll-framing`: a completed matrix retained Memory's
  bottom scroll position across theme changes. `populated-03-interrupted` preserves
  the subsequent full recapture interrupted at the 300-second shell timeout. The
  completed matrix was retained, preferences restored, and only the eight affected
  Memory images were replaced by a successful targeted framing run. No product
  binary changed and no passive performance measurement was repeated.

## Limits

Current Windows 11 x64 host and available display only. 125/150/200% DPI, full
high-contrast/Narrator and edition/clean-VM matrices remain **NOT RUN** here. High
contrast resources and opaque fallback are implemented but not runtime-certified by
these tests. No system DPI/theme/accessibility settings were changed to force coverage.
Interactive UAC consent/alternate-credential helper handshakes remain unverified;
the paired publication and installation-relative F1 parent checks passed the separate
already-elevated, invalid-request fixture. Signing, installer, protected installation and broad release QA remain
outside this UI development task. Short runtime results do not certify long-term
leak freedom or universal compatibility.
