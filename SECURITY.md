# Security design and threat model — phase 0

Historical Phase-0 design below. Phase 4 now implements manual file deletion,
Shell Recycle Bin handling and a bounded Windows Temp helper. Current boundaries,
implemented checks and limitations are documented in [CLEANER-PROTOCOL.md](docs/CLEANER-PROTOCOL.md).
Release security validation remains pending; proposals below are not claims of
implemented release hardening.

## Phase 8 implemented boundary

Windows tool launches accept only closed catalog IDs. Targets are reconstructed from
the trusted system directory or fixed official Windows URIs; executable arguments
remain separate. The application does not request elevation for itself or accept a
shell command string. Availability and launch failure remain explicit UI outcomes.

Local diagnostic records contain known feature/event identifiers, integer codes and
timestamps. No raw event payload, path, command argument, process list, network
address or machine identifier is recorded. Manual export repeats allowlist validation
and typed serialization rather than copying arbitrary log lines. ZIP creation is
local; there is no upload endpoint. Logs/exports have explicit count/size policies.

Old-log clearing requires the exact app-log filename shape, expected header, pinned
non-reparse ancestry, a single hard link and an exclusive handle. Active or locked
files are retained. A per-user log header is a scope check, not cryptographic proof
against malware with the same user's write access. Cleanup never targets backups,
preferences, recovery receipts, exports or recursively discovered files.

## Phase 7 implemented boundary

Memory Tools reads official performance/process APIs under the app token. Its
manual optimizer has no privilege broker, process-write handle, memory-purge API,
page-file modification or forced-GC path. Production has no registered releasable
cache owner. A future owner must prove versioned exclusive ownership and atomically
refuse release while active; user-supplied process IDs/paths cannot become actions.
Only directly known owner-released bytes are reported, independently of RAM changes.

Process details use QUERY_LIMITED_INFORMATION, PID plus creation time and fresh
exit/image checks. Opening a location uses the existing pinned safe Shell folder
selection path, never executable launch. Protected/inaccessible details remain
unavailable. Process data stays local; operational logging excludes names and paths.
The injected synthetic cache/pressure UI is compile-time-only (`MemoryValidation`)
and excluded from the production assembly.

## Phase 6 implemented boundary

Startup accepts session/item IDs plus an explicit enable/disable intent. It never
executes source commands, deletes task registrations or permanently deletes Startup
files. Protection, identity and source data are revalidated. Run/RunOnce removal is
limited to the selected value after a verified raw-byte backup and within a Registry
transaction. Restore refuses conflicting slots. File moves bind exact handles and
reject cross-volume moves, reparses, multiple links and changed metadata/content.

Receipts are DPAPI-protected, versioned, bounded and atomically replaced. Per-user
state is within that user's trust boundary; DPAPI is not authentication against
malware already running as that user. Machine state additionally requires a protected
Administrators/SYSTEM-owned ACL, denying nonprivileged writes. The elevated endpoint
accepts IDs/fingerprints only and reconstructs its own scope. Same-user pipe and exact
peer-process checks complement, rather than replace, its independent source policy.
Unsigned developer publications are not claimed to have production loader integrity.

Startup-specific namespace-write leases permit directory writes but deny rename/delete.
Every directory/object is opened without following reparses and its resolved name is
checked; namespace mutations use the validated directory handle. Pending backup files
are created relative to that handle and failure cleanup uses their original handle.
Cleaner/Uninstaller retain their stricter default directory sharing.
Task Scheduler lacks an atomic definition-and-Enabled compare/exchange; immediate
definition revalidation and post-write verification detect changes, but do not promise
atomicity against a concurrent task administrator. See
[STARTUP-PROTOCOL.md](docs/STARTUP-PROTOCOL.md) for recovery/elevation limits.

## Phase 5 implemented boundary

Native uninstall launches only an exact selected identity from a short-lived service-owned
inventory after fresh metadata validation. Missing/ambiguous commands fail closed; no
command interpreter or caller-supplied executable launch API is exposed by the UI contract.
Original installer processes are not killed. Completion requires inventory identity absence.

Leftovers require an internal pre-uninstall ownership snapshot and verified removal receipt.
UI requests contain session/application/item IDs only. Uncertain/shared/protected/reparse
objects are rejected, and filesystem changes use pinned ancestry/exact object handles.
Registry ancestors and leaves are opened without link traversal; typed snapshots plus
last-write metadata are checked in a Windows transaction. Durable verified local backup
is mandatory before commit. Failure cannot fall back to generic recursive deletion.

All items default unselected; Registry selection adds an explicit warning about recovery.
Current-token permissions apply: Access denied is skipped. No elevated leftover broker,
generic Registry cleaner, service/driver/task deletion or automatic restoration is provided.
Release threat-model testing and real installer matrices remain pending.

## Assets and adversaries

Assets: user files and settings, registry integrity, original user identity, consent scope,
backup manifests, operation journals, installed binaries, elevated token, release signing keys.
Adversaries: an unprivileged local process (including same-user malware), tampered HKCU
uninstall metadata, malicious filenames/links, concurrent file replacement, spoofed broker
endpoints and messages, replaced DLLs, interrupted writes, untrusted update payloads.
An administrator or kernel attacker can already replace the application; do not claim
protection against a fully compromised privileged OS. Same-user secrets alone are not authentication.

