# Cleaner execution boundaries (Phase 4)

## Normal application

The application manifest remains `asInvoker`. Manual scan uses known-folder
roots, opens files read-only/exclusively for identity, and never starts UAC.
`WindowsCleanerService` serializes operations and owns the accepted session.
UI sends only `SessionId`, selected item GUIDs, and an explicit Recycle Bin flag.
No public request can supply a root, path or replacement file metadata.

Sessions expire after ten minutes, are replaced on a new scan, and are consumed
on clean. Leaving the Cleaner page or changing language invalidates the clean
session. Incomplete/cancelled categories do not authorize their discovered IDs.
Definitions are resolved again for execution; the scanner's result is not itself
permission to delete a path without current native validation.

## File execution

The native path lease opens/pins every ancestor, refuses reparse attributes and
checks the handle-resolved path. Final files require exclusive read/DELETE access.
Volume/file ID, creation and write times, link count, size, type, age and allowlist
are checked again. FileDispositionInfo acts on the **same validated handle**;
there is no second pathname-based deletion call. No directories are deleted.
Logical lengths of successfully removed files form confirmed actual bytes.
This is not a net-free-space benchmark or a count of filesystem allocation units.

Inaccessible, changed, locked, replaced, reparsed, sparse/compressed and uncertain
files are not forcibly cleaned. No ACL/ownership change, unlock, process kill,
restart/reboot deletion, external engine or command interpreter is involved.

## Windows Temp broker

Production layout:

```
App/F1.exe
App/ElevatedHost/SystemCore.ElevatedHost.exe
```

The only operational helper mode is `--cleaner <random-pipe-name> <parent-pid>
<nonce>`. These arguments contain no file paths or commands. The no-argument
Phase-0 token probe is retained. Arbitrary modes/arguments are rejected.

The application starts the fixed helper with Windows `runas` **only** for a
confirmed Windows Temp selection whose request passes validation and byte limits.
The helper requires an elevated token, checks the parent's executable against
the fixed sibling App path, and checks the named pipe server PID. The main
application checks the connected pipe client PID against the process it started.
The server pipe is current-user-only and single-instance. A fresh 256-bit nonce
binds the request to this invocation. Alternate-administrator credentials do not
get a permissive ACL fallback; they fail closed.

Wire protocol v1: 4-byte little-endian length followed by UTF-8 structured JSON.
Maximum frame: **1 MiB**; maximum items: **2,000**; maximum JSON depth: 12.
Unknown JSON fields, malformed/truncated frames and oversized frames are rejected.
No persistent broker, request file, arbitrary executable or path-delete endpoint
exists. A helper instance handles one bounded request and exits.

Request: version, nonce, nonempty scan-session ID, completion timestamp and items
with GUID, WindowsTemp category, relative path and expected native identity.
The helper resolves the Windows known folder and appends **Temp itself**, not a
root supplied by IPC. It rejects absolute/UNC/device/ADS/traversal paths, category
mismatch, duplicates, invalid metadata, stale requests and unsafe boundaries.
It then applies the same native lease/identity checks independently. A scan that
cannot read Windows Temp provides no selectable items; UAC is not used to bypass
the scan/preview requirement. Current-host elevated destructive validation is
deferred to a clean VM.

Operation/connect timeout: two minutes. Cancellation after sending writes a
single cancel byte; the helper stops future items and returns partial results.
The application waits for those results rather than inventing a rollback.
Disconnect/timeout after submission means **unknown outcome**: no estimated size
is promoted to actual removed bytes. UI displays the uncertainty and requests a
rescan. Responses must match version, selected GUIDs, cardinality and valid byte
counts before their deletion results are accepted.

Synthetic tests use a friend-test-only trusted target injection under newly
created User Temp fixtures. That injection is not reachable through the helper's
arguments, environment or wire protocol. The framing, request validator and
native file executor are the production implementations. Synthetic pipe tests
do not claim real UAC or elevated Windows Temp certification.

## Recycle Bin

`SHQueryRecycleBinW` and `SHEmptyRecycleBinW` run on a short-lived initialized STA
through a testable `IRecycleBinShell`. The x64 SHQUERYRBINFO layout uses 8-byte
packing (24 bytes), consistent with the Windows SDK shellapi.h x64 definition.
No code enumerates or deletes `$Recycle.Bin` files.

Shell supplies scan item count and bytes. Selection defaults off. The native
confirmation states that **all current contents** will be permanently emptied;
these are not individual file-session IDs. A changed query count/size causes a
skip. Equal aggregate counts cannot prove unchanged contents: the entire-bin
scope is disclosed explicitly, and Shell operates on current contents.
Once Shell starts, it cannot be rolled back or interrupted by this application.

SHEmptyRecycleBin reports success/failure, not an authoritative removed-byte or
item count. The UI reports its result separately; its estimate is never added to
actual file-deletion totals. Automated empty calls use fakes only. Real emptying
is **DEFERRED TO CLEAN VM / RELEASE QA**.

## Scanner / supported scope

200,000 visited entries per category, 500,000 total, depth 32, conservative
240-character paths, 64-bit checked sizes and 100 UI preview rows per page.
An entry/depth limit means Incomplete and disables that category for cleaning.
Access-denied child subtrees are explicitly skipped, not counted as eligible or
silently deleted. Complete means traversal of accessible scope finished within
limits; it does not mean every protected child was readable. Root access failure
means Unavailable. Dashboard partial results are labeled Partial scan.

Browser caches and user WER are **UNSUPPORTED IN V1**: disabled rows, no guessed
profile roots and no browser personal-data deletion.

FIX 3 scan-only optimization uses nonrecursive streaming directory records to
reject reparses, unsupported attributes/patterns and recent files before exclusive
native candidate opening. These records never authorize cleaning: accepted files
still undergo the original pinned-ancestor/identity/age checks. Two independent
category workers share synchronized global entry/byte budgets; deletion remains
serial. Category results retain enum order, cancellation is checked inside each
directory loop, and incomplete categories still cannot authorize their IDs.
Live progress uses aggregate scan counters and a shared 150 ms delivery gate,
with immediate category start/end transitions. Enumeration stays off the UI thread;
queued progress from a cancelled or superseded scan is ignored. Progress is display
data only and never authorizes deletion or changes a category's safety status.

## Validation application

`-p:CleanerValidation=true` produces a separately published validation App.
Conditional code adds fixed synthetic-fixture controls and substitutes the
target provider; it does not expose arbitrary paths. It exercises the normal
page, paging, confirmation, scan and native executor, plus a fake Shell query.
Production publications explicitly use `-p:CleanerValidation=false` and are
checked for absence of the validation controls. Core/Infrastructure hashes are
compared between the final validation and production artifacts. Test rows and
event-subscription counters are bounded; no Cleaner timer is introduced.
