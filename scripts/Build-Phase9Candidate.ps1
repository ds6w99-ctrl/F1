$ErrorActionPreference='Stop'
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
$sdk="$env:DOTNET_ROOT/dotnet.exe";$evidence='artifacts/phase9/9c/build'
New-Item -ItemType Directory -Force $evidence|Out-Null
& $sdk restore SystemCore.sln --locked-mode | Out-File "$evidence/restore.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Locked restore failed'}
foreach($configuration in @('Debug','Release')){
 & $sdk build SystemCore.sln -c $configuration --no-restore | Out-File "$evidence/$configuration.txt" -Encoding utf8
 if($LASTEXITCODE){throw "$configuration build failed"}
}
& $sdk publish src/SystemCore.App/SystemCore.App.csproj -c Release -r win-x64 --no-restore -p:ReliabilityAudit=false -o artifacts/phase9/app | Out-File "$evidence/publish-app.txt" -Encoding utf8
if($LASTEXITCODE){throw 'App publish failed'}
& $sdk publish src/SystemCore.ElevatedHost/SystemCore.ElevatedHost.csproj -c Release -r win-x64 --no-restore -o artifacts/phase9/app/ElevatedHost | Out-File "$evidence/publish-helper.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Helper publish failed'}
& $sdk publish src/SystemCore.App/SystemCore.App.csproj -c Release -r win-x64 --no-restore -p:ReliabilityAudit=true -o artifacts/phase9/9c/audit-app | Out-File "$evidence/publish-audit.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Audit publication failed'}
