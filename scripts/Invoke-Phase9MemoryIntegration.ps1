$ErrorActionPreference='Stop'
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
& "$env:DOTNET_ROOT/dotnet.exe" tests/SystemCore.MemoryToolsTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.MemoryToolsTests.dll --integration | Out-File artifacts/phase9/9a/regression/MemoryTools.txt -Encoding utf8
if($LASTEXITCODE){throw 'Memory integration failed'}
