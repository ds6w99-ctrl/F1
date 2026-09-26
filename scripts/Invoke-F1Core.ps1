param([string]$Publication='artifacts/f1-ui/publish',[string]$EvidencePath='artifacts/f1-ui/test-results/core')
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Invoke-Phase8CoreValidation.ps1)).Replace('artifacts/phase8/app',$Publication)
& ([scriptblock]::Create($source)) -EvidencePath $EvidencePath
foreach($suite in @('Security','Reliability')){
 & "$env:DOTNET_ROOT/dotnet.exe" "tests/SystemCore.${suite}Tests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.${suite}Tests.dll" | Out-File "$EvidencePath/$suite.txt" -Encoding utf8
 if($LASTEXITCODE){throw "$suite F1 regression failed"}
}
