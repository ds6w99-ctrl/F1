# Current status

- **0.9.0-dev — CURRENT DEVELOPMENT BUILD**.
- Phase 9 Development PASS (historical); owner review / Release / Production PENDING.
- Pre-release fixes: **PARTIAL, not complete**. Cleanup does not grant feature acceptance.
- Preserved partial edits: Sidebar Premium-card removal; `ManageableStartupItems` and Startup view-model filtering; read-only `ScanBenchmark` test mode.
- Cleaner FIX 3: measured optimization implemented (fresh 78.831 s before / 37.947 s after, live-dataset drift disclosed); two bounded category workers, early metadata rejection, unchanged native safety validation. Details/counts and gaps: `PRE-RELEASE-FIXES-01.md`, Cleaner Performance.
- Cleaner FIX 3 complete: shared 150 ms live category/visited/eligible/bytes progress, concise EN/AR display, stale-update guards. Safety/executor regressions PASS; synthetic native lifecycle 26 checks and read-only bilingual progress 25 checks PASS. Automatic scanning remains pending (FIX 7).
- Pending other work: bilingual copy reduction, unified game discovery, automatic verified-removal leftover scan, startup data/cache orchestration and corresponding regression/runtime evidence. Startup FIX 2 results are recorded separately in `PRE-RELEASE-FIXES-01.md`.
- Game discovery source support: no dedicated launcher providers; existing Win32/MSI/MSIX inventory only.
- Current executable: `artifacts/current/F1/F1.exe`.
- Latest FIX 3 checks: locked restore, zero-warning Debug/Release, x64 App/helper and separate Cleaner-validation publication PASS. 580 console PASS assertions; 51 native checks PASS. Real-scan exit 2 and production-capture exit 1 are expected fail-closed behavior, not FIX 3 defects: depth-limited User Temp stays incomplete and its default Clean selection is disabled by design. Archived phase4a/4b scans share the same Incomplete/Limit=2/Complete=false signature. See `PRE-RELEASE-FIXES-01.md` and `artifacts/current/fix3-tests/`. No screenshot visual review claimed.
- User settings restored by native scripts. Cleaner destructive checks touched synthetic fixtures only; real-device scans were read-only. Earlier desktop-token launcher failure remains historical; no Standard User/UAC certification is implied.
- Historical gates remain in `artifacts/current-gates.json`; their old links are resolved through `artifacts/project-cleanup/archive-moves.json`. Dates/counts inside them remain historical.
- No Phase 10, installer finalization, signing or production approval.
