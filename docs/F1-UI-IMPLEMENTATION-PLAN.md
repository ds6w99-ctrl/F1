# F1 UI implementation plan

Authority: `artifacts/f1-ui/specification.md`, supplied UI pack and
`docs/design/reference/f1-approved-reference.png` (1448 × 1086). This is an independent
UI rebrand, not approval of Phase 9/10. Existing Phase 9 report/summary read from disk:
Development PASS, owner review PENDING, Release PENDING. No AGENTS.md found.
Workspace has no `.git`; git executable is unavailable. No reset/commit/push.

## Work list

- [x] R0: two read-only parallel reviews, source snapshot, baseline Release build and unit checks; six native cycles / 421.186 seconds including two minutes minimized passed.
- [x] R1: shared design resources, geometric mark, temporary background provenance, central branding. Published resources loaded at R2.
- [x] R2: left-hand shell, supported title bar, real dashboard gauges/history/actions. Runtime drawing defect corrected; real AR/Dark screenshot reviewed, geometry/footer proportions refined.
- [x] R3: all pages and popups implemented with shared surfaces, type, input/action styles and responsive bounded operational lists. Full runtime coverage is verified separately at R4/R5.
- [x] R4: F1 executable/helper/resource compatibility and bilingual/theme/focus/regression verified; 1,739 assertions + 1,000 malformed inputs. Available display tested at 100%; higher DPI/full Narrator/high contrast remain NOT RUN.
- [x] R5: delivered build-06, 132 runtime screenshots, cropped side-by-side/overlay/difference, matched six-cycle measurement and final report/gates. Visual Reference remains PENDING for the missing photographic asset and disclosed differences.

## Actual file map and ownership

`src/SystemCore.App/App.xaml` merges new `DesignSystem/F1.*.xaml` resources.
`MainWindow.xaml[.cs]` owns stable-left navigation, caption integration and page search.
`Views/DashboardView.xaml[.cs]` and `ViewModels/DashboardViewModel.cs` present live data;
`Services/DashboardMonitor.cs` exposes existing shared history without another worker.
`Views`, `SystemInformation`, `Performance`, `MemoryTools`, `Cleaner`, `Uninstaller`,
`Startup`, `Tools` retain their existing view models, command identities and services.
`Services/WindowAppearanceController.cs` applies theme/accessibility and page direction,
without mirroring the shell. `Resources/Shell*.resx` owns localized UI copy.
Branding is centralized with the existing product identity. Namespaces, IPC, data
root, recovery state and technical helper name remain compatible.

## Button → command → service map

| UI | Command / existing action | Service / result |
|---|---|---|
| Hero / cleaning action | navigate Cleaner, execute existing ScanCommand | ICleanerService.ScanAsync; read-only review session, never delete |
| Manage programs | shell Navigate(Uninstaller) | Existing inventory/detail UI, no automatic uninstall |
| Startup manager | shell Navigate(Startup) | Existing inventory/confirmed reversible action UI |
| Premium / brand / About entry | shell Navigate(About) | Version, notices, diagnostics; no subscription |
| Search | bounded list of existing routes | navigation only, never filesystem/shell execution |
| Settings icon | shell Navigate(Settings) | existing persisted preferences |
| Existing dangerous actions | existing handlers/commands/confirmations | unchanged service authorization, cancellation, typed results |

Primary comparison is Arabic/Dark at 1360×1000 outer / 1344×992 client pixels,
96 DPI. Smaller 900×600 outer / 884×592 client and English/Light/System are separate checks.
All screenshots must come from the published WinUI application. Missing clean
background asset must be disclosed; the flattened reference is never an app asset.

Final evidence: `artifacts/f1-ui/summary.json`; report:
`docs/F1-UI-IMPLEMENTATION-REPORT.md`. Baseline 421.186 s / F1 422.067 s; each includes
two minutes minimized. Normal close/restart observed; ExitCode remains null.
Owner review and historical Release / Production approvals remain pending.
