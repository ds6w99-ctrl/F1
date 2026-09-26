param([string]$EvidencePath='artifacts/phase7/completion')
$ErrorActionPreference='Stop'
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
$sdk="$env:DOTNET_ROOT\dotnet.exe"
$evidence=$EvidencePath
New-Item -ItemType Directory -Force $evidence|Out-Null
$elevated=& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Get-ProcessElevation.ps1)))) -ProcessId $PID
if($elevated){throw 'Completion requires desktop token'}
& $sdk tests/SystemCore.MemoryToolsTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.MemoryToolsTests.dll --integration | Out-File "$evidence/memory-tests.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Memory tools suite failed'}
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Invoke-Phase6CoreRegression.ps1)))) -EvidencePath "$evidence/core-regression"
foreach($mode in @('', '--mutation')){
 & $sdk tests/SystemCore.StartupTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.StartupTests.dll $mode | Out-File "$evidence/startup$mode.txt" -Encoding utf8
 if($LASTEXITCODE){throw 'Startup regression failed'}
}
& $sdk tests/SystemCore.CompatibilityTests/bin/Release/net10.0-windows/SystemCore.CompatibilityTests.dll . artifacts/phase7/app | Out-File "$evidence/compatibility.json" -Encoding utf8
if($LASTEXITCODE){throw 'Compatibility failed'}
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-MemoryToolsSyntheticPage.ps1)))) -EvidencePath "$evidence/synthetic-ui"
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-MemoryToolsPage.ps1)))) -EvidencePath "$evidence/ui"
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json';$saved=[IO.File]::ReadAllBytes($pref)
try{
 [IO.File]::WriteAllText($pref,'{"theme":"Light","language":"en"}')
 & ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1)))) -AppPath artifacts/phase7/app/SystemCore.App.exe -EvidencePath "$evidence/regression"
}finally{[IO.File]::WriteAllBytes($pref,$saved)}
& $sdk tests/SystemCore.MemoryToolsTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.MemoryToolsTests.dll --lifecycle "$evidence/core-lifecycle.json" | Out-File "$evidence/core-lifecycle.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Core lifecycle failed'}
[pscustomobject]@{FunctionalChecks='PASS';Elevated=$false;CompletedAt=[DateTimeOffset]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content "$evidence/functional-result.json" -Encoding utf8
