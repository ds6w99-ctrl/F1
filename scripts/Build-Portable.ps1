param([string]$Version = '0.9.0-dev')
# Produces a no-install portable archive: extract anywhere and run App\F1.exe.
#
# Why this exists next to the installer: the installer needs administrator rights because it
# writes to Program Files. Some target machines (locked-down or shared) do not grant that, and
# the application itself is self-contained and asInvoker, so no install is actually required to
# run it. This archive is that path.
#
# Layout contract is the same nested one the installer and the tests pin: the app executable and
# F1.pri sit at the archive root, and the elevated helper stays inside ElevatedHost\ so
# HelperLaunchLease resolves it from AppContext.BaseDirectory.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $app = 'artifacts/publish/App'
    foreach ($required in @("$app/F1.exe", "$app/F1.pri", "$app/ElevatedHost/SystemCore.ElevatedHost.exe")) {
        if (!(Test-Path $required)) { throw "Build and publish the app first; missing $required" }
    }
    $stage = 'artifacts/portable/F1'
    if (Test-Path 'artifacts/portable') { Remove-Item 'artifacts/portable' -Recurse -Force }
    New-Item -ItemType Directory -Force $stage | Out-Null
    Copy-Item "$app/*" $stage -Recurse -Force
    # Notices travel with the payload so the redistributed dependencies keep their attributions.
    Copy-Item 'THIRD-PARTY-NOTICES' "$stage/THIRD-PARTY-NOTICES" -Force
    Copy-Item 'docs/licenses' "$stage/licenses" -Recurse -Force

    $readme = @"
F1 $Version - portable build (Windows 11 x64, version 23H2 or later).
Extract this folder anywhere and run F1.exe. No installation and no administrator rights are
needed to start. Administrator rights are only requested if you ask F1 to remove leftovers that
live outside your user profile.
This build is not code-signed, so Windows SmartScreen may warn once on first run.
Third-party attributions: THIRD-PARTY-NOTICES and the licenses folder.
"@
    Set-Content "$stage/PORTABLE.txt" $readme -Encoding UTF8

    $zip = "artifacts/portable/F1-$Version-win-x64-portable.zip"
    $tar = Get-Command tar.exe -ErrorAction SilentlyContinue
    if ($tar) {
        & $tar.Source -a -c -f $zip -C 'artifacts/portable' 'F1'
        if ($LASTEXITCODE -ne 0) { throw 'Portable archive creation failed.' }
    } else {
        Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
    }
    if (!(Test-Path $zip)) { throw 'Portable archive was not produced.' }
    Get-FileHash $zip -Algorithm SHA256
} finally { Pop-Location }
