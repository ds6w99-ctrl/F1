param([string]$AppPath='artifacts/phase8/app/SystemCore.App.exe',[string]$EvidencePath='artifacts/phase8/completion/stability',[int]$Cycles=24,[string]$CoordinatesPath='artifacts/phase8/completion/ui/native-coordinates.json')
$ErrorActionPreference='Stop'
# Reuse only native input/counter helpers. This process never loads UIAutomation.
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-MemoryToolsStability.ps1))
$prefix=$source.Substring(0,$source.LastIndexOf('try{',$source.IndexOf('$p=Start-Process')))
. ([scriptblock]::Create($prefix)) -AppPath $AppPath -EvidencePath $EvidencePath -Cycles $Cycles
if($null -ne $saved){[IO.File]::WriteAllBytes("$EvidencePath/original-preferences.bin",$saved)}
$coordinates=Get-Content $CoordinatesPath -Raw|ConvertFrom-Json
$language='en'
$runStarted=[DateTimeOffset]::UtcNow
$verifiedChecks=[Collections.Generic.List[string]]::new()
$completed=$false;$exitCode=$null;$restartExitCode=$null;$mainNormalClose=$false;$restartNormalClose=$false;$navCounts=$null;$persisted=$null;$runFailure=$null
function Hit([string]$id){$point=$coordinates.$language.$id;if(!$point){throw "Missing native coordinate $language $id"};Click $point.X $point.Y}
function Launch {
 Set-Variable -Scope 1 -Name p -Value (Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr-$([Guid]::NewGuid().ToString('N')).txt")
 $launch=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $launch.Elapsed.TotalSeconds -lt 30)
  if($p.HasExited -or $p.MainWindowHandle -eq [IntPtr]::Zero){throw 'Native launch failed'}
  $null=$p.Handle # Retain the native handle so ExitCode remains available after exit.
  $verifiedChecks.Add('Native launch visible')
 $elevated=& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Get-ProcessElevation.ps1)))) -ProcessId $p.Id
  if($elevated){throw 'Product stability requires non-elevated app'}
  $verifiedChecks.Add('Native launch not elevated')
 $null=[MemorySoakNative]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,40,40,1120,760,0x14)
 Start-Sleep -Seconds 3;Foreground
}
try{
  if([AppDomain]::CurrentDomain.GetAssemblies().GetName().Name -match 'UIAutomation'){throw 'Fresh controller required: UIAutomation loaded'}
  $verifiedChecks.Add('Controller has no UIAutomation')
 Launch
 $env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
  $counter=Start-Process 'C:\Users\Falcone\AppData\Local\Temp\opencode\memory-tools\dotnet-counters.exe' -ArgumentList 'collect','--process-id',"$($p.Id)",'--counters','EventCounters\System.Runtime','--refresh-interval','5','--format','csv','--output',('"'+"$EvidencePath/counters.csv"+'"') -WindowStyle Hidden -PassThru -RedirectStandardOutput "$EvidencePath/counter-log.txt" -RedirectStandardError "$EvidencePath/counter-error.txt"
  $null=$counter.Handle # Start-Process otherwise loses exit-code access on this host.
 for($i=0;$i -lt 6;$i++){Start-Sleep -Seconds 5;Record 'initial-dashboard'}
 Picture 'initial-dashboard'
 for($cycle=1;$cycle -le $Cycles;$cycle++){
  foreach($page in @('Dashboard','SystemInfo','Performance','MemoryTools','Cleaner','Uninstaller','Startup','Tools','About')){
   Hit "Nav$page";Start-Sleep -Milliseconds 600
   if($cycle -eq 1){
    switch($page){
     'SystemInfo'{Start-Sleep -Seconds 5}
     'Cleaner'{Hit 'CleanerScan';Start-Sleep -Seconds 30}
     'Uninstaller'{Hit 'UnRefresh';Start-Sleep -Seconds 8}
     'Startup'{Hit 'StartRefresh';Start-Sleep -Seconds 8}
     'Tools'{Hit 'ToolsRefresh';Start-Sleep -Seconds 2}
     'MemoryTools'{Hit 'MemAnalyze';Start-Sleep -Seconds 2}
    }
    Picture "cycle1-$page"
   }
  }
  Hit 'NavSettings'
  [Windows.Forms.SendKeys]::SendWait('{ESC}')
  # A mouse click already opens the ComboBox. Alt+Down here would toggle it
  # closed, and the following Enter would reopen it instead of committing.
  Hit 'ThemePicker';[Windows.Forms.SendKeys]::SendWait('{HOME}'+$(switch($cycle%3){0{''} 1{'{DOWN}'} 2{'{END}'}})+'{ENTER}{ESC}');Start-Sleep -Milliseconds 300
  $expectedTheme=switch($cycle%3){0{'System'} 1{'Light'} 2{'Dark'}}
   if((Get-Content $pref -Raw|ConvertFrom-Json).theme -ne $expectedTheme){throw "Native theme choice failed in cycle $cycle"}
   $verifiedChecks.Add("Native theme verified cycle $cycle")
  Hit 'LanguagePicker'
  if($language -eq 'en'){[Windows.Forms.SendKeys]::SendWait('{END}{ENTER}{ESC}');$language='ar'}else{[Windows.Forms.SendKeys]::SendWait('{HOME}{ENTER}{ESC}');$language='en'}
  Start-Sleep -Milliseconds 500
   if((Get-Content $pref -Raw|ConvertFrom-Json).language -ne $language){throw "Native language choice failed in cycle $cycle"}
   $verifiedChecks.Add("Native language verified cycle $cycle")
  if($cycle -in @(1,2,$Cycles)){Picture "cycle$cycle-settings-$language"}
  Hit 'NavDashboard'
  $null=[MemorySoakNative]::ShowWindow($p.MainWindowHandle,6);Start-Sleep -Milliseconds 600
  $null=[MemorySoakNative]::ShowWindow($p.MainWindowHandle,9);Start-Sleep -Milliseconds 600
  Record 'cycle'
 }
 for($i=0;$i -lt 24;$i++){Start-Sleep -Seconds 5;Record 'cooldown'}
 Picture 'cooldown'
 $null=[MemorySoakNative]::ShowWindow($p.MainWindowHandle,6)
 for($i=0;$i -lt 6;$i++){Start-Sleep -Seconds 5;Record 'minimized'}
 $null=[MemorySoakNative]::ShowWindow($p.MainWindowHandle,9);Foreground
  if(!$p.CloseMainWindow() -or !$p.WaitForExit(15000)){throw 'Product app did not close normally'}
  $verifiedChecks.Add('Native app normal close')
  $exitCode=$p.ExitCode;$mainNormalClose=$true;$p.Dispose();$p=$null
  if(!$counter.WaitForExit(15000)){throw 'Counter collector did not close normally'}
  if($counter.ExitCode -ne 0){throw "Counter collector failed: $($counter.ExitCode)"}
  $verifiedChecks.Add('Counter collector exit zero')
  if(@(Import-Csv "$EvidencePath/counters.csv").Count -eq 0){throw 'Counter collector produced no samples'}
  $verifiedChecks.Add('Counter collector produced samples')
  $counter.Dispose();$counter=$null
  $verifiedChecks.Add('Counter collector normal close')
 $persisted=Get-Content $pref -Raw|ConvertFrom-Json
  if($persisted.language -ne $language -or $persisted.theme -ne 'System'){throw 'Native settings interaction not verified in storage'}
  $verifiedChecks.Add('Native settings persisted')
 Launch;Picture 'restart';Record 'restart'
  if(!$p.CloseMainWindow() -or !$p.WaitForExit(15000)){throw 'Restart did not close normally'}
 $restartExitCode=$p.ExitCode;$restartNormalClose=$true;$verifiedChecks.Add('Native restart normal close');$p.Dispose();$p=$null
 # Validate the native route using the application's bounded typed event log after
 # measurement has ended. This adds no UI Automation or live polling to the run.
 $events=@(Get-ChildItem (Join-Path $env:LOCALAPPDATA 'SystemCore/Logs') -Filter 'systemcore-*.jsonl'|Where-Object {$_.CreationTimeUtc -ge $runStarted.UtcDateTime}|ForEach-Object {
  foreach($line in (Get-Content $_.FullName)){
   $entry=$line|ConvertFrom-Json
   if($entry.Event -in @('Navigation','SettingsSaved') -and ([DateTimeOffset]$entry.Timestamp) -ge $runStarted){$entry|Select-Object Timestamp,Feature,Event,Code}
  }
 })
 $events|ConvertTo-Json -Depth 4|Set-Content "$EvidencePath/navigation-events.json" -Encoding utf8
  # Counted before asserting, so a route that never registered reports how many events it actually
  # produced rather than only that it fell below the bound. The bare throw this replaces could not
  # distinguish a route that never navigated from one whose events had not reached the log yet.
  $navCounts=[ordered]@{}
  foreach($page in @('Dashboard','SystemInfo','Performance','MemoryTools','Cleaner','Uninstaller','Startup','Tools','About','Settings')){
    $navCounts[$page]=@($events|Where-Object {$_.Event -eq 'Navigation' -and $_.Feature -eq $page}).Count
    if($navCounts[$page] -lt ($Cycles-1)){throw "Native route not verified for ${page}: $($navCounts[$page]) events, needed $($Cycles-1)"}
    $verifiedChecks.Add("Native route verified $page")
  }
  if(@($events|Where-Object Event -eq 'SettingsSaved').Count -lt $Cycles){throw 'Native settings changes not verified'}
  $verifiedChecks.Add('Native settings events verified')
  $verifiedChecks|ConvertTo-Json|Set-Content "$EvidencePath/verified-checks.json" -Encoding utf8
  $completed=$true
 [pscustomobject]@{Completed=$true;Cycles=$Cycles;Seconds=$watch.Elapsed.TotalSeconds;Elevated=$false;NormalClose=$mainNormalClose;ExitCode=$exitCode;RestartNormalClose=$restartNormalClose;RestartExitCode=$restartExitCode;RequestedCycles=$Cycles;CompletedCycles=$Cycles;Language=$persisted.language;Theme=$persisted.theme;Method='Native input calibrated by prior separate functional process; passive process and System.Runtime counters; no UIAutomation, no forced GC, no trimming';ControllerAssemblies=@([AppDomain]::CurrentDomain.GetAssemblies().GetName().Name)}|ConvertTo-Json -Depth 4|Set-Content "$EvidencePath/result.json" -Encoding utf8
}catch{$runFailure=$_;throw}finally{
 # The application's own exit code is evidence, and the previous version of this script threw it
 # away: on the failure path $p was disposed here without ever reading ExitCode, so an unexpected
 # exit was reported only as "exited unexpectedly" with no code, no stage and no stderr. The code
 # is captured before disposal, and the report below is written whether the run passed or failed.
 $appExitCode=$null;$appCrashed=$false
 if($p){
  try{$p.Refresh()}catch{}
  if($p.HasExited){$appCrashed=$true;try{$appExitCode=$p.ExitCode}catch{}}
  if(!$p.HasExited){try{$null=$p.CloseMainWindow()}catch{};try{$null=$p.WaitForExit(15000)}catch{}}
  if($p.HasExited -and $null -eq $appExitCode){try{$appExitCode=$p.ExitCode}catch{}}
  try{$p.Dispose()}catch{};$p=$null
 }
 $counterError=$null;if($counter){try{if(!$counter.WaitForExit(15000)){throw 'Counters did not end after normal shutdown'}}catch{$counterError=$_}try{$counter.Dispose()}catch{};$counter=$null}
 $restoreError=$null;try{if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}}catch{$restoreError=$_}
 # Every launch redirects stderr to its own file. A stderr tail alone does not prove a native crash, so
 # the files are inventoried with sizes and tails as one signal among exit codes and log/audit evidence, never as crash proof alone.
 $stderrFiles=@(Get-ChildItem $EvidencePath -Filter 'stderr-*.txt' -ErrorAction SilentlyContinue|ForEach-Object {
  [pscustomobject]@{File=$_.Name;Bytes=$_.Length;Tail=((Get-Content $_.FullName -Tail 20 -ErrorAction SilentlyContinue)-join "`n")}})
 $sampleRows=@()
 if(Test-Path "$EvidencePath/process.json"){foreach($sr in (Get-Content "$EvidencePath/process.json" -Raw|ConvertFrom-Json)){$sampleRows+=$sr}}
 $peak=@($sampleRows|Measure-Object -Property PrivateBytes -Maximum).Maximum
 $durationSeconds='NOT AVAILABLE';try{$durationSeconds=$watch.Elapsed.TotalSeconds}catch{}
 $memStart='NOT AVAILABLE';if(@($sampleRows).Count -gt 0){$memStart=@($sampleRows)[0].PrivateBytes}
 $memEnd='NOT AVAILABLE';if(@($sampleRows).Count -gt 0){$memEnd=@($sampleRows)[-1].PrivateBytes}
 $memPeak='NOT AVAILABLE';if(@($sampleRows).Count -gt 0){$memPeak=$peak}
 $handlesStart='NOT AVAILABLE';if(@($sampleRows).Count -gt 0){$handlesStart=@($sampleRows)[0].Handles}
 $handlesEnd='NOT AVAILABLE';if(@($sampleRows).Count -gt 0){$handlesEnd=@($sampleRows)[-1].Handles}
 $threadsStart='NOT AVAILABLE';if(@($sampleRows).Count -gt 0){$threadsStart=@($sampleRows)[0].Threads}
 $threadsEnd='NOT AVAILABLE';if(@($sampleRows).Count -gt 0){$threadsEnd=@($sampleRows)[-1].Threads}
 $respondingEnd='NOT AVAILABLE';if(@($sampleRows).Count -gt 0){$respondingEnd=@($sampleRows)[-1].Responding}
 $lastStage='NOT AVAILABLE';if(@($sampleRows).Count -gt 0){$lastStage=@($sampleRows)[-1].Stage}
 # System.Runtime counters include handled exceptions, so the file proves the collector ran, not that no unhandled failure occurred.
 $counterRows=0;$counterStatus='NOT AVAILABLE: counters.csv missing'
 if(Test-Path "$EvidencePath/counters.csv"){
  try{$counterRows=@(Import-Csv "$EvidencePath/counters.csv" -ErrorAction Stop).Count;if($counterRows -gt 0){$counterStatus='AVAILABLE'}else{$counterStatus='EMPTY'}}catch{$counterStatus="NOT AVAILABLE: $($_.Exception.Message)";$counterRows=0}
 }
 $counterEvidence=[pscustomobject]@{Status=$counterStatus;Rows=$counterRows;Path="$EvidencePath/counters.csv";Note='System.Runtime thrown-exception counters include handled exceptions; not an unhandled-crash count.'}
 # Durable product evidence for unhandled failures, read-only: %LOCALAPPDATA%/SystemCore/Logs/systemcore-*.jsonl
 # records (Timestamp,Feature,Event,Code) per LocalAppDiagnostics/ApplicationLogRecord. No product changes here.
 $unhandledEvents=@();$logEvidence=$null
 try{
  $logDir=Join-Path $env:LOCALAPPDATA 'SystemCore/Logs'
  if(Test-Path $logDir){
   $logFiles=@(Get-ChildItem $logDir -Filter 'systemcore-*.jsonl' -ErrorAction Stop|Where-Object {$_.CreationTimeUtc -ge $runStarted.UtcDateTime})
   if($logFiles.Count -gt 0){
    foreach($lf in $logFiles){foreach($ln in (Get-Content $lf.FullName -ErrorAction SilentlyContinue)){try{$le=$ln|ConvertFrom-Json -ErrorAction Stop;if($le.Event -eq 'UnhandledFailure'){try{if(([DateTimeOffset]$le.Timestamp) -ge $runStarted){$unhandledEvents+=@($le|Select-Object Timestamp,Feature,Event,Code)}}catch{}}}catch{}}}
    $logEvidence=[pscustomobject]@{Status='AVAILABLE';Files=@($logFiles|ForEach-Object {$_.Name});UnhandledFailureCount=$unhandledEvents.Count;Events=$unhandledEvents}
   }else{$logEvidence=[pscustomobject]@{Status='NOT AVAILABLE: no systemcore-*.jsonl files covering this run window';Files=@();UnhandledFailureCount='NOT AVAILABLE';Events=@()}}
  }else{$logEvidence=[pscustomobject]@{Status='NOT AVAILABLE: log directory missing';Files=@();UnhandledFailureCount='NOT AVAILABLE';Events=@()}}
 }catch{$logEvidence=[pscustomobject]@{Status="NOT AVAILABLE: $($_.Exception.Message)";Files=@();UnhandledFailureCount='NOT AVAILABLE';Events=@()}}
 # Explicit audit publication (RELIABILITY_AUDIT=true) writes %LOCALAPPDATA%/SystemCore/ReliabilityAudit/audit-*.json
 # with Unhandled/UIUnhandled/UnobservedTasks/Overflow/UnexpectedBoundaries per ReliabilityAudit.Save. The gate
 # requires this instrumentation: a missing, unparseable or schema-incomplete audit blocks PASS because unobserved
 # exceptions cannot otherwise be verified zero. No forced GC means zero unobserved events is limited to the observed lifetime.
 $auditEvidence=$null
 try{
  $auditDir=Join-Path $env:LOCALAPPDATA 'SystemCore/ReliabilityAudit'
  if(Test-Path $auditDir){
   $auditFiles=@(Get-ChildItem $auditDir -Filter 'audit-*.json' -ErrorAction Stop|Where-Object {$_.LastWriteTimeUtc -ge $runStarted.UtcDateTime})
   if($auditFiles.Count -gt 0){
    $tU=0;$tUI=0;$tUO=0;$tO=0;$tUB=@();$auditBad=@()
    foreach($af in $auditFiles){
     $a=$null;$parsedOk=$false
     try{$a=Get-Content $af.FullName -Raw -ErrorAction Stop|ConvertFrom-Json -ErrorAction Stop;$parsedOk=$true}catch{$auditBad+=@([pscustomobject]@{File=$af.Name;Reason="unparseable: $($_.Exception.Message)"})}
     if($parsedOk){
      $missing=@()
      foreach($rf in @('Unhandled','UIUnhandled','UnobservedTasks','Overflow','UnexpectedBoundaries')){if($null -eq $a.$rf){$missing+=@($rf)}}
      if($missing.Count -gt 0){$auditBad+=@([pscustomobject]@{File=$af.Name;Reason="missing schema fields: $($missing -join ',')"})}
      else{
       try{
        $tU+=[long]$a.Unhandled;$tUI+=[long]$a.UIUnhandled;$tUO+=[long]$a.UnobservedTasks;$tO+=[long]$a.Overflow
        if($a.UnexpectedBoundaries -isnot [PSCustomObject]){throw 'UnexpectedBoundaries is not an object'}
        foreach($k in @($a.UnexpectedBoundaries.PSObject.Properties)){$tUB+=@([pscustomobject]@{File=$af.Name;Component=$k.Name;Count=[long]$k.Value})}
       }catch{$auditBad+=@([pscustomobject]@{File=$af.Name;Reason="invalid values: $($_.Exception.Message)"})}
      }
     }
    }
    if($auditBad.Count -gt 0){$auditEvidence=[pscustomobject]@{Status="DEGRADED: $($auditBad.Count) of $($auditFiles.Count) audit files failed parse/schema; zero unhandled/unobserved must not be claimed";Files=@($auditFiles|ForEach-Object {$_.Name});BadFiles=$auditBad;Unhandled='NOT AVAILABLE';UIUnhandled='NOT AVAILABLE';UnobservedTasks='NOT AVAILABLE';Overflow='NOT AVAILABLE';UnexpectedBoundaries=$tUB;Note='No forced GC; zero unobserved events is limited to the observed lifetime.'}
    }else{$auditEvidence=[pscustomobject]@{Status='AVAILABLE';Files=@($auditFiles|ForEach-Object {$_.Name});BadFiles=@();Unhandled=$tU;UIUnhandled=$tUI;UnobservedTasks=$tUO;Overflow=$tO;UnexpectedBoundaries=$tUB;Note='No forced GC; zero unobserved events is limited to the observed lifetime.'}}
   }else{$auditEvidence=[pscustomobject]@{Status='NOT AVAILABLE: no audit-*.json files covering this run; PASS requires an instrumented RELIABILITY_AUDIT build, so unobserved exceptions cannot be verified zero';Files=@();Unhandled='NOT AVAILABLE';UIUnhandled='NOT AVAILABLE';UnobservedTasks='NOT AVAILABLE';Overflow='NOT AVAILABLE';UnexpectedBoundaries=@();BadFiles=@()}}
  }else{$auditEvidence=[pscustomobject]@{Status='NOT AVAILABLE: ReliabilityAudit directory absent; PASS requires an instrumented RELIABILITY_AUDIT build, so unobserved exceptions cannot be verified zero';Files=@();Unhandled='NOT AVAILABLE';UIUnhandled='NOT AVAILABLE';UnobservedTasks='NOT AVAILABLE';Overflow='NOT AVAILABLE';UnexpectedBoundaries=@();BadFiles=@()}}
 }catch{$auditEvidence=[pscustomobject]@{Status="NOT AVAILABLE: $($_.Exception.Message)";Files=@();Unhandled='NOT AVAILABLE';UIUnhandled='NOT AVAILABLE';UnobservedTasks='NOT AVAILABLE';Overflow='NOT AVAILABLE';UnexpectedBoundaries=@();BadFiles=@()}}
 # Fail-closed gate: PASS only when every required signal is present and clean, including the audit publication.
 # Missing evidence, any nonzero or unavailable exit code, any unclean exit, any recorded UnhandledFailure, any
 # nonzero audit unhandled/UI-unhandled/unobserved count, any audit overflow, or any malformed audit file fails
 # the gate. A missing audit blocks PASS: unobserved exceptions cannot otherwise be verified zero.
 $gateReasons=[Collections.Generic.List[string]]::new()
 if(-not [bool]$completed){$gateReasons.Add('run did not complete')}
 if($null -eq $exitCode){$gateReasons.Add('main exit code unavailable')}elseif($exitCode -ne 0){$gateReasons.Add("main exit code nonzero: $exitCode")}
 if($null -eq $restartExitCode){$gateReasons.Add('restart exit code unavailable')}elseif($restartExitCode -ne 0){$gateReasons.Add("restart exit code nonzero: $restartExitCode")}
 if($appCrashed){$gateReasons.Add("unclean app exit observed (leftover exit code: $(if($null -ne $appExitCode){$appExitCode}else{'unknown'}))")}
 if(@($sampleRows).Count -eq 0){$gateReasons.Add('process samples unavailable')}
 if($counterEvidence.Status -ne 'AVAILABLE'){$gateReasons.Add("counter evidence $($counterEvidence.Status)")}
 if($stderrFiles.Count -eq 0){$gateReasons.Add('stderr evidence unavailable: no stderr-*.txt files')}
 if($null -eq $navCounts){$gateReasons.Add('navigation evidence unavailable: route assertions never ran')}
 if($logEvidence.Status -ne 'AVAILABLE'){$gateReasons.Add("unhandled-failure log evidence $($logEvidence.Status)")}elseif([int]$logEvidence.UnhandledFailureCount -gt 0){$gateReasons.Add("UnhandledFailure events recorded: $($logEvidence.UnhandledFailureCount)")}
 if($auditEvidence.Status -ne 'AVAILABLE'){$auditBadDetail='';if(@($auditEvidence.BadFiles).Count -gt 0){$auditBadDetail=" [files: $((@($auditEvidence.BadFiles)|ForEach-Object {"$($_.File): $($_.Reason)"}) -join '; ')]"}$gateReasons.Add("audit evidence $($auditEvidence.Status); unobserved exceptions cannot be verified zero$auditBadDetail")}
 else{
  if(([long]$auditEvidence.Unhandled -gt 0) -or ([long]$auditEvidence.UIUnhandled -gt 0)){$gateReasons.Add("audit recorded unhandled/UI-unhandled: $($auditEvidence.Unhandled)/$($auditEvidence.UIUnhandled)")}
  if([long]$auditEvidence.UnobservedTasks -gt 0){$gateReasons.Add("audit recorded unobserved tasks: $($auditEvidence.UnobservedTasks)")}
  if([long]$auditEvidence.Overflow -gt 0){$gateReasons.Add("audit key overflow: $($auditEvidence.Overflow); event attribution is incomplete")}
  if(@($auditEvidence.BadFiles).Count -gt 0){$gateReasons.Add("audit files failed parse/schema: $((@($auditEvidence.BadFiles)|ForEach-Object {$_.File}) -join ',')")}
 }
 $gate='FAIL';if($gateReasons.Count -eq 0){$gate='PASS'}
 $reportError=$null;try{
 [pscustomobject]@{
  Completed=[bool]$completed;RequestedCycles=$Cycles;CompletedCycles=$(if([bool]$completed){$Cycles}else{0});LastAttemptedCycle=$(try{if([bool]$completed){$Cycles}else{$cycle}}catch{'NOT AVAILABLE'});Cycles=$Cycles;Seconds=$durationSeconds
  MainExitCode=$(if($null -ne $exitCode){$exitCode}else{'NOT AVAILABLE'});RestartExitCode=$(if($null -ne $restartExitCode){$restartExitCode}else{'NOT AVAILABLE'});LeftoverExitCode=$(if($null -ne $appExitCode){$appExitCode}else{'NOT AVAILABLE'});AppExitCode=$(if($null -ne $appExitCode){$appExitCode}else{$exitCode});AppCrashed=$appCrashed
  NavigationCounts=$navCounts
  LastStage=$lastStage;Samples=$sampleRows.Count
  MemoryStart=$memStart;MemoryEnd=$memEnd;MemoryPeak=$memPeak
  HandlesStart=$handlesStart;HandlesEnd=$handlesEnd
  ThreadsStart=$threadsStart;ThreadsEnd=$threadsEnd
  RespondingEnd=$respondingEnd
  Stderr=$stderrFiles;Counters=$counterEvidence;AppLog=$logEvidence;ReliabilityAudit=$auditEvidence;Gate=$gate;GateReasons=$gateReasons
 }|ConvertTo-Json -Depth 6|Set-Content "$EvidencePath/stability-report.json" -Encoding utf8
 }catch{$reportError=$_;try{[pscustomobject]@{Completed=[bool]$completed;RequestedCycles=$Cycles;Gate='FAIL';GateReasons=@('stability-report build failed',"$reportError");MainExitCode=$(if($null -ne $exitCode){$exitCode}else{'NOT AVAILABLE'});RestartExitCode=$(if($null -ne $restartExitCode){$restartExitCode}else{'NOT AVAILABLE'});Stderr=$stderrFiles}|ConvertTo-Json -Depth 4|Set-Content "$EvidencePath/stability-report.json" -Encoding utf8}catch{}}
 if($null -eq $runFailure){
  if($null -ne $counterError){throw $counterError}
  elseif($null -ne $restoreError){throw $restoreError}
  elseif($null -ne $reportError){throw $reportError}
  elseif($gate -ne 'PASS'){throw "Stability gate FAIL: $($gateReasons -join '; ')"}
 }
}