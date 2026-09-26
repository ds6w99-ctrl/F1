$ErrorActionPreference='Stop'
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
$sdk="$env:DOTNET_ROOT\dotnet.exe"
$evidence='artifacts/phase6/6a-final'
New-Item -ItemType Directory -Force $evidence | Out-Null
& $sdk tests/SystemCore.StartupTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.StartupTests.dll | Out-File "$evidence/tests.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Startup read-only tests failed'}
& $sdk tests/SystemCore.StartupTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.StartupTests.dll --inventory | Out-File "$evidence/inventory.json" -Encoding utf8
if($LASTEXITCODE){throw 'Startup real read-only inventory failed'}
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-StartupPage.ps1)))) -EvidencePath "$evidence/ui"
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
$saved=[IO.File]::ReadAllBytes($pref)
try {
 [IO.File]::WriteAllText($pref,'{"theme":"Light","language":"en"}')
 & ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1)))) -AppPath artifacts/phase6/app/SystemCore.App.exe -EvidencePath "$evidence/regression"
}finally{[IO.File]::WriteAllBytes($pref,$saved)}
