# F1 — VM / clean-machine acceptance

Purpose: close the clean-machine rows of `docs/RELEASE-QA.md` without the work host.

## What the kit does

`scripts/Test-F1VmAcceptance.ps1` runs **on the target machine** (a fresh Windows 11 x64 VM,
22H2 build 22631+ / 24H2 / 25H2). It records the environment, verifies the release hashes,
performs a silent **install → layout check → launch/close → apps-and-features entry →
silent uninstall → residue check**, and (with `-Mode Both`) a portable extract → run → remove
check. Evidence is a JSON file plus a text summary; exit code 0 = PASS.

## How to run it

1. Copy the release artifacts and this script into the VM (shared folder, ISO, or RDP copy):
   - `F1-1.0.0-win-x64-setup.exe`
   - `F1-1.0.0-win-x64-portable.zip`
   - `Test-F1VmAcceptance.ps1`
2. Inside the VM, in an **administrator PowerShell** (the installer requires admin), from the
   folder that contains the files:

   ```
   powershell -ExecutionPolicy Bypass -File .\Test-F1VmAcceptance.ps1 -Mode Both
   ```

   The script verifies the two SHA-256 hashes against the release record
   (`329241B2…` setup, `7105E41D…` portable) and refuses mismatched payloads.

3. The launcher must run in an **interactive desktop session** — the launch/close check needs a
   real window. Run it from the VM's own console/RDP session, not from a service.

4. Copy `vm-acceptance.json` and `vm-acceptance.txt` back to the work host (they land on the
   VM's Desktop under `F1-VM-Evidence` by default; override with `-EvidencePath`).

## What it deliberately does not cover (still manual / deferred)

- **UAC accept / deny / alternate-admin** on the secure desktop.
- **Standard User** run: create the account, sign in, launch F1 at medium integrity.
- **Windows editions** beyond the VM's own edition (matrix rows).
- **DPI / Narrator / High Contrast** visual passes.
- Reboot persistence (log off, reboot, confirm the installed app and preferences).

Those remain `DEFERRED TO RELEASE VALIDATION` rows in `docs/RELEASE-QA.md` until executed.

## Turning a clean VM into release evidence

Record in the QA log: VM image name/date, Windows build/edition, artifact hashes (from the
script output), PASS/FAIL per row, and the evidence files. The Release Gate itself only moves
once the signing row is closed too; an unsigned PASS on clean machines is strong development
evidence, not a release certificate.
