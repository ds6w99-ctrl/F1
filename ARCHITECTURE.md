# Architecture — SystemCore

## Phase 8 additions

Core/Tools owns a closed Windows-tool catalog and typed availability/launch results.
Infrastructure/Tools checks documented URI support or pinned trusted system files;
the UI submits only a catalog ID. The Tools page retains one set of fourteen native
cards, grouped by purpose, and performs availability queries only on entry/Refresh.

Core/Diagnostics exposes typed event records and manual export/clear/folder actions.
Infrastructure/Diagnostics uses a 128-entry asynchronous queue, a per-directory mutex,
ten bounded local log files, handle-verified cleanup and allowlist-reserialized ZIPs.
The application listener excludes all EventSource payload strings. Diagnostics does
not sample performance, record process inventories or make network requests.

Settings retains only real language/theme preferences with a bounded extensible JSON
schema and atomic persistence. ProductSupportViewModel serves the retained Settings/
About views; version metadata and embedded original notices require no web service.
Shared date/time/duration/size/rate formatting follows the selected application culture.

## Phase 7 additions

Memory Tools is a detailed consumer of the existing PerformanceMonitorService.
GetPerformanceInfo extends its shared snapshots; available-memory/commit scalars
extend its existing bounded history. Process CPU reuses the PID/creation-time delta
engine. The page holds only its current top-ten summaries, ten native rows and the
last manual optimization result. Hidden-page references and chart geometry clear
on deactivation. Process path/company metadata is queried only on explicit selection.

Core/MemoryTools separates metric math, sustained pressure/hysteresis, analysis,
and manual optimization decisions. The optimizer serializes requests, verifies
fresh metrics, and accepts only trusted in-process cache-owner contracts. Production
registers no cache owners: current retained inventories/sessions are not proven
discardable caches. A valid no-op is displayed explicitly. `MemoryValidation=true`
compiles a separate injected 16-MB test fixture and synthetic pressure controls;
these types and controls are absent from the production App assembly.

Cache owners must atomically revalidate version/ownership/staleness/inactivity and
return their directly known released bytes. No forced GC, native memory purge,
process mutation, helper endpoint or background optimizer exists. Before/after
observations use the shared monitor, with a temporary event subscription and a
three-second timeout after real actions; no new polling worker is introduced.

## Phase 6 additions

Core/Startup defines normalized source identities, status/protection and ID-only
single-item requests. Separate Registry, known-folder and Task Scheduler COM
providers feed an asynchronous inventory shared with Dashboard. The UI reuses twenty
native rows; search/sort/filter and Arabic/English details do not execute commands.

StartupInventoryService and StartupMutationService share one operation gate and a
consumable ten-minute session. A mutation re-resolves its source, then persists a
versioned DPAPI-protected receipt before changing anything. Registry values preserve
their raw typed bytes and use KTM transactions. Files move on the same volume through
exact source/destination directory handles with no replacement. Task definitions are
revalidated before setting Enabled and checked again afterward. StartupStateService
merges saved disabled/recovery rows into refresh; exact restored sources finalize
their state, while conflicts retain it. There is no automatic startup command launch.

Privileged sources use a separate 4-KiB typed helper endpoint. It independently
resolves IDs/fingerprints against production sources and ACL-protected machine state;
it accepts no command, Registry address, task path or source/destination filename.
User-source operations remain in the application. The main manifest remains asInvoker.
The `StartupValidation=true` build property compiles isolated UI fixtures into a
separate validation publication; production has no runtime test-root selector.
See [Startup protocol](docs/STARTUP-PROTOCOL.md).

## Phase 5 additions

Core/Uninstaller defines installed identities, inventory/removal/leftover sessions,
ID-only requests and per-item outcomes. Infrastructure/Uninstaller separates official
Registry/MSI/MSIX inventory, native execution/completion, internal pre-uninstall
evidence, bounded discovery, filesystem validation and transactional Registry deletion.
The UI composes these through DI; twenty reusable native rows display current strings
and IDs, with cancellable background operations and explicit native/leftover confirmations.

Receipt creation is internal to verified native removal. Scanner and deleter share an
operation semaphore and a single consumable plan. File deletion uses the same pinned
ancestry/exact-handle implementation as Cleaner, extended with empty-directory disposal.
Registry mutation requires a typed durable backup and a matching transactional snapshot
including last-write metadata. Current-token permission failures are per-item results.

Ownership is established before removal, never guessed after it. The pre-uninstall receipt
records the registered install location, the registry data path, and the application's data
directories under the shared roots. A second layer observes the directories beneath those
roots immediately before and immediately after the native uninstaller runs: an anchor is
accepted only when its last-write time moved **and** its direct entry count decreased between
the two snapshots. An active cache rewrites or grows, so its count stays or rises and it never
anchors; a real deletion by the native uninstaller shrinks the directory and proves it was
worked on. Each unverified anchor is additionally capped so one noisy directory can never
flood the review, and a truncated session still deletes the individually verified items it
shows. That names an application-owned folder whose own name resembles nothing, which no
amount of name matching can do. Name-linked and observed items are always Review and require
explicit personal-data consent; only an exact pre-uninstall receipt fact can be Safe.
Admin-only trees (Program Files, ProgramData) are routed through the elevated helper behind
a closed operation allow-list, and the helper never receives a user-writable tree.

