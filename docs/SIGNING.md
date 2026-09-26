# Signing architecture — DEFERRED to Release/Production

No real code-signing certificate is available. This is an **explicitly non-critical phase-0
deferral**, not a failing test. Current SystemCore binaries/setup remain unsigned. Do not
make production trust claims or enable privileged mutating operations on this basis.

## Existing signing integration points

- App/helper are normal PE files with embedded manifests; signing does not change project
  architecture, privilege model or target framework. Signed DLLs may be included as needed.
- Build immutable Release publications. Sign owned EXE/DLL payload with an external
  Authenticode service/certificate, SHA256 digest and RFC3161 timestamp, then verify trust,
  certificate chain, expected publisher and expiry/timestamp policy.
- `Build-Installer.ps1 -EnableSigning -SigningCommand <external-command> -PublisherThumbprint <expected>`
  refuses unsigned/unexpected entry executables. No certificate or signing command is embedded
  in source. Thumbprint is a public selector, not a secret.
- Inno `#ifdef EnableSigning` enables `SignTool=systemcore` and `SignedUninstaller=yes`.
  The CLI supplies the tool definition via `--signtool=systemcore=<command>`. Inno's `$f`
  placeholder is the file to sign. Prefer a separately controlled signing wrapper executable,
  not a command containing plaintext credentials. Verify setup after compilation.
- Recalculate the payload inventory **after signing**, then installer SHA256. Save signatures,
  certificate metadata and verification results with the release evidence. Checksum alone
  authenticates nothing. Include nested uninstaller verification in signed VM testing.

## Validation status

Source-level review of the signing branch and PowerShell parsing can be performed now.
Actual signing/timestamp/trust/revocation/nested-uninstaller tests are **DEFERRED**, not PASS.
No self-signed certificate substitutes for a real commercial signature in the results.
No keys, .pfx/.p12, tokens or certificate-store modifications have been added.
Production publisher/legal identity remains unassigned and must be configured before release.
