# Release QA backlog

Release / Production Gate: **PENDING**.

## Distribution artifacts

Two distribution paths exist for `1.0.0`: the per-machine installer from
`installer/F1.iss` (built by `scripts/Build-Installer.ps1`) and a no-install portable
archive from `scripts/Build-Portable.ps1`. Both are described in `docs/INSTALL.md`, and
both were exercised on the work host — silent install to `%ProgramFiles%\F1`, layout
inspection, a 118/118 UI regression against the **installed** executable, and portable
extract-and-run. The record and artifact hashes are in `installer/DECISION.md`.

None of that closes a row below. Neither artifact is signed, no clean machine was used,
no genuine Standard User account was involved, and no real UAC prompt was answered. The
work host is the same machine that built the payload, so it is the weakest possible
environment for the clean-machine and signing rows.
## Phase 9 additions

- Separate Standard User account and real UAC accept/deny/cancel/alternate-admin
  credential matrix are explicitly deferred; desktop-token validation is not a
  substitute. CurrentUserOnly IPC intentionally rejects a different-user peer.
- Validate signed App/helper loading from a protected installation directory,
  publisher/signature verification policy and all native/runtime dependencies.
  Use external certificate/HSM signing with timestamp and post-sign verification;
  no private keys or test-cert assumptions in source/publication.
- Repeat cold launch, startup-provider cancellation, interrupted preferences/state/
  ZIP writes and low-disk recovery on disposable VMs. Hard termination may retain
  bounded `.pending` export files; never describe an incomplete package as success.
- Retest bounded Registry inventory with unusual value types/large inventories and
  enterprise policies. Incomplete source state must remain visible.
- Long soak on the exact signed payload; keep first-chance attribution capture
  separate from primary memory evidence. Expected high-contrast subscription failure
  and shutdown cancellation must not be confused with unhandled exceptions.
- Final NuGet/runtime/native/license payload SBOM refresh and notices, full Windows
  edition/DPI/Narrator/high-contrast/install lifecycle matrix. The current `1.0.0`
  package is unsigned and has no clean-machine certification yet.
- F1-003 leftover-scan fix is implemented and proven on the work host (unit/runner tests,
  306/306 UI checks, end-to-end synthetic probe: 9/9 items, no pollution, delete enabled).
  Repeating the same end-to-end proof on a disposable clean VM remains a release row.
- Clean-machine portable launch is now evidenced: a fresh Hyper-V VM (Windows 11 Enterprise
  Evaluation, build 26300, no SDKs) verified the portable archive SHA256, extracted it, and
  launched `F1.exe` to a real window titled "F1". The installer's edition gate correctly
  refused the evaluation edition and now fails fast on silent install (no hang). The
  install/uninstall lifecycle on a supported retail edition and the Standard User/UAC
  matrix stay open.

Phase 10 requires owner approval.

## Phase 8 additions

- Exercise Windows tool handler/page availability across Home/Pro/Enterprise/Education,
  optional components, enterprise restrictions and separate Standard User accounts.
  A registered URI handler does not guarantee access to each individual Settings page.
- Full Narrator, high contrast and DPI/multi-monitor matrix, including long RTL mixed
  technical strings, compact navigation, scrolling license text and dialog focus.
- Signed final payload notices/attribution review, clean VM launch and install/update/
  uninstall lifecycle. Development `0.8.0-dev` is not Production 1.0.
- Longer product-like native-input soak, including repeated diagnostic exports and
  cold restarts. Keep continuous UI Automation out of primary memory measurements;
  report runtime thrown-exception counters separately from unhandled failures.
- Broader storage-failure/low-disk/interrupted-write environments and simultaneous
  instances; redacted logs intentionally omit raw payloads and stack traces. Deeper
  diagnostic capture requires separate authorization and explicit data review.

## Phase 7 additions

- Extreme low-memory/commit-pressure disposable VMs; larger RAM sizes, hot-add and
  changing page-file soft commit limits. Cross-check the documented sustained
  thresholds against real paging/responsiveness evidence without claiming a speedup.
- Broader protected-process/access-denied/process-churn matrix and Windows 11
  Home/Pro/Enterprise/Education. The development desktop token is a filtered user
  token, not certification of a genuinely separate Standard User account.
- Long production-like Memory Tools navigation/analysis/manual no-op cycles using
  native input and passive process/runtime counters. Keep UIA functional tests
  separate from the primary memory measurements; investigate any invalid negative
  managed readings without clamping or treating them as freed bytes.