## Trust boundaries

1. UI / scanner / per-user settings: untrusted input to all executors.
2. Windows providers / Registry / external uninstallers: data may be stale or hostile.
3. Standard user -> UAC -> elevated host: no inherited open-ended authorization.
4. User-writable folders -> protected installation / policy: cannot load code or expand policy.
5. Scan-time object -> execution-time object: paths are not stable identity.

## Production IPC proposal (must be implemented and tested before phase 4)

- Local named pipe with explicit ACL scoped to original user SID, SYSTEM and required
  elevated identity; deny remote connections. Never use permissive Everyone ACLs.
  Scope to logon SIDs and grant individual pipe rights, not GENERIC_WRITE (which also
  grants FILE_CREATE_PIPE_INSTANCE); the default pipe DACL can allow Everyone/anonymous read.
- Create server pipe with first-instance semantics; bind an operation ID and one-use
  challenge to the expected launched process handle, PID + creation time and session.
- On both sides validate peer PID using documented named-pipe APIs; hold a process handle,
  inspect token SID/session/integrity, canonical protected image path and verified publisher.
  A random secret or matching PID alone is insufficient. Final publisher identity remains
  unset until actual code signing exists; privileged mutation stays disabled meanwhile.
  Image identity does not prove absence of same-user code injection. Independent host
  allowlists and trusted operation-specific consent remain necessary even for a valid peer;
  consent binding and anti-injection residual risk require review before approving the protocol.
- For alternate-account UAC, distinguish the original requestor SID/logon session/profile
  from elevated administrator SID. ACL and handshake must allow only the expected pair.
  Never substitute `Environment.GetFolderPath` in the elevated account for the original profile.
- Protocol v1: length-prefixed strict typed messages; maximum 64 KiB frame, 30-second
  handshake deadline, bounded item count; reject unknown versions, operations, duplicate
  request IDs, expired consent and extra fields. Fragmentation/oversized input tests required.
- Permit one operation/approved immutable plan per host lifetime; hash/version/item identities
  bound to consent. The helper reconstructs eligible scope from trusted policy and original
  user token, not arbitrary JSON. No ExecuteCommand, DeleteAnyPath or WriteAnyRegistryKey.
- Fail closed on peer ambiguity, cancellation, broken pipe, validation failure or backup
  failure. No repeated UAC, service, token caching, SeDebugPrivilege or UAC bypass.
- Use protected Program Files installation with audited ACLs on **all** path components;
  signed helper and dependency verification; safe DLL search and no plugins/configurable
  assemblies/environment-directed code loading. Review .NET runtime environment injection.
  Protected executable path alone does not establish a safe loader chain.

## Mutation policy requirements

`SafetyPolicy` in both processes uses narrow compiled rules, per-rule versions and bounded
roots. Reject empty/root/UNC/device paths, traversal, ADS, ambiguous normalization and any
reparse point in ancestor/object paths. Multiple hard links or ambiguous ownership are skipped.
Open handles with suitable sharing restrictions, verify final canonical path + volume/file ID
and metadata against the scan identity, and perform supported handle-based mutation with
TOCTOU analysis. Never rely only on string StartsWith/GetFullPath or recursive directory deletion.
Allowlist user temp files only after content/category eligibility beyond age, conservative
7-day threshold, live identity revalidation, exclusions and locked-file checks.

Mandatory negative tests precede destructive implementation: protected paths, Unicode,
long paths, links/junctions/mounts, hard links, concurrent replacement, changed/open files,
access denied, disk full, denied UAC, different administrator, interruption and restore conflicts.
Run mutation/elevation tests in revertible VMs or dedicated machines, never this work host.

Backup/quarantine: capacity and retention bounds, ACL-protected manifest, source/object identity,
hash and registry type/hive/view. Journal steps durably. Restore is a separate allowlisted
operation; never silently overwrite newer files/settings. If required backup fails, deny mutation.
System Restore does not recover permanently deleted personal files.

External uninstallers have their own effects outside SystemCore policy. Validate provenance
and exact executable/argument parsing without cmd/PowerShell or invented silent flags.
Never auto-elevate HKCU commands. Ambiguous commands fall back to Windows settings.

## Phase-0 probe boundary

ElevatedHost has no IPC and accepts **zero arguments**. It reads its own Windows token,
returns 0 only if membership in the Administrators role is effective, otherwise 5; unexpected
arguments return 64. It neither modifies files/Registry nor spawns other processes.
`scripts/Test-UacProbe.ps1` is a manual dedicated-VM harness, **not** the production broker.
The timeout begins after Start-Process returns; it cannot cancel a secure-desktop UAC dialog.
A successful exit proves only the token check, not peer authentication or production IPC safety.
Do not turn this probe into a mutating helper incrementally without the full boundary above.

## References reviewed / design API references

Reviewed 2026-09-24: Microsoft App SDK self-contained deployment and backdrop documents
linked in COMPATIBILITY.md; named-pipe ACL guidance, GetNamedPipeClientProcessId,
GetFinalPathNameByHandleW and DLL security pages below. The server-PID and UAC links
are additional implementation references, not evidence of a finished security review:

- https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights
- https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeclientprocessid
- https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeserverprocessid
- https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew
- https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-security
- https://learn.microsoft.com/en-us/windows/win32/secauthz/user-account-control
