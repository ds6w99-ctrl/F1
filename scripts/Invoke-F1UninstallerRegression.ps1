param([string]$AppPath='artifacts/current/F1/F1.exe',[string]$EvidencePath='artifacts/current/uninstaller-regression')
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $EvidencePath | Out-Null
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
& "$env:DOTNET_ROOT/dotnet.exe" run --project tests/SystemCore.UninstallerTests -c Release --no-build -- --synthetic --leftover-delete | Out-File "$EvidencePath/uninstaller-tests-final.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Targeted uninstaller regression failed'}
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-UninstallerPage.ps1)))) -AppPath $AppPath -EvidencePath "$EvidencePath/fixture-ui"