- Full Narrator/DPI/high-contrast matrix, RTL mixed process names and executable
  paths, chart descriptions, focus order and System/Light/Dark persistence on the
  signed final installation. Signing and install lifecycle remain release work.
- Per-process disk/network attribution and page-file configured/current usage are
  explicitly unavailable in V1; do not infer them from process I/O or commit totals.

## Phase 6 additions

- Genuine separate Standard User and same-user UAC acceptance/denial; alternate-admin
  credentials are currently unsupported/fail closed through the CurrentUserOnly pipe.
  Test exact helper publication, peer identity, disconnection/timeouts and signing/
  loader-chain integrity on disposable VMs, never by changing user startup sources.
- Real logon/reboot behavior across all supported Windows 11 editions, accounts,
  enterprise/domain policy, task ACLs/principals and a large Task Scheduler matrix.
- Concurrent task administrators: Enabled has no atomic definition compare/exchange.
  Verify immediate revalidation/read-back and recovery reporting under races.
- Remapped Startup known folders, non-system fixed volumes, denied/missing keys,
  unavailable KTM, full disks, interrupted/power-loss state writes and manual recovery
  of corrupt or no-longer-valid receipts. Cross-volume move fallback is unsupported.
- Review security/driver classification against broader software inventories. Unknown
  command/file formats and system/security/ambiguous tasks remain read-only.
- Longer manual/non-UIA Startup navigation and mutation cycles with passive metrics;
  full Narrator, keyboard/focus, DPI, RTL long paths and Light/Dark/System visual tests.
- Protect saved disabled files and receipts throughout signed installation upgrades;
  never delete recovery state as ordinary installer cleanup. Bound both state count
  and file sizes; document the 256-live-receipt-per-scope capacity to support teams.

## Phase 5 additions

- Validate original Win32/MSI/MSIX uninstall on disposable clean VMs, including slow
  child installers, reboot-required states, missing uninstallers, MSI contexts,
  removable/protected Store packages and genuinely separate Standard User/UAC cases.
- Large/rare installer inventories, incomplete source permissions, duplicate identities,
  multiple products sharing directories or app-specific registry storage.
- Current-token leftover cleanup skips access denied. An explicit elevated leftover
  broker is now implemented: a fixed protocol version with bounded item count, a
  nonce/timestamp-validated request, and a CurrentUserOnly pipe that fails closed to
  SafetyRejected. Remaining release work is the actual elevated accept/deny behaviour
  on disposable VMs, not the broker's existence.
- Registry transaction availability/conflicts/ancestor rename races, damaged or full
  backup storage, typed JSON backup recovery procedure and key-security metadata.
  Backup is mandatory for removal, but automatic restoration is not guaranteed.
- Longer production-like GUI cycles with manual/non-UIA input and passive metrics;
  distinguish GUI idle, core synthetic cycles and UIA-connected functional checks.
  Preserve/exclude invalid negative GC.GetTotalMemory(false) values; Phase 5's first
  cooldown workload again recorded one such sample. Cross-check runtime counters
  and last-GC heap/commit; do not clamp it or infer a production leak from that value.
- Full Arabic/English evidence/risk/path display at all DPI settings, keyboard-only
  selection, Narrator, modal confirmation and cancellation; verify signed exact release
  payload on machines without SDKs or developer tools.
- Optional shortcut discovery is unsupported in V1; maintain Review/default-off if added.

## Earlier phase backlog

