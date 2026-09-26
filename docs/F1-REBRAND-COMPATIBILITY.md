# F1 rebrand compatibility

The App project alone sets `AssemblyName=F1`, with explicit
`RootNamespace=SystemCore.App`. Product version remains **0.9.0-dev**. Resources keep
the `SystemCore.App.Resources.Shell` base name; the satellite assembly is named
`F1.resources.dll`. The output is `F1.exe`, not a renamed standalone apphost.

`Core/ProductIdentity.cs` centralizes display name, author, attribution, tagline and
the executable leaf. Localization uses explicit `{Brand}` placeholders, resolved
before positional formatting. Namespace, project and resource keys are not renamed.

Both Cleaner/Startup helper-parent checks now expect the exact installation-relative
`F1.exe` path. Helper is republished in `ElevatedHost/SystemCore.ElevatedHost.exe`.
Its pinning, path equality, pipe peer PID, nonce, local-only flags, deadlines and
bounded messages remain enforced. Validation fixture copies the new apphost name.

Eight non-mutating parent-identity integration checks passed against byte-identical
copies of the newly published helper (`artifacts/f1-ui/test-results/helper-identity/`). For both
Cleaner and Startup, a fixture parent at the exact installation-relative `F1.exe`
was accepted through pipe connection/peer verification, then its deliberately invalid
Version-0 request was rejected (exit 64). The old `SystemCore.App.exe`, another leaf
name, and `F1.exe` in the wrong directory were rejected before connecting (exit 5).
The probe has no eligible cleanup/startup target and performs no user operation.
It uses an already elevated test controller; this is not interactive UAC or
alternate-account certification.

Compatibility identities retained:

- `%LOCALAPPDATA%/SystemCore`: preferences, StartupState, Backups, Logs, Exports.
- `%PROGRAMDATA%/SystemCore-StartupState-v1`, DPAPI and recovery receipts.
- IPC names, log/diagnostic format names, EventSources, mutexes and owned filename
  prefixes. They also enforce retention/ownership; cosmetic renaming would break it.
- `asInvoker` App / `requireAdministrator` helper and signing publisher identity.

No storage migration is required. Historical phase evidence/publications are retained
and do not validate this UI change. Installer fixture remains historical. The new
UI task does not approve Phase 9 owner review or Phase 10 / production release.

## Runtime implementation notes

R2 caught an invalid shared native PathGeometry between the gauge and its glow.
The fixed component gives each Path an owned, bounded 64-segment geometry and updates
its points from real percentages. Failed publications/probes are preserved under
`artifacts/f1-ui/iterations/r2`; the corrected runtime was launched and closed.
Asset suppression and clean rebuild probes did not resolve the failure; isolating
Dashboard rendering and correcting native geometry ownership did. No exception was
suppressed, no forced GC or dependency upgrade introduced.

Final verification results are recorded in `F1-UI-IMPLEMENTATION-REPORT.md`.