## Phase 4 additions

Core/Cleaner and Infrastructure/Cleaner provide scanner-owned bounded sessions, GUID-only
selection, exact-handle revalidation/deletion, official Shell Recycle Bin operations and
a typed Windows Temp helper. Dashboard retains a small summary instead of full scan graphs.
See [Cleaner protocol](docs/CLEANER-PROTOCOL.md); early design statements below are historical.

## Phase 3 additions

Core/Performance owns `IPerformanceMonitorService`, unified snapshots, scalar bounded history,
delta trackers and the sole sampling task. Infrastructure/Performance adds native PDH disk I/O,
.NET network/process counters and isolated collection. App/Performance provides lightweight
native charts and reused process rows. DashboardMonitor is now a timer-free adapter to the
shared service, not a second polling engine. Detailed collection is requested only by the
visible Performance or Memory Tools page; all provider reads pause without visible telemetry consumers.
UI updates are coalesced.

## Phase 2 additions

Core/SystemInformation defines immutable snapshot records, provider contracts, a fault-isolating
aggregator and shared binary size/percentage formatting. Infrastructure/SystemInformation contains
seven Windows providers and a bounded WMI reader. App/SystemInformation separates presentation
mapping, view-model lifecycle and XAML. Firmware combines two independently guarded queries.

Static inventory is session-cached by the view model; Refresh bypasses it. An AsyncRelayCommand
prevents overlapping refreshes. Leaving the page/minimizing cancels the caller; native WMI calls
may finish later, bounded by three worker slots and a five-second enumeration timeout. Each
provider has a twenty-second caller timeout. No WMI runs on the UI thread or live timer.

DashboardMonitor is shared by consumer identity between Dashboard and System Info. It performs
reads only when at least one visible, non-minimized page subscribes. Both pages use the same
CPU/memory/uptime/system-volume sample. Windows identity and volume capacity readers are shared;
Dashboard does not wait for hardware inventory. Shared `InfoFormatting` retains 1024-based
scaling with UI labels B/KB/MB/GB/TB and B/s/KB/s/MB/s/GB/s across all pages.

## Modular monolith

The solution has four production projects:

- **App**: WinUI resources, navigation, MVVM view models, composition root using DI.
- **Core**: models, policies, operation coordination and contracts; no WinUI dependency.
- **Infrastructure**: read-only Windows providers first; approved writes later.
- **ElevatedHost**: short-lived, protected, typed privileged operations only.

Phase 0 creates App/Core and the **token-only** ElevatedHost probe plus executable
unit and actual-host compatibility test runners. Infrastructure and IntegrationTests are introduced when they contain
real work. The production broker protocol is designed in SECURITY.md, not implemented.
Compatibility policy math is in Core.
Phase 1 adds Infrastructure, CommunityToolkit.Mvvm view models and a Microsoft DI composition
root. MainWindow handles window lifecycle and view composition. View models own presentation
state; Core defines sampler/inventory contracts and tested math; Infrastructure performs reads.
One background PeriodicTimer samples once a second, skips reads off Dashboard/while minimized,
and marshals results through DispatcherQueue. Inventory is loaded once off-thread with an
eight-second wait bound; native calls may finish later. There is no synchronous UI wait.

## Module boundaries for later phases

Dashboard, Cleaner, Uninstaller, SystemInfo, Performance, Memory, Startup, Tools, Settings.
Read contracts: `ICleanerScanner`, `IInstalledAppsProvider`, `ILeftoverScanner`,
`ISystemInfoProvider`, `IPerformanceSampler`, `IStartupProvider`.
Write contracts: `ICleanupExecutor`, `ISafetyPolicy`, `IUninstallService`,
`IBackupService`, `IPrivilegeBroker`. Add `IUpdateService` as disabled/offline until a
real authenticated update service exists. Contracts will be implemented only with their stage.

`OperationCoordinator` will grant scoped, asynchronous operation leases using resource keys
(application identity, cleanup root, registry source). Uninstall and leftover handling for
the same app must conflict. UI cancellation stops future steps; durable journals describe
already-completed steps. No false rollback promise.

## Data and privilege flow

Read-only scan -> immutable, versioned review plan -> explicit confirmation -> coordinator
lease -> executor revalidates each object through policy -> per-step journal -> report.
If elevated: typed operation -> authenticated local broker -> independent host validation
against compiled allowlist + original user identity -> bounded execution -> structured result.
No UI-supplied executable, script, arbitrary path deletion or arbitrary registry writes.

## Assumptions and stage boundaries

- Workspace was empty; source created directly in its root.
- Product name temporary; publisher and logo unassigned; no commercial license invented.
- The original two-tab fixture is superseded by the phase-1 shell; its scripts/artifacts are historical.
- Both .NET and Windows App SDK self-contained is the candidate deployment strategy.
- Scope is Windows 11 23H2+ x64, Home/Pro/Enterprise/Education; 24H2+ recommended.
  Exact Microsoft servicing is reviewed separately, never inferred from build >= 22631.
- Clean-machine/version/edition certification belongs to the mandatory Release Gate. Select
  supported versions at release time; the technical floor is not lifecycle policy.
- No telemetry/network code in the probe. Development documentation/package downloads
  are build-time activity, not application runtime behavior.
- No background service, tray component or login task.
