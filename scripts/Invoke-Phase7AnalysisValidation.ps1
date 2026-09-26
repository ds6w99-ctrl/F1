$ErrorActionPreference='Stop'
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
$sdk="$env:DOTNET_ROOT\dotnet.exe"
& $sdk tests/SystemCore.MemoryToolsTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.MemoryToolsTests.dll --integration | Out-File artifacts/phase7/7a/tests-desktop.txt -Encoding utf8
if($LASTEXITCODE){throw 'Memory analysis tests failed'}
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Invoke-Phase6CoreRegression.ps1)))) -EvidencePath artifacts/phase7/7a/core-regression
& $sdk tests/SystemCore.StartupTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.StartupTests.dll --mutation | Out-File artifacts/phase7/7a/startup-regression.txt -Encoding utf8
if($LASTEXITCODE){throw 'Startup regression failed'}
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-MemoryToolsPage.ps1))))
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json';$saved=[IO.File]::ReadAllBytes($pref)
try{
 [IO.File]::WriteAllText($pref,'{"theme":"Light","language":"en"}')
 & ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1)))) -AppPath artifacts/phase7/app/SystemCore.App.exe -EvidencePath artifacts/phase7/7a/regression
}finally{[IO.File]::WriteAllBytes($pref,$saved)}
