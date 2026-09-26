param([string]$AppPath,[string]$EvidencePath,[int]$Minutes=20)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
if(!('PerformanceSoakNative' -as [type])) {
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PerformanceSoakNative {
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int command);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
}
'@
}
New-Item -ItemType Directory -Force $EvidencePath | Out-Null
$path=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
$existed=Test-Path $path
$original=if($existed){[IO.File]::ReadAllBytes($path)}else{$null}
$rows=[Collections.Generic.List[object]]::new()
$p=$null
function Find([string]$id) {
 $window.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$id))
}
function Nav([string]$id) { (Find $id).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select() }
function Record-Observation([string]$state,[int]$seconds) {
 $p.Refresh()
 $cpu=$p.TotalProcessorTime.TotalMilliseconds
 $clock=[Diagnostics.Stopwatch]::StartNew()
 Start-Sleep -Seconds $seconds
 $p.Refresh()
 if($p.HasExited){throw 'Application exited during observation'}
 $clock.Stop()
 $row=[pscustomobject]@{State=$state;Time=[DateTimeOffset]::Now.ToString('o');Seconds=$clock.Elapsed.TotalSeconds;CpuPercent=100*($p.TotalProcessorTime.TotalMilliseconds-$cpu)/$clock.Elapsed.TotalMilliseconds/[Environment]::ProcessorCount;PrivateBytes=$p.PrivateMemorySize64;WorkingSet=$p.WorkingSet64;Handles=$p.HandleCount;Responding=$p.Responding;Minimized=[PerformanceSoakNative]::IsIconic($p.MainWindowHandle)}
 $rows.Add($row)
 $rows | ConvertTo-Json -Depth 4 | Set-Content "$EvidencePath/observations.json" -Encoding UTF8
 if(!$p.Responding){throw 'Application is unresponsive'}
}
try {
 New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
 [IO.File]::WriteAllText($path,'{"theme":"System","language":"en"}')
 Get-FileHash (Join-Path (Split-Path $AppPath) 'SystemCore.App.dll'),(Join-Path (Split-Path $AppPath) 'SystemCore.Infrastructure.dll'),(Join-Path (Split-Path $AppPath) 'SystemCore.Core.dll') | Select-Object Path,Hash | ConvertTo-Json | Set-Content "$EvidencePath/hashes.json"
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 Start-Sleep -Seconds 8
 $p.Refresh()
 if($p.HasExited){throw 'Launch failed'}
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 Record-Observation 'Dashboard' 30
 Nav 'NavPerformance'
 Start-Sleep -Seconds 8
 foreach($id in @('PerfCpu','PerfMemory','PerfDisk','PerfNetwork')) {
  if((Find $id).Current.Name -match 'Not available'){throw "Unavailable metric on current host: $id"}
 }
 $start=[DateTimeOffset]::Now
 $clock=[Diagnostics.Stopwatch]::StartNew()
 while($clock.Elapsed.TotalMinutes -lt $Minutes) { Record-Observation 'Performance' 30 }
 $duration=$clock.Elapsed.TotalSeconds
 $history=(Find 'PerfHistory').Current.Name
 $count=[int]([regex]::Match($history,'\d+').Value)
 if($count -lt 50 -or $count -gt 60){throw "Unexpected bounded history: $history"}
 Nav 'NavDashboard'
 Start-Sleep -Seconds 2
 Nav 'NavPerformance'
 Start-Sleep -Seconds 5
 $null=[PerformanceSoakNative]::ShowWindow($p.MainWindowHandle,6)
 Start-Sleep -Seconds 3
 Record-Observation 'Minimized' 30
 $null=[PerformanceSoakNative]::ShowWindow($p.MainWindowHandle,9)
 Start-Sleep -Seconds 5
 if((Find 'PerfCpu').Current.Name -match 'Not available'){throw 'Resume did not restore CPU data'}
 $null=$p.CloseMainWindow()
 if(!$p.WaitForExit(10000)){throw 'Close did not exit'}
 [pscustomobject]@{Result='PASS';Started=$start.ToString('o');PerformanceSeconds=$duration;HistoryCount=$count;ExitCode=$p.ExitCode;ObservationCount=$rows.Count;Scope='Development observation; not production soak'} | ConvertTo-Json | Set-Content "$EvidencePath/result.json"
} catch {
 [pscustomobject]@{Result='FAIL';Error=$_.Exception.Message} | ConvertTo-Json | Set-Content "$EvidencePath/result.json"
 throw
} finally {
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(10000)};$p.Dispose()}
 if($existed){[IO.File]::WriteAllBytes($path,$original)}else{Remove-Item $path -ErrorAction SilentlyContinue}
}
