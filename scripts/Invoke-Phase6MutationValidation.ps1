$ErrorActionPreference='Stop'
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
$sdk="$env:DOTNET_ROOT\dotnet.exe"
$evidence='artifacts/phase6/6b-verified'
New-Item -ItemType Directory -Force $evidence | Out-Null
& $sdk tests/SystemCore.StartupTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.StartupTests.dll --mutation | Out-File "$evidence/mutation-desktop.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Startup mutation tests failed'}
& $sdk tests/SystemCore.StartupTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.StartupTests.dll | Out-File "$evidence/inventory-desktop.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Startup inventory tests failed'}
