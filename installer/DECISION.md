# ADR-0001 — experimental phase-0 installer

Updated 2026-09-24 after the owner changed scope to Windows 11 only.

## Decision and implementation

Inno Setup **7.1.0 x64** is selected for the **test fixture**; exact script:
`SystemCore.Phase0.iss`. The compiler was downloaded from its official immutable GitHub
release, checked using Authenticode (publisher **Pyrsys B.V.**, valid), then extracted using
its documented `/PORTABLE=1 /CURRENTUSER` mode to the approved local tooling temp directory.
This does not mean the SystemCore installer was installed on the work host.
Tool acquisition evidence: `artifacts/phase0-validation/inno-tool-verification.json`.

Fixture AppId is fixed and separate from the eventual production identity. Install only to
`%ProgramFiles%\SystemCore-Phase0`; reject overridden locations. Per-machine admin install,
app asInvoker; no post-install elevated launch. Start-menu entry launches as the caller.
MinVersion 10.0.22631; `x64os`; client Home/Pro/Enterprise/Education allowlist. Same payload
for Windows 11 Pro 23H2/24H2/25H2. Default Program Files ACL inheritance; VM must verify ACLs.

Separate App/ElevatedHost runtime folders. Includes exact restored license/notice files.
No service, scheduled task, login item, driver or shared runtime installer. Does not remove
user data or backups. No wildcard UninstallDelete. Files are removed through the installer's
ownership log; unknown user-created files are preserved. Installer logs requested by the tester
are evidence, not unexpected residue. Check Install/Uninstall with a clean snapshot baseline.

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
