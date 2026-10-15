# ADR-0001 — experimental phase-0 installer

Updated 2026-09-24 after the owner changed scope to Windows 11 only. Tool re-acquired, layout
contract recorded and the fixture first compiled on 2026-10-16.

## Decision and implementation

Inno Setup **7.1.0 x64** is selected for the **test fixture**; exact script:
`SystemCore.Phase0.iss`. The compiler was downloaded from its official immutable GitHub
release, checked using Authenticode (publisher **Pyrsys B.V.**, valid), then extracted using
its documented `/PORTABLE=1 /CURRENTUSER` mode to the approved local tooling temp directory.
This does not mean the SystemCore installer was installed on the work host.
Tool acquisition evidence: `artifacts/phase0-validation/inno-tool-verification.json`.

Re-acquired 2026-10-16, after an `artifacts/` cleanup deleted that evidence file. Because
`artifacts/` is ignored by Git the file had never been recoverable from history, so the fixed
facts are recorded here too. Release tag `is-7_1_0`, which GitHub marks immutable; asset
`innosetup-7.1.0-x64.exe`, 14,304,168 bytes, SHA-256
`0362a383ed217d4c4239b5933866dd96d3eb2102737da92f80f6057a4b40df2f`, equal to the digest GitHub
publishes for that asset. Authenticode: status Valid, subject
`CN=Pyrsys B.V., O=Pyrsys B.V., S=Noord-Holland, C=NL`, issuer
`Sectigo Public Code Signing CA R36`, thumbprint `E0AB19C8D38CBF9C44709925122A7A02F8C70CB7`,
expires 2028-10-03. The extracted compiler reports 7.1.0, is itself validly signed by the same
publisher, and left no uninstall entry, no Start-menu entry and nothing under Program Files:
portable, as this ADR requires.

Fixture AppId is fixed and separate from the eventual production identity. Install only to
`%ProgramFiles%\SystemCore-Phase0`; reject overridden locations. Per-machine admin install,
app asInvoker; no post-install elevated launch. Start-menu entry launches as the caller.
MinVersion 10.0.22631; `x64os`; client Home/Pro/Enterprise/Education allowlist. Same payload
for Windows 11 Pro 23H2/24H2/25H2. Default Program Files ACL inheritance; VM must verify ACLs.

The runtime layout is **nested, not sibling**: the app is installed to `{app}\App` and the helper
to `{app}\App\ElevatedHost`. `HelperLaunchLease` resolves the helper as
`<app directory>\ElevatedHost\SystemCore.ElevatedHost.exe` from `AppContext.BaseDirectory`, and
`SecurityTests` (helper path is installation-relative) and `CompatibilityTests` (the helper
runtimeconfig is under `<publication>\ElevatedHost`) both pin that relationship. A helper placed
at `{app}\ElevatedHost` is installed where the application never looks, which silently removes
elevated leftover deletion on an installed product. `App/ElevatedHost` in
`docs/CLEANER-PROTOCOL.md` denotes that same nesting relative to the app directory.

Includes exact restored license/notice files. No service, scheduled task, login item, driver or
shared runtime installer. Does not remove user data or backups. No wildcard UninstallDelete.
Files are removed through the installer's ownership log; unknown user-created files are preserved.
Installer logs requested by the tester are evidence, not unexpected residue. Check
Install/Uninstall with a clean snapshot baseline.

On 2026-10-16 the fixture compiled for the first time, and while checking where a silent
`/DIR`-overridden run had put its payload it was found to have self-elevated and installed itself
at the fixed path on the work host. The installed tree was inspected and then removed with its
own uninstaller; the Program Files directory, the uninstall entry and the Start-menu shortcut are
all gone. That inspection confirmed `App\F1.exe` with the helper at `App\ElevatedHost\...` and no
sibling `ElevatedHost\`. It remains a fixture, not a release installer.

Repair UI is **not supported (N/A)**. Reinstallation is a separate scenario, not a repair promise.
Clean-machine installation/upgrade/removal validation is pending; this is not a release installer.

## Licensing

Reviewed exact 7.1.0 license:
https://raw.githubusercontent.com/jrsoftware/issrc/is-7_1_0/license.txt

Custom permissive terms allow commercial use but require notice retention, origin accuracy
and marking modified copies. Official download page also requests purchasing a commercial
license for commercial use; no purchase or commercial deployment is authorized here.
Review that policy and all embedded third-party terms before production. Compiler resources
carry their own notices; do not strip them. Tool source/setup options verified in
https://raw.githubusercontent.com/jrsoftware/issrc/is-7_1_0/setup.iss.

## Signing

Signing is **DEFERRED to Release/Production**, non-blocking for phase 0 without a certificate.
The conditional `EnableSigning` build config signs setup and uninstaller using an externally
configured SignTool. `Build-Installer.ps1` first verifies already-signed app/helper payload
against an explicitly supplied certificate thumbprint, and verifies the installer after build.
See `docs/SIGNING.md`. No key/certificate secret belongs in source. No architecture change
is needed to enable the signing pipeline; actual signing verification remains deferred.
