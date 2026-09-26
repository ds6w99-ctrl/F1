$ErrorActionPreference='Stop'
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
$sdk="$env:DOTNET_ROOT\dotnet.exe"
$evidence='artifacts/phase7/7a-verified'
& $sdk tests/SystemCore.MemoryToolsTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.MemoryToolsTests.dll --integration | Out-File "$evidence/tests.txt" -Encoding utf8
if($LASTEXITCODE){throw '7A tests failed'}
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Invoke-Phase6CoreRegression.ps1)))) -EvidencePath "$evidence/core-regression"
foreach($mode in @('', '--mutation')){
 & $sdk tests/SystemCore.StartupTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.StartupTests.dll $mode | Out-File "$evidence/startup$mode.txt" -Encoding utf8
 if($LASTEXITCODE){throw 'Startup regression failed'}
}
& $sdk tests/SystemCore.CompatibilityTests/bin/Release/net10.0-windows/SystemCore.CompatibilityTests.dll . artifacts/phase7/7a-app | Out-File "$evidence/compatibility.json" -Encoding utf8
if($LASTEXITCODE){throw 'Compatibility failed'}
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-MemoryToolsPage.ps1)))) -AppPath artifacts/phase7/7a-app/SystemCore.App.exe -EvidencePath "$evidence/ui" -AnalysisOnly
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json';$saved=[IO.File]::ReadAllBytes($pref)
try{
 [IO.File]::WriteAllText($pref,'{"theme":"Light","language":"en"}')
 & ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1)))) -AppPath artifacts/phase7/7a-app/SystemCore.App.exe -EvidencePath "$evidence/regression"
}finally{[IO.File]::WriteAllBytes($pref,$saved)}
[pscustomobject]@{Gate='Phase 7A';Result='PASS';CompletedAt=[DateTimeOffset]::UtcNow;Scope='Development; desktop limited token; real read-only metrics, isolated regression fixtures';CoreHash=(Get-FileHash artifacts/phase7/7a-app/SystemCore.Core.dll).Hash;InfrastructureHash=(Get-FileHash artifacts/phase7/7a-app/SystemCore.Infrastructure.dll).Hash}|ConvertTo-Json|Set-Content "$evidence/gate.json" -Encoding utf8
