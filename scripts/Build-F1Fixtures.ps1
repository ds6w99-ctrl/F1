$ErrorActionPreference='Stop'
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
New-Item -ItemType Directory -Force artifacts/f1-ui/test-results/fixtures|Out-Null
& "$env:DOTNET_ROOT/dotnet.exe" publish src/SystemCore.App/SystemCore.App.csproj -c Release -r win-x64 --no-restore -p:ReliabilityAudit=false -p:CleanerValidation=true -p:StartupValidation=true -p:MemoryValidation=true -o artifacts/f1-ui/fixture-app | Out-File artifacts/f1-ui/test-results/fixtures/publish.txt -Encoding utf8
if($LASTEXITCODE){throw 'Isolated fixture publication failed'}
