$ErrorActionPreference='Stop'
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
$sdk="$env:DOTNET_ROOT\dotnet.exe"
$elevated=& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Get-ProcessElevation.ps1)))) -ProcessId $PID
if($elevated){throw 'Completion runner must use a non-elevated desktop token'}
& $sdk tests/SystemCore.CompatibilityTests/bin/Release/net10.0-windows/SystemCore.CompatibilityTests.dll . artifacts/phase5/app | Out-File artifacts/phase5/completion/compatibility-desktop-token.txt -Encoding utf8
if($LASTEXITCODE){throw 'Non-elevated compatibility failed'}
& $sdk tests/SystemCore.UninstallerTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.UninstallerTests.dll --synthetic --leftover-delete | Out-File artifacts/phase5/completion/uninstaller-desktop-token.txt -Encoding utf8
if($LASTEXITCODE){throw 'Non-elevated uninstaller safety tests failed'}
& $sdk tests/SystemCore.SystemInformationTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.SystemInformationTests.dll | Out-File artifacts/phase5/completion/system-information.txt -Encoding utf8
if($LASTEXITCODE){throw 'System information failed'}
& $sdk tests/SystemCore.PerformanceTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.PerformanceTests.dll | Out-File artifacts/phase5/completion/performance.txt -Encoding utf8
if($LASTEXITCODE){throw 'Performance failed'}
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-UninstallerPage.ps1)))) -EvidencePath artifacts/phase5/completion-ui
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
$saved=[IO.File]::ReadAllBytes($pref)
try {& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1)))) -AppPath artifacts/phase5/app/SystemCore.App.exe -EvidencePath artifacts/phase5/completion-regression}finally{[IO.File]::WriteAllBytes($pref,$saved)}
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-UninstallerStability.ps1)))) -EvidencePath artifacts/phase5/completion-stability -IdleSeconds 120
