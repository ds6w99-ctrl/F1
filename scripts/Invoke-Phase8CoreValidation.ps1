param([string]$EvidencePath='artifacts/phase8/completion/core')
$ErrorActionPreference='Stop'
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
$sdk="$env:DOTNET_ROOT\dotnet.exe"
New-Item -ItemType Directory -Force $EvidencePath | Out-Null
$elevated=& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Get-ProcessElevation.ps1)))) -ProcessId $PID
if($elevated){throw 'Validation requires desktop token'}
& $sdk tests/SystemCore.ProductTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.ProductTests.dll --integration | Out-File "$EvidencePath/product.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Product suite failed'}
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Invoke-Phase6CoreRegression.ps1)))) -EvidencePath "$EvidencePath/regression"
foreach($mode in @('', '--mutation')){
 & $sdk tests/SystemCore.StartupTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.StartupTests.dll $mode | Out-File "$EvidencePath/startup$mode.txt" -Encoding utf8
 if($LASTEXITCODE){throw 'Startup regression failed'}
}
& $sdk tests/SystemCore.MemoryToolsTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.MemoryToolsTests.dll --integration | Out-File "$EvidencePath/memory.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Memory regression failed'}
& $sdk tests/SystemCore.CompatibilityTests/bin/Release/net10.0-windows/SystemCore.CompatibilityTests.dll . artifacts/phase8/app | Out-File "$EvidencePath/compatibility.json" -Encoding utf8
if($LASTEXITCODE){throw 'Compatibility failed'}
$hashes=[ordered]@{}
foreach($assembly in @('Core','Infrastructure')){$hashes[$assembly]=(Get-FileHash "tests/SystemCore.ProductTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.$assembly.dll").Hash}
$hashes|ConvertTo-Json|Set-Content "$EvidencePath/engine-hashes.json" -Encoding utf8
[pscustomobject]@{Result='PASS';Elevated=$false;CompletedAt=[DateTimeOffset]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content "$EvidencePath/result.json" -Encoding utf8
