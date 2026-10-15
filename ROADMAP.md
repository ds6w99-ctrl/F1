# Roadmap

Gate policy: [docs/GATES.md](docs/GATES.md). Release-only cases are **DEFERRED TO RELEASE VALIDATION**.
Windows 11 x64 only; Home/Pro/Enterprise/Education. Official release support follows Microsoft's
edition lifecycle at shipping time, independently of the technical minimum.

## Current authorization — Phase 9

The owner accepted Phase 8 Development PASS and authorized Phase 9 only.
Sequential gates: 9A exception/reliability audit, then 9B security hardening, then
9C full regression. All three gates and Phase 9 Development now PASS; stopped for
owner review. Phase 10 is not authorized.
Release / Production remains PENDING. Accepted Phase 4/5/6/7 evidence is preserved.
The phase descriptions below preserve earlier planning/history.

## Phase 0 — accepted

Development Gate **PASS**; Release Gate **PENDING**. Existing successful build, 18 unit tests,
9 compatibility tests, native launch, x64 publication and installer compilation accepted.

## Phase 1 — authorized

WinUI shell with MVVM/DI, navigation, en/ar localization, themes and genuine Dashboard readings:
CPU, memory, system-volume capacity/free space, OS/device/uptime and scoped inventory counts.
Cleaner status stays Not scanned. Later modules are clearly identified navigation placeholders.
Shared one-second sampling pauses when away from Dashboard/minimized. No maintenance actions.
Run Debug/Release builds, essential tests, x64 publish and native navigation smoke.
Stop after the Phase 1 report for owner review; do not begin Phase 2 automatically.

## Later stages

Phase 3 Performance Monitoring is complete: a shared read-only sampler, bounded live history
and process CPU/working-set views. Development Gate PASS, including 149 unit checks, 72 native
UI checks and a 20-minute development soak.
Phase 4 Safe Cleaner was authorized and implemented; final development validation
is complete.
The later explicit Phase 5 authorization superseded its previous wait-for-review instruction.

Owner-authorized Phase 2 (System Information only) is now implemented; Development Gate PASS.
Application/startup management, cleanup and optimization remain
future scopes requiring separate authorization.

2. Detailed read-only system/application/startup inventories and performance/memory diagnostics.
3. Narrow versioned Cleaner scanner; review/exclusion plans only, no deletion.
4. Safety tests, authenticated privileged broker, journals/backup/restore before scoped mutation.
5. Validated native uninstall and optional proven leftover associations.
6. Tested reversible startup edits and selective memory actions, otherwise read-only.
7. Reports, privacy, tools, accessibility/performance and security/license review.
8. Tools / Settings / Product UX (completed and owner-approved).
9. Security / Reliability / Final Hardening (current owner-authorized scope).
10. Release validation (requires owner authorization): clean targets, install lifecycle,
    real UAC, signing, full QA/SBOM/security.

Development Gate flexibility does not waive tests required to implement dangerous operations.
