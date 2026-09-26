param([string]$EvidencePath='artifacts/phase8/completion/build')
$ErrorActionPreference='Stop'
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
$sdk="$env:DOTNET_ROOT\dotnet.exe"
New-Item -ItemType Directory -Force $EvidencePath | Out-Null
& $sdk restore SystemCore.sln --locked-mode | Out-File "$EvidencePath/locked-restore.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Locked restore failed'}
foreach($configuration in @('Debug','Release')){
 & $sdk build SystemCore.sln -c $configuration --no-restore | Out-File "$EvidencePath/$configuration.txt" -Encoding utf8
 if($LASTEXITCODE){throw "$configuration build failed"}
}
& $sdk publish src/SystemCore.App/SystemCore.App.csproj -c Release -r win-x64 --no-restore -o artifacts/phase8/app | Out-File "$EvidencePath/publish-app.txt" -Encoding utf8
if($LASTEXITCODE){throw 'App publish failed'}
& $sdk publish src/SystemCore.ElevatedHost/SystemCore.ElevatedHost.csproj -c Release -r win-x64 --no-restore -o artifacts/phase8/app/ElevatedHost | Out-File "$EvidencePath/publish-helper.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Helper publish failed'}
