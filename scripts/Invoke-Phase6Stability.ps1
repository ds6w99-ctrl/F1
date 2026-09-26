param([string]$EvidencePath='artifacts/phase6/stability')
$ErrorActionPreference='Stop'
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
$evidence=$EvidencePath
New-Item -ItemType Directory -Force $evidence | Out-Null
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-UninstallerStability.ps1))
& ([scriptblock]::Create($source.Substring(0,$source.IndexOf('& "$env:DOTNET_ROOT\dotnet.exe"')))) -AppPath artifacts/phase6/app/SystemCore.App.exe -EvidencePath $evidence -IdleSeconds 120
& "$env:DOTNET_ROOT\dotnet.exe" tests/SystemCore.StartupTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.StartupTests.dll --lifecycle "$evidence/core.json" | Out-File "$evidence/core-tests.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Startup lifecycle failed'}
