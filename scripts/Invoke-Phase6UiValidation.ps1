$ErrorActionPreference='Stop'
$elevated=& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Get-ProcessElevation.ps1)))) -ProcessId $PID
if($elevated){throw 'UI validation must be unelevated'}
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-StartupSyntheticPage.ps1))))
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-StartupPage.ps1)))) -EvidencePath artifacts/phase6/final-ui
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
$saved=[IO.File]::ReadAllBytes($pref)
try {
 [IO.File]::WriteAllText($pref,'{"theme":"Light","language":"en"}')
 & ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1)))) -AppPath artifacts/phase6/app/SystemCore.App.exe -EvidencePath artifacts/phase6/final-regression
}finally{[IO.File]::WriteAllBytes($pref,$saved)}
