# SystemCore 0.9.0-dev — release-candidate preparation draft

Development-only, unsigned Windows 11 x64 publication. Phase 9 review candidate;
not a production release. Installable artifacts now exist — the per-machine installer from
`installer/F1.iss` and a portable archive, both described in `docs/INSTALL.md` — but
signing, clean-VM/edition/accessibility validation and the final soak still belong to
Phase 10 after owner authorization.

## Reliability

- Removed repeated exception-driven protected-process time queries and locked-file
  scan probes. Unknown metrics remain unavailable, never fabricated as zero.
- Added provider retry cooldown, numeric failure boundaries, owned activation tasks
  and awaited window shutdown. Optional unpackaged high-contrast event failure is
  explicitly bounded; language/theme/high-contrast rechecks remain functional.
- Added separate bounded local exception-audit publication for development evidence.
  Ordinary App publication does not contain first-chance audit instrumentation.

## Security and state integrity

- Pinned expected helper launch path and separated arguments; closed typed IPC
  operations, current-user pipes and native source revalidation preserved.
- Bounded Registry inventory reads and enumeration, stricter corrupt-state checks,
  and reparse-safe preference persistence.
- Diagnostics ZIP verifies before atomic publication; interrupted/cancelled writes
  do not report success. Status refresh runs away from the UI thread.
- Updated dependency review/SBOM and tested malformed inputs, paths and recovery.

Existing feature/safety policies remain: official Windows tools only, reversible
Startup changes, conservative confirmed Cleaner/Uninstaller operations and manual
Memory optimization restricted to eligible SystemCore-owned caches. Production
registers no such cache owner and legitimately returns a no-op.

## Known scope limitations

No new features, telemetry, network upload, updater, accounts or background cleanup.
The observed runtime evidence is workload-specific, not a general leak-free claim.
Unexpected native/provider hangs are not forcibly killed. Hard termination can leave
bounded pending export files; use the local Exports folder to inspect/remove them.
Real distinct Standard User/UAC credential matrix and final production validation
remain pending. See [docs/GATES.md](docs/GATES.md) for current gates.
