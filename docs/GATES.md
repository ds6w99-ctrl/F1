# Development and Release Gates

Owner-approved policy, 2026-09-24. This supersedes the previous single phase-0 gate.

## Development Gate

Required to proceed between development phases:

- Successful solution and Release build; zero compile errors and no critical warnings.
- Essential unit and compatibility/architecture tests pass.
- Actual application launch on Windows 11 x64; main UI runs without elevation.
- Successful x64 publish; no known crash blocking use.
- Architecture sufficiently stable for the next scoped phase.

**Phase 0 Development Gate: PASS.** Accepted existing evidence: 18/18 unit tests,
9/9 compatibility tests, Release build without errors/warnings, actual Pro 25H2 launch,
x64 app/helper publication, and experimental installer compilation. No tests were rerun
merely to change the gate policy. Original execution logs remain in artifacts/phase0-validation.

## Release / Production Gate

**Phase 0 Release / Production Gate: PENDING.** The pending status represents release
validation, not a known architectural blocker. Clean machines, edition/version coverage,
real Standard User/UAC, installation lifecycle, visual/accessibility QA, signing, final
security and dependency reviews are mandatory before public/commercial release.

Unavailable cases are recorded as **DEFERRED TO RELEASE VALIDATION**, not development
failures and not passed tests. Track them in RELEASE-QA.md. A real reproducible crash or
incorrect/destructive operation still blocks development when it affects the active scope.
This split does not authorize unsafe maintenance features before their own safety tests.

Phase 1 is authorized: application shell and read-only Dashboard foundation only.
Stop after its Development Gate report for owner review before Phase 2.
