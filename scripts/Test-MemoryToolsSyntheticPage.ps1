param([string]$AppPath='artifacts/phase7/validation-app/SystemCore.App.exe',[string]$EvidencePath='artifacts/phase7/completion/synthetic-ui')
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
. ([scriptblock]::Create($source.Substring(0,$source.IndexOf('$p=Start-Process')))) -AppPath $AppPath -EvidencePath $EvidencePath
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json';$saved=[IO.File]::ReadAllBytes($pref)
[IO.File]::WriteAllText($pref,'{"theme":"Light","language":"en"}')
$p=$null
function InvokeMemory([string]$id){(Find $id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();Start-Sleep -Milliseconds 200}
function OptimizeMemory {
 InvokeMemory 'MemOptimize';$watch=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 100}while(!(Find 'MemOptimize').Current.IsEnabled -and $watch.Elapsed.TotalSeconds -lt 10)
 Check 'Synthetic manual operation finished' ((Find 'MemOptimize').Current.IsEnabled) $null
}
try{
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 $watch=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $watch.Elapsed.TotalSeconds -lt 30)
 Check 'Synthetic Memory app launch' (!$p.HasExited -and $p.MainWindowHandle -ne [IntPtr]::Zero) $null
 $elevated=& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Get-ProcessElevation.ps1)))) -ProcessId $p.Id
 Check 'Synthetic Memory app unelevated' (!$elevated) $null
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 Navigate 'NavMemoryTools';Start-Sleep -Seconds 5
 Check 'Compile-time fixture required' ($null -ne (Find 'MemTestAudit')) $null
 InvokeMemory 'MemTestNormal';OptimizeMemory
 Check 'Normal synthetic state no-op' ((Find 'MemOptimizationSummary').Current.Name.Contains('No optimization is currently required.')) $null
 InvokeMemory 'MemTestHigh';OptimizeMemory
 Check 'High pressure shows guidance' ((Find 'MemPressure').Current.Name.Contains('Closing applications you are not using may help.')) $null
 Check 'High without owned cache no automatic action' ((Find 'MemOptimizationSummary').Current.Name.Contains('No safe automatic action is available.')) $null
 InvokeMemory 'MemTestActive';OptimizeMemory
 Check 'Active buffer preserved by UI action' ((Find 'MemTestAudit').Current.Name.Contains('Cache:16777216; Active:True; Released:0')) (Find 'MemTestAudit').Current.Name
 InvokeMemory 'MemTestStale';OptimizeMemory
 Check 'Stale fixture actually released' ((Find 'MemTestAudit').Current.Name.Contains('Cache:0; Active:False; Released:16777216')) (Find 'MemTestAudit').Current.Name
 Check 'Exact known bytes displayed' ((Find 'MemOptimizationSummary').Current.Name -match 'Released from SystemCore caches: 16(\.0)? MB') (Find 'MemOptimizationSummary').Current.Name
 Check 'Synthetic before after displayed' ((Find 'MemBeforeAfter').Current.Name -match 'Known owned cache: 16(\.0)? MB' -and (Find 'MemBeforeAfter').Current.Name.Contains('Known owned cache: 0 B')) $null
 Save-WindowPicture 'synthetic-release'
 OptimizeMemory
 Check 'Repeated optimize cannot release twice' ((Find 'MemOptimizationSummary').Current.Name.Contains('Released from SystemCore caches: 0 B') -and (Find 'MemTestAudit').Current.Name.Contains('Released:16777216')) $null
 Navigate 'NavSettings'
 $ar=([xml][IO.File]::ReadAllText((Resolve-Path src/SystemCore.App/Resources/Shell.ar.resx))).root.data
 Choose 'LanguagePicker' (($ar|Where-Object name -eq 'Arabic').value)
 Navigate 'NavMemoryTools';InvokeMemory 'MemTestNormal';OptimizeMemory
 Check 'Arabic no-op explanation' ((Find 'MemOptimizationSummary').Current.Name.Contains(($ar|Where-Object name -eq 'MemPlanNormal').value)) $null
 Check 'Arabic Optimize accessible name' ((Find 'MemOptimize').Current.Name -eq ($ar|Where-Object name -eq 'MemOptimize').value) $null
 Save-WindowPicture 'synthetic-ar-noop'
 $null=$p.CloseMainWindow();Check 'Synthetic normal shutdown' ($p.WaitForExit(10000)) $null
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(10000)};$p.Dispose()}
 [IO.File]::WriteAllBytes($pref,$saved)
 $results|ConvertTo-Json -Depth 5|Set-Content "$EvidencePath/checks.json" -Encoding utf8
}
