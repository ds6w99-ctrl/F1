$ErrorActionPreference='Stop'
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
& "$env:DOTNET_ROOT/dotnet.exe" run --project tests/SystemCore.UninstallerTests -c Release --no-build -- --synthetic --leftover-delete | Out-File artifacts/f1-uninstaller/uninstaller-tests-final.txt -Encoding utf8
if($LASTEXITCODE){throw 'Targeted uninstaller regression failed'}
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-UninstallerPage.ps1)))) -AppPath artifacts/f1-uninstaller/publish/F1.exe -EvidencePath artifacts/f1-uninstaller/fixture-ui
