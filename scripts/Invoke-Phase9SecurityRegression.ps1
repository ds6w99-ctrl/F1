$ErrorActionPreference='Stop'
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
$sdk="$env:DOTNET_ROOT/dotnet.exe"
$evidence='artifacts/phase9/9b/regression'
New-Item -ItemType Directory -Force $evidence|Out-Null
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Invoke-Phase6CoreRegression.ps1)))) -EvidencePath "$evidence/core"
foreach($suite in @('Security','Reliability','Product','Startup')){
 & $sdk "tests/SystemCore.${suite}Tests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.${suite}Tests.dll" | Out-File "$evidence/$suite.txt" -Encoding utf8
 if($LASTEXITCODE){throw "$suite security regression failed"}
}
& $sdk tests/SystemCore.StartupTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.StartupTests.dll --mutation | Out-File "$evidence/startup-mutation.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Startup mutation regression failed'}
& $sdk tests/SystemCore.MemoryToolsTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.MemoryToolsTests.dll --integration | Out-File "$evidence/memory.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Memory regression failed'}
@{Result='PASS';Scope='9B security and affected-feature regression';Time=[DateTimeOffset]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content "$evidence/result.json" -Encoding utf8
