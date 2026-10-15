# ADR-0001 — experimental phase-0 installer

Updated 2026-09-24 after the owner changed scope to Windows 11 only. Tool re-acquired, layout
contract recorded and the fixture first compiled on 2026-10-16.

This ADR governs the **phase-0 fixture** (`SystemCore.Phase0.iss`). The owner subsequently asked
for a build that can actually be installed on another machine, so a **separate** product spec,
`installer/F1.iss`, was added and is recorded near the end of this file. The fixture is not that
build, and its rules — fixed location, overridden locations rejected — continue to apply to the
fixture alone. See `docs/INSTALL.md` for the user-facing instructions.

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

## Distribution spec `F1.iss` (0.9.0-dev)

`installer/F1.iss` is the installable product build. It is deliberately unlike the fixture:
its own AppId `{{81C8CF82-BB21-4324-A51C-22C7C9F14222}` (the fixture AppId stays reserved for
the fixture), no fixed-path enforcement so the wizard directory page and `/DIR` both work, and
`AppName`/`AppPublisher`/`AppVersion` taken from the real product identity. It keeps the
fixture's correct parts: per-machine admin install, app asInvoker with no post-install elevated
launch, `MinVersion=10.0.22631`, `x64os`, and the client Home/Pro/Enterprise/Education allowlist
that also mirrors `CompatibilityRules.IsSupportedEdition`.

`scripts/Build-Installer.ps1` now takes `-Spec` (default `installer/F1.iss`) and hashes whatever
the compiler produced, so one script builds either spec.

Verified on the work host on 2026-10-16:

- Compiler: the pinned 7.1.0 portable tool from this ADR. Result
  `artifacts/installer/F1-0.9.0-dev-win-x64-setup.exe`, 80,158,807 bytes, SHA-256
  `6A3AA6A22347AFB5395A1251DEF8B288D1CD4835060B2CA57650E02D74B7B744`.
- Silent install (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`) to the default `{autopf}\F1`:
  exit 0. Layout confirmed: `App\F1.exe`, `App\F1.pri`,
  `App\ElevatedHost\SystemCore.ElevatedHost.exe`, `Notices\THIRD-PARTY-NOTICES`,
  `Notices\licenses` (23 files), and **no** sibling `ElevatedHost\` at the install root.
  Apps & features entry: `F1 0.9.0-dev`, publisher `Falcone`, uninstaller
  `C:\Program Files\F1\Uninstall\unins000.exe`. Start-menu shortcut present at
  `%ProgramData%\Microsoft\Windows\Start Menu\Programs\F1.lnk`.
- `scripts/Test-Shell.ps1` run against the **installed** artifact
  (`C:\Program Files\F1\App\F1.exe`): **118 checks, 118 passed, 0 failed**, including the real
  Cleaner scan (44.9 s) and a relaunch with saved preferences.
- Portable path: `scripts/Build-Portable.ps1` produced
  `artifacts/portable/F1-0.9.0-dev-win-x64-portable.zip` (813 entries, 114,643,941 bytes,
  SHA-256 `C54F2997D33B2017F25A70D6B3B51938FF330B16CE90BE778287296FCECF9C86`). Extracted to a
  fresh directory, launched, drew its window, and wrote nothing into its own folder.

Two facts worth keeping, both cost real debugging:

- **Inside a `[Code]` section the comment marker is `//`, not `;`.** A `;` there terminates a
  Pascal statement, so a `;` comment fails compilation with `'BEGIN' expected` pointing at the
  comment line. That is why the fixture carries no comments in its `[Code]` section.
- **A production publication writes nothing into its own directory.** Every state path is under
  `%LOCALAPPDATA%` or `%PROGRAMDATA%`, so installing under Program Files is safe.
  `StartupValidationEnvironment` does copy the executable beside itself, but it is compiled under
  `STARTUP_VALIDATION` and is absent from a production publication.

This remains work-host evidence. It does not close the clean-machine, Standard User, real UAC
prompt or signing rows in `docs/RELEASE-QA.md`.

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
