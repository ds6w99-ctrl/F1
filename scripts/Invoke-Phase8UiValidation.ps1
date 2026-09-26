param([switch]$RegressionOnly)
$ErrorActionPreference='Stop'
if(!$RegressionOnly){& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-ProductUx.ps1))))}
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-MemoryToolsPage.ps1)))) -AppPath artifacts/phase8/app/SystemCore.App.exe -EvidencePath artifacts/phase8/completion/memory-ui
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json';$saved=if(Test-Path $pref){[IO.File]::ReadAllBytes($pref)}else{$null}
try{
 [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"theme":"Light","language":"en"}')
 & ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1)))) -AppPath artifacts/phase8/app/SystemCore.App.exe -EvidencePath artifacts/phase8/completion/shell-regression -CleanerExecutor
}finally{if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}}
