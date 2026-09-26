# Startup reversible state and helper protocol (v1)

## UI / application contract

`StartupOperationRequest(SessionId, ItemId, Action, Confirmed)` refers to the current
service-owned ten-minute inventory session. One request consumes that session.
Only one operation runs at a time. Protected entries cannot reach source mutation.
Every action has a confirmation, defaulting to Cancel; RunOnce adds a one-time-action
warning. The view refreshes afterward, including on a reported recovery/conflict.

## State and recovery

- User root: `%LOCALAPPDATA%\SystemCore\StartupState`.
- Machine root: `%PROGRAMDATA%\SystemCore-StartupState-v1`, created with a protected
  Administrators/SYSTEM full-control and Users read-only ACL; unsafe pre-existing
  ownership/ACL/reparse roots are refused, not rewritten.
- Maximum 256 live receipts per root; 1 MiB serialized plaintext per receipt.
  Filename is a validated SHA-256 item ID. File backups are limited to 128 MiB each.
- Receipts record schema version, source snapshot, owner, scope, timestamp and phase:
  Prepared / Disabled / Restoring. Registry data retains exact raw bytes and type;
  files retain identity, metadata and SHA-256; tasks retain the original XML/state.
- DPAPI encrypts/authenticates the receipt. Machine DPAPI alone is not authorization:
  the machine ACL is required. Per-user DPAPI remains within the same-user trust
  boundary. Recovered locations must still match the compiled source roots and ID.
- Atomic state creation/replacement: root-relative native create, Flush(true),
  exact-handle native rename, decrypt/read-back/schema validation. A process-wide
  service gate plus an exclusive per-store lock prevents concurrent SystemCore writes.
- Any failure after preparation retains a recovery receipt. A new service instance
  reads it on refresh. No source rewrite occurs for conflicting newer entries.
  An already-restored exact source can finalize its receipt without being overwritten.
- Successful enable verifies the source and removes only its own receipt. Corrupt
  state remains on disk and marks inventory incomplete; it is never guessed/repaired.
  Inaccessible/missing/changed targets can prevent restoration, with backup retained.
  Power-loss/volume-failure guarantees beyond Windows filesystem/KTM durability are
  not claimed by the process-crash tests.

## Source operations

Registry: fresh allowlisted value -> transactional raw snapshot -> verified receipt ->
exact value removal -> commit/read-back -> Disabled receipt. Restore only into an
empty slot or finalize an exact already-restored value. No key-tree deletion and no
fallback outside KTM if the transaction is unsupported/conflicting.

Files: fresh identity/protection -> exclusive source file handle -> verified receipt
and hash -> root-handle-relative rename with replacement disabled -> identity/hash
read-back. Restore mirrors this flow. Cross-volume moves are refused; there is no
copy/delete fallback. Reparse/unknown file types stay read-only.

Tasks: supported logon/boot definition -> fresh snapshot -> verified receipt ->
official Task Scheduler Enabled property -> state and definition read-back. Settings
Enabled is excluded only when comparing the otherwise unchanged post-toggle XML.
Mutation permissions come from Task Scheduler GetSecurityDescriptor plus Windows
AccessCheck/file-write rights, evaluated against the limited token even in the helper.
RunLevel describes execution privilege and is not treated as proof that editing
requires UAC. Unavailable access metadata makes the task read-only. Security-descriptor
changes are included in fingerprint revalidation and post-toggle checks.
No task registration creation/deletion or command execution occurs. Concurrent task
administrators can race this API; a mismatch is reported as recovery, not success.

## Elevated endpoint

`--startup SystemCore-Startup-<guid> <parentPID> <nonce>` launches the fixed helper
through Windows `runas`. The app hosts a CurrentUserOnly local pipe; client PID must
match the launched helper. The helper requires an administrator token, verifies the
expected sibling App image path and the pipe server PID, and handles one request.

Request fields: version=1, 64-hex nonce, timestamp, 64-hex item ID/fingerprint, action.
Frames are length-prefixed, maximum 4,096 bytes; unknown JSON fields/versions/actions,
expired timestamps, wrong nonce and malformed IDs are rejected. No caller-provided
Registry address, value bytes, file/task path or startup command is accepted.

The helper independently inventories production roots plus protected saved state.
It requires an exact fingerprint match, an elevated source and nonprotected status
before executing one reversible operation. User-only items cannot use this endpoint.
Results are a closed outcome enum. UAC denial/cancellation before dispatch performs no
mutation. After dispatch the client waits for this one operation rather than killing
the helper; timeout/disconnection reports an uncertain/recovery result.

Alternate-administrator credentials fail closed through the same-user pipe. This
version does not reinterpret the administrator profile as the requestor's profile.
The full separate-account/UAC/signing/loader-tamper matrix remains Release QA.

## Diagnostics and privacy

`SystemCore-Startup` EventSource emits source index/count/completeness and operation
intent/outcome only. It is local and opt-in, without commands, arguments or path logs,
network traffic, analytics, watchers, a background service or a periodic scan.

API reference: [Microsoft — Security Contexts for Tasks](https://learn.microsoft.com/en-us/windows/win32/taskschd/security-contexts-for-running-tasks),
especially the distinction between RunLevel and file-write permission to update tasks.
