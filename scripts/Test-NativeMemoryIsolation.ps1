param([string]$AppPath='artifacts/debug/final-validation-app/SystemCore.App.exe',
 [string]$EvidencePath='artifacts/debug/native/isolation', [string[]]$Phases=@('A','B','C','D'),
 [switch]$DiagnosticCooldownGc)
$ErrorActionPreference='Stop'
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
$vmmap='C:\Users\Falcone\AppData\Local\Temp\opencode\native-memory-tools\VMMap\vmmap64.exe'
$counterTool='C:\Users\Falcone\AppData\Local\Temp\opencode\memory-tools\dotnet-counters.exe'
$gcTool='C:\Users\Falcone\AppData\Local\Temp\opencode\memory-tools\dotnet-gcdump.exe'
$driver=[IO.File]::ReadAllText((Resolve-Path scripts/Test-CleanerLifecycle.ps1))
. ([scriptblock]::Create($driver.Substring(0,$driver.IndexOf('$p=$null;$cycle=0')))) -AppPath $AppPath -EvidencePath $EvidencePath
$all=[Collections.Generic.List[object]]::new()
$phase='';$p=$null;$counter=$null;$cycle=0
function Record([string]$label,[bool]$map=$false) {
 $p.Refresh()
 $row=[ordered]@{Phase=$phase;Label=$label;Cycle=$cycle;Seconds=$watch.Elapsed.TotalSeconds;PrivateBytes=$p.PrivateMemorySize64;WorkingSet=$p.WorkingSet64;Handles=$p.HandleCount;Threads=$p.Threads.Count;Responding=$p.Responding}
 $status=Find 'CleanerValidationStatus'
 if($status){$row.Diagnostics=$status.Current.Name}
 $all.Add([pscustomobject]$row)
 $all | ConvertTo-Json -Depth 5 | Set-Content "$EvidencePath/observations.json" -Encoding utf8
 if($map){
  $file=[IO.Path]::GetFullPath("$EvidencePath/$phase-$label.xml")
  $capture=Start-Process $vmmap -ArgumentList '-accepteula','-p',"$($p.Id)",('"'+$file+'"') -PassThru
  if(!$capture.WaitForExit(60000)){throw 'VMMap capture timeout'}
  if($capture.ExitCode -ne 0 -or !(Test-Path $file)){throw 'VMMap capture failed'}
  $capture.Dispose()
 }
}
try {
 foreach($phase in $Phases) {
  $cycle=0;$counter=$null
  try {
   $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/$phase-stderr.txt"
   $watch=[Diagnostics.Stopwatch]::StartNew()
   do {Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq 0 -and $watch.Elapsed.TotalSeconds -lt 30)
   if($p.HasExited -or $p.MainWindowHandle -eq 0){throw 'Launch failed'}
   Start-Sleep -Seconds 3
   $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
   $null=[CleanerNative]::ShowWindow($p.MainWindowHandle,9)
   $counter=Start-Process $counterTool -ArgumentList 'collect','--process-id',"$($p.Id)",'--counters','System.Runtime','--refresh-interval','5','--format','csv','--output',('"'+[IO.Path]::GetFullPath("$EvidencePath/$phase-counters.csv")+'"') -PassThru -RedirectStandardOutput "$EvidencePath/$phase-counter-log.txt" -RedirectStandardError "$EvidencePath/$phase-counter-error.txt"
   Record 'start' $true
   if($phase -in @('A','E')) {
    if($phase -eq 'E'){Navigate 'NavPerformance'}
    # Genuine idle baseline: no Cleaner navigation or scans.
    for($minute=1;$minute -le 5;$minute++){Start-Sleep -Seconds 60;Record "idle-$minute" $true}
   } else {
    for($cycle=1;$cycle -le 20;$cycle++) {
     switch($phase) {
      'B' {foreach($page in @('Dashboard','Cleaner','Performance','SystemInfo','Dashboard')){Navigate "Nav$page";Start-Sleep -Milliseconds 500}}
      'C' {if($cycle -eq 1){Navigate 'NavCleaner'};Scan;Scan}
      'D' {
       Navigate 'NavCleaner'
       Assert 'Synthetic build marker required before fixture preparation' ($null -ne (Find 'CleanerValidationPrepare'))
       Invoke 'CleanerValidationPrepare';Scan;Review
       ToggleItem 'CleanerItem-05-unselected.tmp'
       Invoke 'CleanerNext';Start-Sleep -Milliseconds 250
       ToggleItem 'CleanerItem-page-104.tmp'
       Invoke 'CleanerPrevious';Start-Sleep -Milliseconds 250
       Invoke 'CleanerClean';Start-Sleep -Milliseconds 200
       Assert 'Synthetic clean requires confirmation' ($null -ne (Find 'CleanerConfirmation'))
       Invoke 'PrimaryButton';Start-Sleep -Milliseconds 200;Wait-Idle
       $status=(Find 'CleanerValidationStatus').Current.Name
       Assert 'Synthetic clean verified on disk' ($status -match 'Deleted:105;Skipped:4;Failed:0;Bytes:111;Root:True;Unselected:True;PageExcluded:True;Changed:True;Gone:True;Locked:True;Rejected:True') $status
       Scan
       Navigate 'NavDashboard';Navigate 'NavCleaner';Scan
      }
      'F' {
       Navigate 'NavSettings'
       $old=if($cycle%2 -eq 1){$en}else{$ar}
       $theme=@('ThemeLight','ThemeDark','ThemeSystem')[$cycle%3]
       Choose 'ThemePicker' (($old | Where-Object name -eq $theme).value)
       Choose 'LanguagePicker' $(if($cycle%2 -eq 1){($ar | Where-Object name -eq 'Arabic').value}else{'English'})
       Navigate 'NavDashboard'
       $null=[CleanerNative]::ShowWindow($p.MainWindowHandle,6);Start-Sleep -Milliseconds 500
       $null=[CleanerNative]::ShowWindow($p.MainWindowHandle,9);Start-Sleep -Milliseconds 500
      }
     }
     Record "cycle-$cycle" ($cycle -in @(1,10,20))
     Start-Sleep -Seconds 3
    }
   }
   if($phase -eq 'D' -and $DiagnosticCooldownGc) {
    Navigate 'NavDashboard';Record 'cooldown-start' $true
    for($minute=1;$minute -le 5;$minute++){Start-Sleep -Seconds 60;Record "cooldown-$minute" ($minute -eq 5)}
    # Exactly one diagnostic full GC request after the cooldown. Not a fix or soak.
    & $gcTool collect --process-id $p.Id --output "$EvidencePath/D-after-cooldown.gcdump" | Out-File "$EvidencePath/D-gc-log.txt"
    if($LASTEXITCODE){throw 'Diagnostic GC collection failed'}
    Start-Sleep -Seconds 10;Record 'after-diagnostic-gc' $true
   }
  } catch {
   $_ | Out-String | Set-Content "$EvidencePath/$phase-error.txt" -Encoding utf8
  } finally {
   if($p -and !$p.HasExited){$null=$p.CloseMainWindow();if(!$p.WaitForExit(10000)){throw 'Application did not shut down cleanly'}}
   if($counter){if(!$counter.WaitForExit(15000)){throw 'Counter collector did not exit after target shutdown'};$counter.Dispose()}
   if($p){$p.Dispose();$p=$null}
  }
 }
} finally {
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $checks | ConvertTo-Json -Depth 5 | Set-Content "$EvidencePath/ui-checks.json" -Encoding utf8
}
