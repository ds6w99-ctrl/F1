# Release validation procedures — Windows 11 only

**Unavailable manual cases are DEFERRED TO RELEASE VALIDATION until actual evidence is recorded.**
Historical phase-0 fixture steps below must be adapted to the release artifact and current shell.
These cases do not block the Development Gate solely because the environment is unavailable.
Use clean snapshot
VMs or dedicated test machines, never a shared runner or the everyday work machine for
installer/UAC tests. No VM/hypervisor/credentials are assumed to exist.

## Matrix and provisioning

Release validation: **Windows 11 versions supported per edition at shipping time, x64**. Add Home/Enterprise/Education rows
before claiming validation of those editions. Minimum technical build 22631; recommended 24H2+.
Record exact build/UBR, edition, CPU/RAM/GPU, display, UAC policy, clean snapshot identifier,
and preinstalled runtimes. No Visual Studio/.NET SDK/developer-installed native dependencies.
Use legally obtained installation media. A hypervisor flag alone is not an available test VM.

Create two test accounts **inside each VM**: a genuine standard user not in Administrators,
and a distinct administrator. Also test a split-token admin at medium integrity. Do not
equate a non-elevated admin token with a Standard User account. Credentials stay off logs/chat.
For each VM capture `whoami /groups`, `Get-LocalGroupMember Administrators` (redact identities),
OS build, and absence of developer tooling. Take snapshot **before** installing SystemCore.

## Artifact preparation

1. Run pinned locked restore, Release build, UnitTests, publish both folders, then real-host
   CompatibilityTests. Save native exit codes and full logs.
2. Run `scripts/Build-Installer.ps1` with verified Inno 7.1.0. Copy the exact same installer
   and `publish-inventory.json` to every VM. Record SHA256; hashes do not authenticate publisher.
3. Unsigned phase-0 artifacts are expected. Signing is a documented production deferral.
4. Start an evidence ledger, one row per case. Allowed statuses: PASS, FAIL,
   DEFERRED TO RELEASE VALIDATION (unavailable environment/evidence), N/A (genuinely unsupported feature).

## Install / launch / uninstall sequence

1. Install with logging: `SystemCore-Phase0-0.0.1-win-x64-setup.exe /LOG="C:\TestEvidence\install.log"`.
   Accept UAC interactively. Verify Installed Apps entry and fixed Program Files location.
2. Verify installed file hashes against the manifest. Inspect all binary-folder ancestors
   and payload ACLs: standard users must not have write/delete/modify permissions.
3. From the genuine standard account Start menu, launch. Confirm no UAC for the window,
   correct rendered controls and locale, no missing-runtime messages, no developer-tool dependency.
4. Close and reopen. Confirm process termination, no tray/background leftovers.
5. Restart **the VM**, sign into the same standard account and launch the installed app.
   Record LastBootUpTime before/after. This is not equivalent to restarting the app process.
6. Repair: **N/A**, not advertised. Record that Installed Apps offers no repair promise.
7. Close app; run its registered uninstaller, save log; verify owned files/shortcut/Installed
   Apps key removed. Compare preinstall baseline, preserving unrelated/user files and evidence.
8. Reinstall the same fixture; verify first launch, same hashes and single Installed Apps entry;
   uninstall again. Restore VM snapshot after evidence is collected.

## UI/language/theme/navigation

`scripts/Test-WindowProbe.ps1` operates the actual native window using UI Automation. It tests
the two **probe** tabs, not future product modules. Run it from the standard account against
the installed EXE. It requires no .NET SDK; uses Windows PowerShell UI Automation assemblies.

Manually verify both English and Arabic, all themes, RTL tab/header/control order and LTR
diagnostic values. Full text must wrap, not clip or overlap. Test long text at a small usable
window. Capture actual window screenshots only after confirming the probe is visible.
An automation tree or `ActualTheme` property is not visual proof of proper rendering.

For Mica: test supported GPU/effects-enabled configuration, then transparency disabled, then
High Contrast. Activate/refresh probe after settings changes. Expect Mica only when available;
otherwise readable solid system-theme background. Restore VM settings afterward. Do not force
effects with undocumented APIs. Unavailable Mica alone does not validate the other scenarios.

## UAC (separate non-destructive helper)

The phase-0 UI does **not** call UAC. Its independent token-only helper is exercised through
`scripts/Test-UacProbe.ps1 -DedicatedTestMachine` from the fixed installed location.
Therefore testing this harness does not certify the future in-app privilege broker.

1. Medium-integrity administrator: accept actual consent; helper exit **0**, no writes.
2. Reject actual prompt: harness exit **3/CANCELLED**, no retry. Observe UI remains responsive:
   switch tabs/language/theme, close/reopen it. Record a screen/video showing prompt cancellation
   and responsiveness. No mocked Process.Start or injected error code counts as this test.
3. Genuine standard user: supply the **other** admin account interactively; helper exit 0.
   Record different account roles without passwords; verify neither profile was modified.
4. Cancel credential prompt under standard user. Confirm no crash, freeze, repeated prompt or
   lingering helper; record error/exit. The harness timeout cannot cancel secure-desktop consent.
5. Invoke helper with an unexpected argument from an elevated VM shell: exit 64, no operations.
6. Full in-app accept/deny behavior is **NOT TESTED/not implemented** until a real broker exists;
   if required as a phase-0 gate, it remains a blocker, not an inferred PASS from independence.

## Accessibility and DPI — manual, real OS configurations

At **100%, 125%, 150%, 200%** in the VM's display settings, sign out/reopen if Windows asks.
Record actual per-window DPI (96/120/144/192) from the harness; do not simulate scale with
resizing or claim scaling from a compile-time number. At each setting repeat both languages,
Light/Dark, tab changes and a small viewport. Capture full window and focused control.

Keyboard-only: unplug/ignore mouse, Tab/Shift+Tab, arrows between tabs, Alt+Down and arrows in
combos, Enter/Space refresh, Alt+F4. Expected traversal: Language -> Theme -> active probe tab
-> Refresh -> Language; no trap, hidden focus or off-screen action. Check focus contrast in
both themes/high contrast. UIA focus tracking is structural evidence, not focus visibility.

Narrator: start inside VM; listen to control names/roles/states, language pronunciations,
selected tab/theme and reading order. Require correct localized names and readable values.
Save human observations/audio with permission; a running Narrator process is not a PASS.

## Results ledger

| Test | Pro 23H2 | Pro 24H2 | Pro 25H2 clean | Home/Enterprise/Education | Evidence | Notes |
|---|---|---|---|---|---|---|
| Install/first launch | NOT TESTED | NOT TESTED | NOT TESTED | NOT TESTED | none | No VM supplied |
| Restart + installed launch | NOT TESTED | NOT TESTED | NOT TESTED | NOT TESTED | none | Real boot required |
| UAC accept/reject/different admin | NOT TESTED | NOT TESTED | NOT TESTED | NOT TESTED | none | Real secure-desktop interaction |
| Narrator/focus/DPI | NOT TESTED | NOT TESTED | NOT TESTED | NOT TESTED | none | Human visual/audio evidence required |
| Uninstall/reinstall/residue | NOT TESTED | NOT TESTED | NOT TESTED | NOT TESTED | none | Compare clean baseline |

For each execution add UTC, OS/UBR, account role, snapshot, artifact hashes, expected/actual,
native exit code, log path and screenshot/audio reference. Do not overwrite failures with
later successes; retain both with the fix identified. Current gate policy is in [GATES.md](GATES.md).
