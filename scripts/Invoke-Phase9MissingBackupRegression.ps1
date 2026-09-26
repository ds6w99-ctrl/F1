$ErrorActionPreference='Stop'
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
& "$env:DOTNET_ROOT/dotnet.exe" tests/SystemCore.StartupTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.StartupTests.dll --mutation | Out-File artifacts/phase9/9c/core/startup--mutation.txt -Encoding utf8
if($LASTEXITCODE){throw 'Missing backup regression failed'}
