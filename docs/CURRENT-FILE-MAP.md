# Current file map

| Location | Purpose |
|---|---|
| `src/SystemCore.App/` | WinUI shell, page view models/views, services, localization and DesignSystem |
| `src/SystemCore.App/Assets/Brand/` | Official `F1_Logo.png`, `F1.ico`; preserve unchanged |
| `src/SystemCore.Core/` | Models/contracts/policies; Startup manageability and Uninstaller presentation eligibility |
| `src/SystemCore.Infrastructure/` | Windows native providers, Cleaner, Startup, Uninstaller/security |
| `src/SystemCore.ElevatedHost/` | Bounded elevated helper; keep installation-relative identity checks |
| `tests/` | Existing console regression projects; tests can mutate dedicated fixtures, inspect switches first |
| `scripts/` | Build, native UI/regression tools. Historical defaults may reference archived outputs; pass current paths |
| `docs/` | Handoff, status, current specifications, protocols, SBOMs/licenses and release rules |
| `docs/archive/phases/` | Completed phase reports, including security/reliability evidence summaries |
| `docs/archive/evidence/` | Historical artifact trees, diagnostics, reference images, prompts/specifications and payload ZIPs |
| `artifacts/current/F1/` | Single current runnable development publication, including assets/helper |
| `artifacts/current/review/` | Last reviewed Uninstaller screenshots; predate partial pre-release source changes |
| `artifacts/current/project-state.json` | Machine-readable current state |
| `artifacts/project-cleanup/` | File manifest, move/payload ledgers, build/brand/smoke evidence |
| `installer/` | Existing installer source/config; cleanup did not finalize it |

Root project metadata (`SystemCore.sln`, `global.json`, Directory props, NuGet.Config), README, architecture/security/compatibility, dependency notices and roadmap are intentionally retained. Assets live inside the App project; there is no required root assets folder.

## Build and smoke
SDK: `C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk\dotnet.exe`, exact SDK 10.0.401 from `global.json`.

PowerShell (without changing execution policy):
```powershell
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Build-F1Candidate.ps1)))) -EvidencePath artifacts/project-cleanup/build -PublishPath artifacts/current/F1
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-CleanupSmoke.ps1))))
```

Build wrapper uses existing `ReliabilityAudit=false` configuration. This is a development build, not a new reliability audit. Final build recreates bin/obj after generated-output cleanup.

## Historical references
Old `artifacts/<name>/...` paths map to `docs/archive/evidence/<name>/...`. Old `docs/PHASE-*.md` map to `docs/archive/phases/PHASE-*.md`. Runnable historical directories now have adjacent `*-payload.zip`; archive verification compared each extracted-stream SHA256 to the original file before removing the loose copy. Historical reports are retained verbatim; use the move ledger to resolve their paths. Do not run old payloads as current builds.
