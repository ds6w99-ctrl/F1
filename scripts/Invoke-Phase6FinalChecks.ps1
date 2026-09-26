param([string]$EvidenceRoot='artifacts/phase6/accepted')
$ErrorActionPreference='Stop'
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
$sdk="$env:DOTNET_ROOT\dotnet.exe"
New-Item -ItemType Directory -Force $EvidenceRoot | Out-Null
& $sdk tests/SystemCore.StartupTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.StartupTests.dll --mutation | Out-File "$EvidenceRoot/mutation.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Final startup mutation failed'}
& $sdk tests/SystemCore.StartupTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.StartupTests.dll | Out-File "$EvidenceRoot/inventory-tests.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Final startup inventory failed'}
& $sdk tests/SystemCore.StartupTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.StartupTests.dll --inventory | Out-File "$EvidenceRoot/inventory.json" -Encoding utf8
if($LASTEXITCODE){throw 'Final real inventory failed'}
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Invoke-Phase6CoreRegression.ps1)))) -EvidencePath "$EvidenceRoot/core-regression"
& $sdk tests/SystemCore.CompatibilityTests/bin/Release/net10.0-windows/SystemCore.CompatibilityTests.dll . artifacts/phase6/app | Out-File "$EvidenceRoot/compatibility.json" -Encoding utf8
if($LASTEXITCODE){throw 'Final compatibility failed'}
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-StartupPage.ps1)))) -EvidencePath "$EvidenceRoot/production-ui"
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
$saved=[IO.File]::ReadAllBytes($pref)
try {
 [IO.File]::WriteAllText($pref,'{"theme":"Light","language":"en"}')
 & ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1)))) -AppPath artifacts/phase6/app/SystemCore.App.exe -EvidencePath "$EvidenceRoot/regression"
}finally{[IO.File]::WriteAllBytes($pref,$saved)}
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Invoke-Phase6Stability.ps1)))) -EvidencePath "$EvidenceRoot/stability"
