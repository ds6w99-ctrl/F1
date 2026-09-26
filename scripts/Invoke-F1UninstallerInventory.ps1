$ErrorActionPreference='Stop'
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
& "$env:DOTNET_ROOT/dotnet.exe" run --project tests/SystemCore.UninstallerTests -c Release --no-build -- --inventory | Out-File artifacts/f1-uninstaller/inventory.json -Encoding utf8
if($LASTEXITCODE){throw 'Read-only inventory review failed'}
