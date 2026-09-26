# Project cleanup

- **Generated output removed:** 32 bin/obj directory trees under src/tests; source/tests/configuration preserved. Required final builds recreated their current outputs.
- **Archived:** 45 top-level artifact/report moves. Historical phase reports under `docs/archive/phases`; logs, screenshots, prompts, security/reliability evidence and large diagnostic captures under `docs/archive/evidence`.
- **Publications consolidated:** 38 old runnable payload directories converted into ZIP archives; every file verified by length and SHA256 before loose copies were removed. One current runnable publication: `artifacts/current/F1/`.
- **Uncertain artifacts:** retained in archive, including large heap/trace diagnostics and historical installer/test-kit evidence. No blind deletion of unique history.
- **Docs/prompts:** active remaining requirements copied to `docs/PRE-RELEASE-REMAINING-SPECIFICATION.md`; old prompts stay with archived evidence. Root architecture/security/configuration documents and SBOMs/licenses remain.
- **Build:** final locked restore, Debug, Release, x64 App/helper PASS, zero warnings/errors. Logs: `artifacts/project-cleanup/final-build/`.
- **Tests:** 198 unit checks and 34 non-destructive native UI smoke checks PASS. Launch, ten routes, About attribution, absent Premium card and normal close covered; empty app stderr. Desktop-token controller failed; direct current-token execution succeeded. This is not alternate-credential UAC/Standard User validation.
- **Brand:** resource-value scan found no SystemCore product copy; internal XAML namespace/class references retained. Original/project/published PNG and ICO hashes recorded; attribution checked at runtime. No logo regeneration.
- **Current executable:** `artifacts/current/F1/F1.exe` — CURRENT DEVELOPMENT BUILD 0.9.0-dev.
- **Handoff:** `docs/NEW_CHAT_START_HERE.md`; machine state: `artifacts/current/project-state.json`.
- **Known issues:** pre-release fixes remain PARTIAL; see handoff. No new feature work or Phase 10 was performed.
- **Git:** no repository; no commit/push/reset.

## Evidence navigation
`artifacts/project-cleanup/pre-cleanup-file-list.csv`, `pre-cleanup-git-status.txt`, `archive-moves.json`, `archived-payloads.json`, `deleted-generated-directories.txt`, `brand-hashes.json`, `unit-tests.txt`, `runtime/checks.json`. Current payload hashes: `artifacts/current/publish-manifest.csv`.

Historical links intentionally retain original provenance; map old artifact paths through the move ledger or prefix `docs/archive/evidence/`. Old phase reports map to `docs/archive/phases/`. Old payload directories are adjacent `*-payload.zip` archives. Historical scripts may need explicit current AppPath/EvidencePath arguments.
