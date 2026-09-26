param([string]$AppPath='artifacts/phase7/app/SystemCore.App.exe',[string]$EvidencePath='artifacts/phase7/7a-ui',[switch]$AnalysisOnly)
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
. ([scriptblock]::Create($source.Substring(0,$source.IndexOf('$p=Start-Process')))) -AppPath $AppPath -EvidencePath $EvidencePath
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
$saved=if(Test-Path $pref){[IO.File]::ReadAllBytes($pref)}else{$null}
[IO.File]::WriteAllText($pref,'{"theme":"Light","language":"en"}')
$p=$null
function ScrollMemory([double]$percent){
 $node=Find 'MemMode'
 while($node -and !$node.GetCurrentPropertyValue([Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty)){$node=[Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($node)}
 if($node){$node.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1,$percent);Start-Sleep -Milliseconds 200}
}
try{
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 $watch=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $watch.Elapsed.TotalSeconds -lt 30)
 Check 'Memory app launched' (!$p.HasExited -and $p.MainWindowHandle -ne [IntPtr]::Zero) $null
 $elevated=& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Get-ProcessElevation.ps1)))) -ProcessId $p.Id
 Check 'Memory tools run unelevated' (!$elevated) $elevated
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 Navigate 'NavMemoryTools'
 $watch.Restart()
 do{Start-Sleep -Milliseconds 200;$physical=Find 'MemPhysical'}while((!$physical -or $physical.Current.Name -notmatch 'RAM' -or $physical.Current.Name -match 'Not available') -and $watch.Elapsed.TotalSeconds -lt 15)
 Check 'Real physical memory displayed' ($physical.Current.Name -match 'RAM' -and $physical.Current.Name -notmatch 'Not available') $physical.Current.Name
 Check 'Real commit displayed' ((Find 'MemCommit').Current.Name -notmatch 'Not available') (Find 'MemCommit').Current.Name
 Check 'Real system cache displayed' ((Find 'MemCache').Current.Name -notmatch 'Not available') (Find 'MemCache').Current.Name
 Check 'Page file not fabricated' ((Find 'MemPageFile').Current.Name -match 'Not available') $null
 Check 'UI uses shared KB MB GB labels' ($physical.Current.Name -notmatch 'KiB|MiB|GiB|TiB') $null
 Start-Sleep -Seconds 6
 $first=[int]([regex]::Match((Find 'MemHistory').Current.Name,'\d+').Value)
 Start-Sleep -Seconds 3
 $second=[int]([regex]::Match((Find 'MemHistory').Current.Name,'\d+').Value)
 Check 'Memory history updates and stays bounded' ($second -gt $first -and $second -le 60) @{First=$first;Second=$second}
 Check 'Charts have accessible descriptions' ((Find 'MemUsageChart').Current.Name.Contains('60 seconds') -and (Find 'MemAvailableChart').Current.Name.Contains('60 seconds')) $null
 (Find 'MemAnalyze').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();Start-Sleep -Milliseconds 250
 Check 'Manual analysis uses shared snapshot' ((Find 'MemStatus').Current.Name -eq 'Analysis updated from the shared measurements.') $null
 Check 'Pressure assessment available' ((Find 'MemPressure').Current.Name -match 'Normal|Elevated|High') (Find 'MemPressure').Current.Name
 if(!$AnalysisOnly){
  Check 'Production excludes Memory fixture controls' ($null -eq (Find 'MemTestAudit')) $null
  (Find 'MemAnalyze').SetFocus();[Windows.Forms.SendKeys]::SendWait('{TAB}');Start-Sleep -Milliseconds 200
  Check 'Keyboard tab from Analyze reaches Optimize' ([Windows.Automation.AutomationElement]::FocusedElement.Current.AutomationId -eq 'MemOptimize') $null
  [Windows.Forms.SendKeys]::SendWait('{TAB}');Start-Sleep -Milliseconds 200
  Check 'Keyboard tab from Optimize reaches consumer mode' ([Windows.Automation.AutomationElement]::FocusedElement.Current.AutomationId -eq 'MemMode') ([Windows.Automation.AutomationElement]::FocusedElement.Current.AutomationId)
  (Find 'MemOptimize').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
  $watch.Restart()
  do{Start-Sleep -Milliseconds 100}while(!(Find 'MemOptimize').Current.IsEnabled -and $watch.Elapsed.TotalSeconds -lt 10)
  Check 'Production Optimize completes without action' ((Find 'MemOptimizationSummary').Current.Name.Contains('No memory action was performed.')) (Find 'MemOptimizationSummary').Current.Name
  Check 'Normal state reports no optimization required' ((Find 'MemOptimizationSummary').Current.Name.Contains('No optimization is currently required.')) $null
  Check 'No fabricated reclaimed bytes' ((Find 'MemOptimizationSummary').Current.Name.Contains('Released from SystemCore caches: 0 B')) $null
  Check 'Before after measurements and attribution caveat' ((Find 'MemBeforeAfter').Current.Name.Contains('Before') -and (Find 'MemBeforeAfter').Current.Name.Contains('After') -and (Find 'MemBeforeAfter').Current.Name.Contains('no speed improvement')) $null
  ScrollMemory 0
 }
 Save-WindowPicture 'memory-en-overview'
 ScrollMemory 100
 foreach($mode in @('Top memory','Top CPU')){
  $en=([xml][IO.File]::ReadAllText((Resolve-Path src/SystemCore.App/Resources/Shell.resx))).root.data
  $key=if($mode -eq 'Top memory'){'PerfTopMemory'}else{'PerfTopCpu'}
  Choose 'MemMode' (($en|Where-Object name -eq $key).value)
  Check 'Top consumers real and bounded' ((Find 'MemConsumers').Current.Name -match '^[1-9][0-9]? accessible') $mode
 }
 $rows=(Find 'MemProcesses').FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem))
 $row=$rows|Where-Object {$_.Current.Name.Length -gt 0}|Select-Object -First 1
 $row.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();Start-Sleep -Milliseconds 400
 Check 'Process details are read-only' ((Find 'MemDetails').Current.Name.Contains('Read-only')) $null
 Check 'Per-process disk unavailability explicit' ((Find 'MemDisk').Current.Name.Contains('not available in this version')) $null
 Check 'Per-process network unavailability explicit' ((Find 'MemNetwork').Current.Name.Contains('not available in this version')) $null
 Navigate 'NavDashboard';Navigate 'NavMemoryTools';Start-Sleep -Seconds 4
 Check 'Return resumes shared memory telemetry' ((Find 'MemPhysical').Current.Name -match 'RAM') $null
 $null=[ShellSmokeNative]::ShowWindow($p.MainWindowHandle,6);Start-Sleep -Seconds 2
 $null=[ShellSmokeNative]::ShowWindow($p.MainWindowHandle,9);Start-Sleep -Seconds 3
 Check 'Minimize restore responsive' ((Find 'MemAnalyze').Current.IsEnabled) $null
 foreach($theme in @('Dark','System','Light')){
  Navigate 'NavSettings';Choose 'ThemePicker' $theme
  Navigate 'NavMemoryTools';ScrollMemory 0
  Check "Memory page visible in $theme" ((Find 'MemAnalyze').Current.IsEnabled) $null
  Save-WindowPicture "memory-$theme"
 }
 Navigate 'NavSettings'
 $ar=([xml][IO.File]::ReadAllText((Resolve-Path src/SystemCore.App/Resources/Shell.ar.resx))).root.data
 Choose 'LanguagePicker' (($ar|Where-Object name -eq 'Arabic').value)
 Navigate 'NavMemoryTools';ScrollMemory 0;Start-Sleep -Seconds 4
 Check 'Arabic Memory title' ((Find 'PageTitle').Current.Name -eq ($ar|Where-Object name -eq 'NavMemoryTools').value) $null
 Check 'Arabic Analyze name' ((Find 'MemAnalyze').Current.Name -eq ($ar|Where-Object name -eq 'MemAnalyze').value) $null
 Save-WindowPicture 'memory-ar-overview'
 $null=$p.CloseMainWindow();Check 'Memory app closes normally' ($p.WaitForExit(10000)) $null
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(10000)};$p.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $results|ConvertTo-Json -Depth 5|Set-Content "$EvidencePath/checks.json" -Encoding utf8
}
