param([string]$EvidencePath='artifacts/phase6/6a-core-regression')
$ErrorActionPreference='Stop'
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
$sdk="$env:DOTNET_ROOT\dotnet.exe"
$evidence=$EvidencePath
New-Item -ItemType Directory -Force $evidence | Out-Null
$elevated=& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Get-ProcessElevation.ps1)))) -ProcessId $PID
if($elevated){throw 'Regression requires desktop token'}
& $sdk tests/SystemCore.UnitTests/bin/Release/net10.0/SystemCore.UnitTests.dll | Out-File "$evidence/unit.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Unit regression failed'}
& $sdk tests/SystemCore.CleanerTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.CleanerTests.dll | Out-File "$evidence/cleaner.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Cleaner scanner failed'}
& $sdk tests/SystemCore.CleanerTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.CleanerTests.dll --executor | Out-File "$evidence/cleaner-executor.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Cleaner executor failed'}
& $sdk tests/SystemCore.CleanerTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.CleanerTests.dll --completion | Out-File "$evidence/cleaner-completion.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Cleaner completion failed'}
& $sdk tests/SystemCore.UninstallerTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.UninstallerTests.dll --synthetic --leftover-delete | Out-File "$evidence/uninstaller.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Uninstaller safety failed'}
& $sdk tests/SystemCore.SystemInformationTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.SystemInformationTests.dll | Out-File "$evidence/system-information.txt" -Encoding utf8
if($LASTEXITCODE){throw 'System information failed'}
& $sdk tests/SystemCore.PerformanceTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.PerformanceTests.dll | Out-File "$evidence/performance.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Performance failed'}
