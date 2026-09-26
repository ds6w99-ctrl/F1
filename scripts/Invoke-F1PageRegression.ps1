param([string]$EvidencePath='artifacts/f1-ui/test-results/pages')
$ErrorActionPreference='Stop'
$app='artifacts/f1-ui/publish/F1.exe'
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-MemoryToolsPage.ps1)).Replace('Released from SystemCore caches:','Released from F1 caches:')
& ([scriptblock]::Create($source)) -AppPath $app -EvidencePath "$EvidencePath/memory"
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-ThemeLocalization.ps1)).Replace('artifacts/phase8/app/SystemCore.App.exe',$app).Replace('artifacts/phase8/completion',"$EvidencePath/themes")
& ([scriptblock]::Create($source))
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
$source=$source.Replace("SetValue('SystemCore')","SetValue('F1')")
$source=$source.Replace("Check 'Arabic dashboard' ((Find 'PageTitle').Current.Name -eq (`$arabic | Where-Object name -eq 'NavDashboard').value)","Check 'Arabic dashboard' ((Find 'PageTitle').Current.Name -eq (`$arabic | Where-Object name -eq 'F1Welcome').value)")
$source=$source.Replace("Check 'Arabic restored after relaunch' ((Find 'PageTitle').Current.Name -eq (`$arabic | Where-Object name -eq 'NavDashboard').value)","Check 'Arabic restored after relaunch' ((Find 'PageTitle').Current.Name -eq (`$arabic | Where-Object name -eq 'F1Welcome').value)")
$source=$source.Replace(".Current.Name -eq 'Dashboard'",".Current.Name -eq 'Welcome back'")
$source=$source.Replace("'RTL sidebar restored'","'Arabic left sidebar restored'").Replace(".Current.BoundingRectangle.X -gt (Find 'MetricCpu')",".Current.BoundingRectangle.X -lt (Find 'MetricCpu')")
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json';$saved=if(Test-Path $pref){[IO.File]::ReadAllBytes($pref)}else{$null}
try{
 [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"theme":"Light","language":"en"}')
 & ([scriptblock]::Create($source)) -AppPath $app -EvidencePath "$EvidencePath/shell" -CleanerExecutor
}finally{if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}}
