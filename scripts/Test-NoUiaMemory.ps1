param([string]$EvidencePath='artifacts/debug/no-uia-final')
$ErrorActionPreference='Stop'
$EvidencePath=[IO.Path]::GetFullPath($EvidencePath)
New-Item -ItemType Directory -Force $EvidencePath|Out-Null
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
$sdk="$env:DOTNET_ROOT\dotnet.exe"
$counterTool='C:\Users\Falcone\AppData\Local\Temp\opencode\memory-tools\dotnet-counters.exe'
$principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Use an unelevated controller'}
# No UIAutomation assemblies, accessibility queries, debugger, or heap profiler.
$p=$null;$counter=$null;$rows=[Collections.Generic.List[object]]::new()
try {
 $p=Start-Process (Resolve-Path artifacts/publish/App/SystemCore.App.exe) -PassThru -RedirectStandardError "$EvidencePath/A-stderr.txt"
 Start-Sleep -Seconds 3
 if($p.HasExited){throw 'Production app exited during startup'}
 $counter=Start-Process $counterTool -ArgumentList 'collect','--process-id',"$($p.Id)",'--counters','EventCounters\System.Runtime','--refresh-interval','5','--format','csv','--output',('"'+"$EvidencePath/A-counters.csv"+'"') -PassThru -RedirectStandardOutput "$EvidencePath/A-counter-log.txt" -RedirectStandardError "$EvidencePath/A-counter-error.txt"
 $clock=[Diagnostics.Stopwatch]::StartNew()
 for($minute=0;$minute -le 10;$minute++){
  if($minute){Start-Sleep -Seconds 60}
  $p.Refresh();if($p.HasExited){throw 'Production app exited during idle'}
  $rows.Add([pscustomobject]@{Minute=$minute;Seconds=$clock.Elapsed.TotalSeconds;Pid=$p.Id;PrivateBytes=$p.PrivateMemorySize64;WorkingSet=$p.WorkingSet64;Handles=$p.HandleCount;Threads=$p.Threads.Count;Responding=$p.Responding})
  $rows|ConvertTo-Json|Set-Content "$EvidencePath/A-observations.json" -Encoding utf8
 }
 $null=$p.CloseMainWindow()
 if(!$p.WaitForExit(15000)){throw 'Production app did not close normally'}
 [pscustomobject]@{Completed=$true;DurationSeconds=$clock.Elapsed.TotalSeconds;ExitCode=$p.ExitCode}|ConvertTo-Json|Set-Content "$EvidencePath/A-result.json" -Encoding utf8
} finally {
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(10000)};$p.Dispose()}
 if($counter){if(!$counter.WaitForExit(15000)){throw 'Counters did not exit after target shutdown'};$counter.Dispose()}
}
& $sdk tests/SystemCore.CleanerTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.CleanerTests.dll --memory-engine-isolation "$EvidencePath/BC-observations.json" 1> "$EvidencePath/BC-stdout.json" 2> "$EvidencePath/BC-stderr.txt"
if($LASTEXITCODE){throw 'Core workload/cooldown failed'}
[pscustomobject]@{Completed=$true;Cycles=100;CooldownSeconds=300;ExitCode=$LASTEXITCODE}|ConvertTo-Json|Set-Content "$EvidencePath/BC-result.json" -Encoding utf8