Phase 4 Development is **PASS** under the owner's final no-UIA isolation rule.
Additional long-duration memory validation is **DEFERRED TO RELEASE VALIDATION**:
production UI with manual or non-UIA input, on an isolated desktop, and passive
metrics; compare with UIA-connected/disconnected runs without treating client-
lifetime allocations or allocator commit as proof of a production leak. Include
longer native-allocation tracking if growth reproduces without UIA. Preserve
bounded sessions/items/views, handles/threads and actual outstanding allocations
as separate measurements. Complete the broader production native UI regression;
historical interrupted UIA attempts do not become passes through this gate change.
Investigate the three negative GC.GetTotalMemory(false) readings in the isolated
Core cooldown and cross-check runtime counters/last-GC heap data before relying
on that metric.
Phase 4 adds **DEFERRED TO CLEAN VM / RELEASE QA**: real destructive Recycle Bin
emptying, Windows Temp elevated fixture/full cleaning, genuine Standard User
and same-user UAC consent/denial. The broker uses a current-user-only pipe;
alternate-administrator credentials fail closed and require explicit release
coverage rather than broadening the pipe ACL. No test emptied the user's real
Recycle Bin or cleaned real Windows Temp. Browser caches and user WER are
**UNSUPPORTED IN V1**, not partially enabled features. Review the fixed helper
publication layout, signing/trust, IPC peer authentication, timeout/disconnection
and unknown-result UI on the exact release artifact. Full UIA/synthetic tests
and the development lifecycle soak are separate development requirements.
Phase 3 adds VPN/bridging/multiple-IP-adapter traffic accounting, disabled/unregistered PDH
counters, genuine Standard User process/counter access, high process churn and large process
counts, processor groups, longer performance/memory soak and chart/list accessibility validation.
Status: **DEFERRED TO RELEASE VALIDATION**; the development soak is tracked separately.
Phase-3 development soak completed: 20 minutes, bounded history, no observed crash. Longer
soak must follow the modest private-byte growth and late handle increase observed during development; this observation does not certify long-term leak freedom.
Phase 2 adds hardware-diversity validation: absent/denied/slow WMI providers, multiple CPU/GPU
configurations, firmware omissions, >64 active processors, device/network changes, volume
mount points and storage permissions; extended System Info RTL/Narrator/copy/layout and soak.
Status: **DEFERRED TO RELEASE VALIDATION**.
Default status for every open row: **DEFERRED TO RELEASE VALIDATION**.
These items do not block development solely because a VM, device or certificate is unavailable.
Actual phase-specific failures remain visible in development reports.

| Required validation | Status | Evidence required before release |
|---|---|---|
| Clean Windows 11 machine without VS/SDKs | DEFERRED TO RELEASE VALIDATION | Install and actual launch of the exact published artifact |
| Home / Pro / Education; Enterprise where practical | DEFERRED TO RELEASE VALIDATION | Edition-specific results; disclose any practical Enterprise coverage limitation |
| Officially supported Windows 11 versions at release | DEFERRED TO RELEASE VALIDATION | Dated lifecycle/edition matrix and actual target tests |
| Genuine Standard User | DEFERRED TO RELEASE VALIDATION | Account membership and medium-integrity launch, not a filtered administrator substitute |
| UAC accept / deny / alternate administrator | DEFERRED TO RELEASE VALIDATION | Actual prompts, outcomes, responsiveness and original-user scope |
| Install / uninstall / reinstall / residue | DEFERRED TO RELEASE VALIDATION | Logs, hashes, baseline comparison |
| Restart persistence | DEFERRED TO RELEASE VALIDATION | OS boot evidence and installed launch |
| DPI 100%, 125%, 150%, 200% | DEFERRED TO RELEASE VALIDATION | Actual DPI, both languages/themes, no clipping or overlap |
| Narrator / focus visibility / keyboard-only | DEFERRED TO RELEASE VALIDATION | Spoken output, visible focus, full traversal; development UIA evidence is partial |
| High Contrast and unavailable visual effects | DEFERRED TO RELEASE VALIDATION | Readable native fallback and settings-change behavior |
| Arabic RTL / English LTR visual verification | DEFERRED TO RELEASE VALIDATION | Navigation, alignment, text, paths and reading order |
| Light / Dark / System themes | DEFERRED TO RELEASE VALIDATION | Visual and live-switch verification on release artifact |
| Digital code signing and timestamp/trust | DEFERRED TO RELEASE VALIDATION | Real certificate, expected publisher, signed payload/setup/uninstaller |
| Final dependency / SBOM / notices review | DEFERRED TO RELEASE VALIDATION | Exact managed/native/tooling closure and redistribution terms |
| Final security and privacy testing | DEFERRED TO RELEASE VALIDATION | Threat-model review, runtime network observation, authenticated broker tests when implemented |
| Reference-machine performance / cold startup / long soak | DEFERRED TO RELEASE VALIDATION | Defined warm-up, CPU/working set/private bytes/handles, pause/resume and long-duration evidence |

The earlier phase-0 short CPU observation is retained as a measurement, not a development
failure or a steady-state diagnosis. Investigate under controlled reference conditions.
No fixed obligation to test an expired Windows version: choose supported versions per edition
at release time, independently of the minimum technical API floor.
