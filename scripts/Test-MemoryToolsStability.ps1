param([string]$AppPath='artifacts/phase7/app/SystemCore.App.exe',[string]$EvidencePath='artifacts/phase7/completion/stability',[int]$Cycles=20)
$ErrorActionPreference='Stop'
$EvidencePath=[IO.Path]::GetFullPath($EvidencePath)
New-Item -ItemType Directory -Force $EvidencePath | Out-Null
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
# Native mouse/keyboard input and passive process counters. No UI Automation.
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class MemorySoakNative {
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int command);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,IntPtr pid);
 [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint from,uint to,bool attach);
 [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint x,uint y,int data,UIntPtr extra);
}
'@
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json';$saved=if(Test-Path $pref){[IO.File]::ReadAllBytes($pref)}else{$null}
[IO.File]::WriteAllText($pref,'{"theme":"Light","language":"en"}')
$p=$null;$counter=$null;$samples=[Collections.Generic.List[object]]::new();$cycle=0
$watch=[Diagnostics.Stopwatch]::StartNew()
function Record([string]$stage){
 $p.Refresh();if($p.HasExited){throw 'Memory Tools app exited unexpectedly'}
 $samples.Add([pscustomobject]@{Stage=$stage;Cycle=$cycle;Seconds=$watch.Elapsed.TotalSeconds;PrivateBytes=$p.PrivateMemorySize64;WorkingSet=$p.WorkingSet64;CpuSeconds=$p.TotalProcessorTime.TotalSeconds;Handles=$p.HandleCount;Threads=$p.Threads.Count;Responding=$p.Responding})
 $samples|ConvertTo-Json|Set-Content "$EvidencePath/process.json" -Encoding utf8
}
function Foreground {
 $focusWatch=[Diagnostics.Stopwatch]::StartNew()
 do {
  $from=[MemorySoakNative]::GetCurrentThreadId()
  $to=[MemorySoakNative]::GetWindowThreadProcessId([MemorySoakNative]::GetForegroundWindow(),[IntPtr]::Zero)
  $attached=$false
  try {
   if($to -ne 0 -and $to -ne $from){$attached=[MemorySoakNative]::AttachThreadInput($from,$to,$true)}
   $null=[MemorySoakNative]::BringWindowToTop($p.MainWindowHandle)
   $null=[MemorySoakNative]::SetForegroundWindow($p.MainWindowHandle)
  } finally {if($attached){$null=[MemorySoakNative]::AttachThreadInput($from,$to,$false)}}
  Start-Sleep -Milliseconds 200
 }while([MemorySoakNative]::GetForegroundWindow() -ne $p.MainWindowHandle -and $focusWatch.Elapsed.TotalSeconds -lt 5)
 if([MemorySoakNative]::GetForegroundWindow() -ne $p.MainWindowHandle){throw 'Native input target lost foreground'}
}
function Click([int]$x,[int]$y){
 Foreground
 $r=New-Object MemorySoakNative+Rect;$null=[MemorySoakNative]::GetWindowRect($p.MainWindowHandle,[ref]$r)
 $null=[MemorySoakNative]::SetCursorPos($r.Left+$x,$r.Top+$y)
 [MemorySoakNative]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[MemorySoakNative]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 200
}
function Wheel([int]$amount){
 Foreground
 $r=New-Object MemorySoakNative+Rect;$null=[MemorySoakNative]::GetWindowRect($p.MainWindowHandle,[ref]$r)
 $null=[MemorySoakNative]::SetCursorPos($r.Left+900,$r.Top+570)
 [MemorySoakNative]::mouse_event(0x800,0,0,$amount,[UIntPtr]::Zero);Start-Sleep -Milliseconds 300
}
function Picture([string]$name){
 Foreground;$r=New-Object MemorySoakNative+Rect;$null=[MemorySoakNative]::GetWindowRect($p.MainWindowHandle,[ref]$r)
 $image=New-Object Drawing.Bitmap(($r.Right-$r.Left),($r.Bottom-$r.Top));$g=[Drawing.Graphics]::FromImage($image)
 try{$g.CopyFromScreen($r.Left,$r.Top,0,0,$image.Size);$image.Save("$EvidencePath/$name.png",[Drawing.Imaging.ImageFormat]::Png)}finally{$g.Dispose();$image.Dispose()}
}
try{
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 $launch=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $launch.Elapsed.TotalSeconds -lt 30)
 if($p.HasExited -or $p.MainWindowHandle -eq [IntPtr]::Zero){throw 'Native launch failed'}
 $elevated=& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Get-ProcessElevation.ps1)))) -ProcessId $p.Id
 if($elevated){throw 'Soak requires non-elevated app'}
 $null=[MemorySoakNative]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,40,40,1120,760,0x14)
 Start-Sleep -Seconds 3
 $env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
 $counter=Start-Process 'C:\Users\Falcone\AppData\Local\Temp\opencode\memory-tools\dotnet-counters.exe' -ArgumentList 'collect','--process-id',"$($p.Id)",'--counters','EventCounters\System.Runtime','--refresh-interval','5','--format','csv','--output',('"'+"$EvidencePath/counters.csv"+'"') -WindowStyle Hidden -PassThru -RedirectStandardOutput "$EvidencePath/counter-log.txt" -RedirectStandardError "$EvidencePath/counter-error.txt"
 Click 105 363
 for($i=0;$i -lt 12;$i++){Start-Sleep -Seconds 5;Record 'page-open'}
 Picture 'open'
 for($cycle=1;$cycle -le $Cycles;$cycle++){
  Wheel 2400
  Click 885 75 # Analyze
  Click 1010 75 # Optimize (production has no releasable cache provider)
  # Optimize temporarily disables itself and WinUI may move focus. Re-anchor on
  # the continuously enabled Analyze button before traversing the verified order.
  Click 885 75
  [Windows.Forms.SendKeys]::SendWait('{TAB}{TAB}')
  [Windows.Forms.SendKeys]::SendWait('%{DOWN}{HOME}{ENTER}')
  Start-Sleep -Milliseconds 200
  if($cycle -eq 1){Picture 'top-memory'}
  [Windows.Forms.SendKeys]::SendWait('%{DOWN}{END}{ENTER}')
  Start-Sleep -Milliseconds 200
  if($cycle -eq 1){Picture 'top-cpu'}
  Click 105 124 # Dashboard
  Click 105 363 # Memory Tools
  Start-Sleep -Seconds 2
  Wheel 3600
  $null=[MemorySoakNative]::ShowWindow($p.MainWindowHandle,6);Start-Sleep -Milliseconds 400
  $null=[MemorySoakNative]::ShowWindow($p.MainWindowHandle,9);Start-Sleep -Milliseconds 400
  Record 'cycle'
 }
 Picture 'after-cycles'
 Click 105 124
 for($i=0;$i -lt 12;$i++){Start-Sleep -Seconds 5;Record 'page-hidden'}
 $null=[MemorySoakNative]::ShowWindow($p.MainWindowHandle,6)
 for($i=0;$i -lt 12;$i++){Start-Sleep -Seconds 5;Record 'minimized'}
 $null=[MemorySoakNative]::ShowWindow($p.MainWindowHandle,9)
 Click 105 363
 for($i=0;$i -lt 24;$i++){Start-Sleep -Seconds 5;Record 'cooldown'}
 Picture 'cooldown'
 if(!$p.CloseMainWindow() -or !$p.WaitForExit(15000)){throw 'App did not close normally'}
 [pscustomobject]@{Completed=$true;Cycles=$Cycles;Seconds=$watch.Elapsed.TotalSeconds;Elevated=$elevated;NormalClose=$true;ExitCode=$p.ExitCode;Method='Native input + process counters + System.Runtime counters; no UI Automation, no forced GC'}|ConvertTo-Json|Set-Content "$EvidencePath/result.json" -Encoding utf8
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(15000)};$p.Dispose()}
 if($counter){if(!$counter.WaitForExit(15000)){throw 'Counters did not end after normal app shutdown'};$counter.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
}
