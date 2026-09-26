param([string]$EvidencePath='artifacts/f1-ui/test-results/build',[string]$PublishPath='artifacts/f1-ui/publish')
$ErrorActionPreference='Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
$sdk="$env:DOTNET_ROOT/dotnet.exe"
New-Item -ItemType Directory -Force $EvidencePath|Out-Null
& $sdk restore SystemCore.sln --locked-mode | Out-File "$EvidencePath/restore.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Locked restore failed'}
foreach($configuration in @('Debug','Release')){
 & $sdk build SystemCore.sln -c $configuration --no-restore -p:ReliabilityAudit=false | Out-File "$EvidencePath/$configuration.txt" -Encoding utf8
 if($LASTEXITCODE){throw "$configuration build failed"}
}
& $sdk publish src/SystemCore.App/SystemCore.App.csproj -c Release -r win-x64 --no-restore -p:ReliabilityAudit=false -o $PublishPath | Out-File "$EvidencePath/publish-app.txt" -Encoding utf8
if($LASTEXITCODE){throw 'F1 publish failed'}
& $sdk publish src/SystemCore.ElevatedHost/SystemCore.ElevatedHost.csproj -c Release -r win-x64 --no-restore -o "$PublishPath/ElevatedHost" | Out-File "$EvidencePath/publish-helper.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Helper publish failed'}
foreach($file in @('F1.exe','F1.dll','F1.pri','F1.runtimeconfig.json','ar/F1.resources.dll','Assets/F1/mark.svg','Assets/F1/mountains-placeholder.svg','ElevatedHost/SystemCore.ElevatedHost.exe')){if(!(Test-Path "$PublishPath/$file")){throw "Missing payload $file"}}
