$ErrorActionPreference='Stop'
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-ProductUx.ps1)))) -AppPath artifacts/phase9/app/SystemCore.App.exe -EvidencePath artifacts/phase9/9c/ui -ExpectedVersion '0.9.0-dev'
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-MemoryToolsPage.ps1)))) -AppPath artifacts/phase9/app/SystemCore.App.exe -EvidencePath artifacts/phase9/9c/memory-ui
& "$PSScriptRoot/Test-ThemeLocalization.ps1" -EvidencePath artifacts/current/theme-localization
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json';$saved=if(Test-Path $pref){[IO.File]::ReadAllBytes($pref)}else{$null}
try{
 [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"theme":"Light","language":"en"}')
 & ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1)))) -AppPath artifacts/phase9/app/SystemCore.App.exe -EvidencePath artifacts/phase9/9c/shell-regression -CleanerExecutor
}finally{if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}}
