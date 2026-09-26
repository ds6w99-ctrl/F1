$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Invoke-Phase8CoreValidation.ps1)).Replace('artifacts/phase8/app','artifacts/phase9/app')
& ([scriptblock]::Create($source)) -EvidencePath artifacts/phase9/9c/core
foreach($suite in @('Security','Reliability')){
 & "$env:DOTNET_ROOT/dotnet.exe" "tests/SystemCore.${suite}Tests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.${suite}Tests.dll" | Out-File "artifacts/phase9/9c/core/$suite.txt" -Encoding utf8
 if($LASTEXITCODE){throw "$suite candidate regression failed"}
}
