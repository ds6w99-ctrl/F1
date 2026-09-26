param([string]$AppPath='artifacts/phase8/app/SystemCore.App.exe',[string]$EvidencePath='artifacts/phase8/completion/stability',[int]$Cycles=24)
$ErrorActionPreference='Stop'
# Reuse only native input/counter helpers. This process never loads UIAutomation.
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-MemoryToolsStability.ps1))
$prefix=$source.Substring(0,$source.LastIndexOf('try{',$source.IndexOf('$p=Start-Process')))
. ([scriptblock]::Create($prefix)) -AppPath $AppPath -EvidencePath $EvidencePath -Cycles $Cycles
if($null -ne $saved){[IO.File]::WriteAllBytes("$EvidencePath/original-preferences.bin",$saved)}
$coordinates=Get-Content artifacts/phase8/completion/ui/native-coordinates.json -Raw|ConvertFrom-Json
$language='en'
$runStarted=[DateTimeOffset]::UtcNow
function Hit([string]$id){$point=$coordinates.$language.$id;if(!$point){throw "Missing native coordinate $language $id"};Click $point.X $point.Y}
function Launch {
 Set-Variable -Scope 1 -Name p -Value (Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr-$([Guid]::NewGuid().ToString('N')).txt")
 $launch=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $launch.Elapsed.TotalSeconds -lt 30)
 if($p.HasExited -or $p.MainWindowHandle -eq [IntPtr]::Zero){throw 'Native launch failed'}
 $elevated=& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Get-ProcessElevation.ps1)))) -ProcessId $p.Id
 if($elevated){throw 'Product stability requires non-elevated app'}
 $null=[MemorySoakNative]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,40,40,1120,760,0x14)
 Start-Sleep -Seconds 3;Foreground
}
try{
 if([AppDomain]::CurrentDomain.GetAssemblies().GetName().Name -match 'UIAutomation'){throw 'Fresh controller required: UIAutomation loaded'}
 Launch
 $env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
 $counter=Start-Process 'C:\Users\Falcone\AppData\Local\Temp\opencode\memory-tools\dotnet-counters.exe' -ArgumentList 'collect','--process-id',"$($p.Id)",'--counters','EventCounters\System.Runtime','--refresh-interval','5','--format','csv','--output',('"'+"$EvidencePath/counters.csv"+'"') -WindowStyle Hidden -PassThru -RedirectStandardOutput "$EvidencePath/counter-log.txt" -RedirectStandardError "$EvidencePath/counter-error.txt"
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
  Hit 'LanguagePicker'
  if($language -eq 'en'){[Windows.Forms.SendKeys]::SendWait('{END}{ENTER}{ESC}');$language='ar'}else{[Windows.Forms.SendKeys]::SendWait('{HOME}{ENTER}{ESC}');$language='en'}
  Start-Sleep -Milliseconds 500
  if((Get-Content $pref -Raw|ConvertFrom-Json).language -ne $language){throw "Native language choice failed in cycle $cycle"}
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
 $exitCode=$p.ExitCode;$p.Dispose();$p=$null
 if(!$counter.WaitForExit(15000)){throw 'Counter collector did not close normally'};$counter.Dispose();$counter=$null
 $persisted=Get-Content $pref -Raw|ConvertFrom-Json
 if($persisted.language -ne $language -or $persisted.theme -ne 'System'){throw 'Native settings interaction not verified in storage'}
 Launch;Picture 'restart';Record 'restart'
 if(!$p.CloseMainWindow() -or !$p.WaitForExit(15000)){throw 'Restart did not close normally'}
 # Validate the native route using the application's bounded typed event log after
 # measurement has ended. This adds no UI Automation or live polling to the run.
 $events=@(Get-ChildItem (Join-Path $env:LOCALAPPDATA 'SystemCore/Logs') -Filter 'systemcore-*.jsonl'|Where-Object {$_.CreationTimeUtc -ge $runStarted.UtcDateTime}|ForEach-Object {
  foreach($line in (Get-Content $_.FullName)){
   $entry=$line|ConvertFrom-Json
   if($entry.Event -in @('Navigation','SettingsSaved') -and ([DateTimeOffset]$entry.Timestamp) -ge $runStarted){$entry|Select-Object Timestamp,Feature,Event,Code}
  }
 })
 $events|ConvertTo-Json -Depth 4|Set-Content "$EvidencePath/navigation-events.json" -Encoding utf8
 foreach($page in @('Dashboard','SystemInfo','Performance','MemoryTools','Cleaner','Uninstaller','Startup','Tools','About','Settings')){
  if(@($events|Where-Object {$_.Event -eq 'Navigation' -and $_.Feature -eq $page}).Count -lt ($Cycles-1)){throw "Native route not verified for $page"}
 }
 if(@($events|Where-Object Event -eq 'SettingsSaved').Count -lt $Cycles){throw 'Native settings changes not verified'}
 [pscustomobject]@{Completed=$true;Cycles=$Cycles;Seconds=$watch.Elapsed.TotalSeconds;Elevated=$false;NormalClose=$true;ExitCode=$exitCode;RestartNormalClose=$true;Language=$persisted.language;Theme=$persisted.theme;Method='Native input calibrated by prior separate functional process; passive process and System.Runtime counters; no UIAutomation, no forced GC, no trimming';ControllerAssemblies=@([AppDomain]::CurrentDomain.GetAssemblies().GetName().Name)}|ConvertTo-Json -Depth 4|Set-Content "$EvidencePath/result.json" -Encoding utf8
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(15000)};$p.Dispose()}
 if($counter){if(!$counter.WaitForExit(15000)){throw 'Counters did not end after normal shutdown'};$counter.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
}
