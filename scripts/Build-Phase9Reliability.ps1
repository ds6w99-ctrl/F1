$ErrorActionPreference='Stop'
$evidence='artifacts/phase9/9a/build'
New-Item -ItemType Directory -Force $evidence | Out-Null
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
$sdk="$env:DOTNET_ROOT/dotnet.exe"
& $sdk restore SystemCore.sln --locked-mode | Out-File "$evidence/restore.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Locked restore failed'}
foreach($configuration in @('Debug','Release')){
 & $sdk build SystemCore.sln -c $configuration --no-restore | Out-File "$evidence/$configuration.txt" -Encoding utf8
 if($LASTEXITCODE){throw "$configuration reliability build failed"}
}
& $sdk publish src/SystemCore.App/SystemCore.App.csproj -c Release -r win-x64 --no-restore -p:ReliabilityAudit=true -o artifacts/phase9/9a/verified-app | Out-File "$evidence/audit-publish.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Audit publish failed'}
