# Current status

- **0.9.0-dev — CURRENT DEVELOPMENT BUILD**.
- Phase 9 Development PASS (historical); owner review / Release / Production PENDING.
- Pre-release fixes: **PARTIAL, not complete**. Cleanup does not grant feature acceptance.
- Preserved partial edits: Sidebar Premium-card removal; `ManageableStartupItems` and Startup view-model filtering; read-only `ScanBenchmark` test mode.
- Pending: complete Startup eligibility/icons, measured Cleaner optimization, bilingual copy reduction, unified game discovery, automatic verified-removal leftover scan, startup data/cache orchestration and all corresponding regression/runtime evidence.
- Game discovery source support: no dedicated launcher providers; existing Win32/MSI/MSIX inventory only.
- Current executable: `artifacts/current/F1/F1.exe`.
- Latest checks: locked restore, zero-warning Debug/Release, x64 App/helper publish, unit smoke and 34 native runtime checks PASS. See `artifacts/project-cleanup/`.
- User settings restored after smoke; no destructive app actions tested. Desktop-token launcher failed; direct current-token native smoke passed.
- Historical gates remain in `artifacts/current-gates.json`; their old links are resolved through `artifacts/project-cleanup/archive-moves.json`. Dates/counts inside them remain historical.
- No Phase 10, installer finalization, signing or production approval.
