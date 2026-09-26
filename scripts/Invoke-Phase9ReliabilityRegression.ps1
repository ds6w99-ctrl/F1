$ErrorActionPreference='Stop'
$evidence='artifacts/phase9/9a/regression'
New-Item -ItemType Directory -Force $evidence | Out-Null
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
$sdk="$env:DOTNET_ROOT/dotnet.exe"
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Invoke-Phase6CoreRegression.ps1)))) -EvidencePath "$evidence/core"
foreach($suite in @('Reliability','Product','Startup')){
 & $sdk "tests/SystemCore.${suite}Tests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.${suite}Tests.dll" | Out-File "$evidence/$suite.txt" -Encoding utf8
 if($LASTEXITCODE){throw "$suite regression failed"}
}
& $sdk tests/SystemCore.MemoryToolsTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.MemoryToolsTests.dll --integration | Out-File "$evidence/MemoryTools.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Memory integration failed'}
& $sdk tests/SystemCore.StartupTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.StartupTests.dll --mutation | Out-File "$evidence/startup-mutation.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Startup mutation fixtures failed'}
[pscustomobject]@{Result='PASS';Time=[DateTimeOffset]::UtcNow.ToString('o');Scope='9A affected-engine safety/regression under desktop token; not the later 9C release-candidate gate'}|ConvertTo-Json|Set-Content "$evidence/result.json" -Encoding utf8
