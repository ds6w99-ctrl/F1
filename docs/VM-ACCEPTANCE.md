# F1 — VM / clean-machine acceptance

Purpose: close the clean-machine rows of `docs/RELEASE-QA.md` without relying on the work host.

## Result to date (2026-10-18)

A fresh Hyper-V VM was provisioned from the official Windows 11 Enterprise Evaluation ISO and
exercised:

| Check | Result |
|---|---|
| Guest OS | Windows 11 Enterprise Evaluation, build **26300** (25H2), x64, no SDKs/dev tools |
| Portable archive SHA256 | verified (`7105E41D…`) |
| Portable extract + core payload (F1.exe / F1.pri / ElevatedHost) | PASS |
| Portable launch | real window titled **"F1"** in the interactive session, closed cleanly |
| Installer edition gate | correctly refused the evaluation edition (`EnterpriseEval`) |
| Installer silent fail-fast | exits in ~9.5 s with a non-zero code (no hang) |

Evidence lives under `artifacts/vm-validation/` (host logs) and the guest `C:\F1Evidence\`.

## How it works

1. **`scripts/Invoke-F1VmValidation.ps1`** (elevated, host) — creates a Gen2 VM, then drives
   the rest of the flow.
2. **`scripts/Invoke-F1VmOfflineProvision.ps1`** (elevated, host) — applies `install.wim`
   directly to the VM disk (avoids the DVD "press any key" prompt), runs `bcdboot`, and stages
   `scripts/vm/autounattend-offline.xml` (specialize + oobeSystem: local admin `F1Admin`,
   autologon, WinRM enable).
3. The orchestrator waits for WinRM, copies the release artifacts plus
   **`scripts/Test-F1VmAcceptance.ps1`**, and runs the acceptance inside the guest.
4. Because a WinUI app needs a real desktop, the **launch** check runs interactively:
   `scripts/vm/launch-interactive.ps1` is invoked from the autologon user's `Run` key and the
   guest is rebooted, so the app starts in the console session and reports its window title.

### Host prerequisites (all recorded in this run)

- Hyper-V feature enabled (**reboot required** — `vmms` only registers after it).
- Host WinRM service started (`winrm quickconfig -q`); client `TrustedHosts=*` and
  `AllowUnencrypted=true` for the Basic fallback. Negotiate works once the service is up.
- A Windows 11 ISO at `G:\ISO\Win11Eval.iso`.

## Evaluation-edition caveat

The Microsoft **evaluation** ISO reports `EditionID = EnterpriseEval`, which is *not* in the
installer's client-edition allowlist (correctly). Consequently:

- **Portable** acceptance works on the clean VM.
- The **installer** install/uninstall lifecycle can only be exercised on a supported retail
  edition (Home/Pro/Enterprise/Education); on the eval edition the installer refuses — now
  failing fast instead of hanging. Retail-edition install/uninstall and the Standard User/UAC
  matrix remain `DEFERRED TO RELEASE VALIDATION` rows in `docs/RELEASE-QA.md`.

## Still manual / deferred

- UAC accept / deny / alternate-admin on the secure desktop.
- Standard User run (medium-integrity launch).
- Windows editions beyond the VM's own edition (matrix).
- DPI / Narrator / High Contrast visual passes.
- Reboot persistence of an installed copy.

## Note on the app's own edition gate

`CompatibilityRules.IsSupportedEdition` exists in `SystemCore.Core` and is exercised by
`CompatibilityTests`, but it is **not wired into the application startup path** — the runtime
edition gate lives only in the installer. The portable app therefore starts on any Windows 11
x64 build; documentation claiming the app "refuses twice" should be reconciled with this.
