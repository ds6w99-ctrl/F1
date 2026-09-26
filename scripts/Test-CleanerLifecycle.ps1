param([string]$AppPath, [string]$EvidencePath, [int]$DurationSeconds=0,
 [int]$MinimumCycles=1, [string]$DumpTool='', [int[]]$SnapshotCycles=@(0,1,10,21),
 [string]$DiagnosticGcDumpTool='', [string]$NativeMapTool='', [int]$StartupIdleSeconds=0)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
if(!('CleanerNative' -as [type])) { Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class CleanerNative {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int L,T,R,B; }
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd,int command);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd,IntPtr dc,uint flags);
 [DllImport("advapi32.dll",SetLastError=true)] private static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
 [DllImport("advapi32.dll",SetLastError=true)] private static extern bool GetTokenInformation(IntPtr token,int kind,out int value,int length,out int returned);
 [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
 public static bool IsElevated(IntPtr process) {
  IntPtr token;
  if(!OpenProcessToken(process,8,out token))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
  try {int value,returned;if(!GetTokenInformation(token,20,out value,4,out returned))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());return value!=0;}
  finally {CloseHandle(token);}
 }
}
'@ }
New-Item -ItemType Directory -Force $EvidencePath | Out-Null
$checks=[Collections.Generic.List[object]]::new()
$observations=[Collections.Generic.List[object]]::new()
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
$saved=if(Test-Path $pref){[IO.File]::ReadAllBytes($pref)}else{$null}
[IO.File]::WriteAllText($pref,'{"theme":"System","language":"en"}')
$en=([xml][IO.File]::ReadAllText((Resolve-Path 'src/SystemCore.App/Resources/Shell.resx'))).root.data
$ar=([xml][IO.File]::ReadAllText((Resolve-Path 'src/SystemCore.App/Resources/Shell.ar.resx'))).root.data
function Find([string]$id) {
 # Refresh the root provider across minimize/restore and language relayout.
 $root=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 $root.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$id))
}
function Assert([string]$name,[bool]$value,$detail=$null) { $checks.Add([pscustomobject]@{Cycle=$cycle;Test=$name;Passed=$value;Detail=$detail}); if(!$value){throw $name} }
function Invoke([string]$id) {
 $ready=[Diagnostics.Stopwatch]::StartNew()
 do {$e=Find $id;if(!$e){Start-Sleep -Milliseconds 100}}while(!$e -and $ready.Elapsed.TotalSeconds -lt 5)
 if(!$e){throw "Missing $id after bounded UIA wait"}
 $e.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Navigate([string]$id) {
 $null=[CleanerNative]::ShowWindow($p.MainWindowHandle,9)
 Ensure-TestViewport
 $null=[CleanerNative]::SetForegroundWindow($p.MainWindowHandle)
 $ready=[Diagnostics.Stopwatch]::StartNew()
 $opened=$false
 do {
  $nav=Find $id
  if(!$nav -and !$opened){$toggle=Find 'TogglePaneButton';if($toggle){$toggle.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();$opened=$true}}
  if(!$nav){Start-Sleep -Milliseconds 100}
 }while(!$nav -and $ready.Elapsed.TotalSeconds -lt 5)
 if(!$nav){throw "Navigation element $id unavailable after bounded wait"}
 $nav.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 200
}
function Ensure-TestViewport {
 $rect=New-Object CleanerNative+Rect
 $null=[CleanerNative]::GetWindowRect($p.MainWindowHandle,[ref]$rect)
 $width=[Math]::Min(1120,[CleanerNative]::GetSystemMetrics(0)-80)
 $height=[Math]::Min(760,[CleanerNative]::GetSystemMetrics(1)-80)
 if(($rect.R-$rect.L) -ne $width -or ($rect.B-$rect.T) -ne $height){
  $null=[CleanerNative]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,40,40,$width,$height,0x14)
  Start-Sleep -Milliseconds 200
 }
}
function Wait-Idle { $w=[Diagnostics.Stopwatch]::StartNew(); do { Start-Sleep -Milliseconds 100; $e=Find 'CleanerScan' } while((!$e -or !$e.Current.IsEnabled) -and $w.Elapsed.TotalSeconds -lt 20); Assert 'Operation returns idle' ($e -and $e.Current.IsEnabled) }
function Scan { Invoke 'CleanerScan'; Start-Sleep -Milliseconds 100; Wait-Idle }
function Scroll-IfPossible($element,[double]$percent) {
 if(!$element){return}
 try {
  if($element.GetCurrentPropertyValue([Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty)) {
   $scroll=$element.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
   if($scroll.Current.VerticallyScrollable){$scroll.SetScrollPercent(-1,$percent)}
  }
 } catch {
  # Virtualized UIA elements can be replaced between query and scroll. Only this
  # non-destructive realization hint tolerates that race; assertions still time out.
  $cause=$_.Exception.GetBaseException()
  if($cause -isnot [InvalidOperationException] -and $cause -isnot [Windows.Automation.ElementNotAvailableException]){throw}
 }
}
function Review {
 $ready=[Diagnostics.Stopwatch]::StartNew();$item=$null
 do {
  $list=Find 'CleanerCategories'
  if($list){
   $ancestor=[Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($list)
   while($ancestor -and !$ancestor.GetCurrentPropertyValue([Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty)){$ancestor=[Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($ancestor)}
   Scroll-IfPossible $ancestor 0
   Scroll-IfPossible $list 0
   $item=$list.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem))
  }
  if(!$item){Start-Sleep -Milliseconds 100}
 } while(!$item -and $ready.Elapsed.TotalSeconds -lt 10)
 Assert 'Category row realized before selection' ($null -ne $item) (Find 'CleanerSummary').Current.Name
 $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
 Start-Sleep -Milliseconds 150
}
function ToggleItem([string]$id) {
 $list=Find 'CleanerPreview'
 foreach($percent in @(0,25,50,75,100)) {
  $item=Find $id
  if($item){$item.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle(); return}
  Scroll-IfPossible $list $percent
  Start-Sleep -Milliseconds 100
 }
 throw "Missing fixture item $id"
}
function Choose([string]$id,[string]$name) {
 Ensure-TestViewport
 $null=[CleanerNative]::SetForegroundWindow($p.MainWindowHandle)
 $combo=Find $id
 if(!$combo){throw "Missing settings control $id"}
 $expand=$combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern);$expand.Expand();Start-Sleep -Milliseconds 150
 $condition=[Windows.Automation.AndCondition]::new([Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$name),[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::IsSelectionItemPatternAvailableProperty,$true))
 $processCondition=[Windows.Automation.AndCondition]::new($condition,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$p.Id))
 $ready=[Diagnostics.Stopwatch]::StartNew();$item=$null
 do {
  # WinUI popups may be hosted outside the main HWND's UIA subtree. Keep the
  # desktop lookup strictly scoped to the test process and exact requested label.
  $item=[Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants,$processCondition)
  if(!$item){$expand.Expand();Start-Sleep -Milliseconds 100}
 }while(!$item -and $ready.Elapsed.TotalSeconds -lt 5)
 if(!$item){throw "Settings choice $id/$name unavailable after bounded popup wait"}
 $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();$expand.Collapse();Start-Sleep -Milliseconds 200
}
function Picture([string]$name) {
 $r=New-Object CleanerNative+Rect;$null=[CleanerNative]::GetWindowRect($p.MainWindowHandle,[ref]$r)
 $image=New-Object Drawing.Bitmap(($r.R-$r.L),($r.B-$r.T));$g=[Drawing.Graphics]::FromImage($image);$dc=$g.GetHdc()
 try{$ok=[CleanerNative]::PrintWindow($p.MainWindowHandle,$dc,2)}finally{$g.ReleaseHdc($dc)}
 try{if($ok){$image.Save([IO.Path]::GetFullPath("$EvidencePath/$name.png"))}}finally{$g.Dispose();$image.Dispose()}
}
function Observe {
  $p.Refresh()
  $rect=New-Object CleanerNative+Rect;$null=[CleanerNative]::GetWindowRect($p.MainWindowHandle,[ref]$rect)
  $observations.Add([pscustomobject]@{Seconds=$clock.Elapsed.TotalSeconds;Cycle=$cycle;PrivateBytes=$p.PrivateMemorySize64;WorkingSet=$p.WorkingSet64;Handles=$p.HandleCount;Threads=$p.Threads.Count;Responding=$p.Responding;WindowWidth=($rect.R-$rect.L);WindowHeight=($rect.B-$rect.T);Diagnostics=(Find 'CleanerValidationStatus').Current.Name})
  $observations | ConvertTo-Json -Depth 4 | Set-Content "$EvidencePath/observations.json" -Encoding UTF8
  if($DumpTool -and $cycle -in $SnapshotCycles) {
   # Heap dumps suspend the process but do not force an application GC. Keep this
   # profiling run separate from the unprofiled acceptance soak.
   & $DumpTool collect --process-id $p.Id --type Heap --output "$EvidencePath/heap-$cycle.dmp" | Out-File "$EvidencePath/heap-$cycle-collect.txt"
   if($LASTEXITCODE){throw 'Heap snapshot collection failed'}
  }
  if($NativeMapTool -and ($cycle -le 1 -or $cycle%10 -eq 0)){ Capture-NativeMap "cycle-$cycle" }
}
function Capture-NativeMap([string]$label) {
 $path=[IO.Path]::GetFullPath("$EvidencePath/native-$label.xml")
 $capture=Start-Process $NativeMapTool -ArgumentList '-accepteula','-p',"$($p.Id)",('"'+$path+'"') -PassThru
 try {if(!$capture.WaitForExit(60000) -or !(Test-Path $path)){throw 'Native memory capture failed'}}finally{$capture.Dispose()}
}
$p=$null;$cycle=0;$completed=$false;$clock=[Diagnostics.Stopwatch]::StartNew()
try {
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 $elevated=[CleanerNative]::IsElevated($p.Handle)
 [pscustomobject]@{Pid=$p.Id;Elevated=$elevated;StartedUtc=$p.StartTime.ToUniversalTime().ToString('o');Executable=[IO.Path]::GetFullPath($AppPath)} | ConvertTo-Json | Set-Content "$EvidencePath/process.json" -Encoding utf8
 Assert 'Target process remains unelevated' (!$elevated)
 $launch=[Diagnostics.Stopwatch]::StartNew()
 do {Start-Sleep -Milliseconds 300;$p.Refresh()} while(!$p.HasExited -and $p.MainWindowHandle -eq 0 -and $launch.Elapsed.TotalSeconds -lt 30)
 Assert 'Native validation app launched' (!$p.HasExited -and $p.MainWindowHandle -ne 0)
 Start-Sleep -Seconds 3
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 if($StartupIdleSeconds -gt 0){Start-Sleep -Seconds $StartupIdleSeconds}
 Navigate 'NavCleaner'
 Assert 'Separate synthetic build marker required' ($null -ne (Find 'CleanerValidationPrepare'))
 Observe
 do {
  $cycle++
  $null=[CleanerNative]::ShowWindow($p.MainWindowHandle,9)
  Ensure-TestViewport
  Invoke 'CleanerValidationPrepare';Assert 'Clean disabled before scan' (!(Find 'CleanerClean').Current.IsEnabled)
  Invoke 'CleanerScan';Invoke 'CleanerCancel';Wait-Idle
  Invoke 'CleanerScan';Navigate 'NavDashboard';Start-Sleep -Milliseconds 300;Navigate 'NavCleaner';Wait-Idle
  Scan;Review
  $list=Find 'CleanerCategories'
  $box=$list.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::CheckBox))
  $box.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle();Assert 'No selected category disables clean' (!(Find 'CleanerClean').Current.IsEnabled)
  $box.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
  ToggleItem 'CleanerItem-05-unselected.tmp'
  Invoke 'CleanerNext';Start-Sleep -Milliseconds 150
  ToggleItem 'CleanerItem-page-104.tmp'
  Invoke 'CleanerPrevious';Start-Sleep -Milliseconds 150
  $realize=[Diagnostics.Stopwatch]::StartNew();$item=$null
  do {
   Scroll-IfPossible (Find 'CleanerPreview') 0
   $item=Find 'CleanerItem-05-unselected.tmp'
   if(!$item){Start-Sleep -Milliseconds 100}
  }while(!$item -and $realize.Elapsed.TotalSeconds -lt 5)
  Assert 'Excluded item realized after previous page' ($null -ne $item)
  Assert 'Item exclusion persists across paging' ($item.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [Windows.Automation.ToggleState]::Off)
  Assert 'Preview bounded to 100 rows' ((Find 'CleanerValidationStatus').Current.Name -match 'PreviewRows:100;')
  Invoke 'CleanerClean';Start-Sleep -Milliseconds 200;Assert 'Confirmation exists' ($null -ne (Find 'CleanerConfirmation'))
  Invoke 'CloseButton';Start-Sleep -Milliseconds 150;Assert 'Cancel confirmation preserves session' ((Find 'CleanerClean').Current.IsEnabled)
  Invoke 'CleanerClean';Start-Sleep -Milliseconds 200
  if($cycle -eq 1){Picture 'confirmation'}
  Invoke 'PrimaryButton';Start-Sleep -Milliseconds 200;Wait-Idle
  $status=(Find 'CleanerValidationStatus').Current.Name
  Assert 'Full synthetic partial clean verified on disk' ($status -match 'Deleted:105;Skipped:4;Failed:0;Bytes:111;Root:True;Unselected:True;PageExcluded:True;Changed:True;Gone:True;Locked:True;Rejected:True') $status
  Assert 'Clean session consumed' (!(Find 'CleanerClean').Current.IsEnabled)
  Assert 'Actual result displayed' ((Find 'CleanerSummary').Current.Name -match '111') (Find 'CleanerSummary').Current.Name
  if($cycle -le 2){Picture "result-$cycle"}
  Scan;Review
  Assert 'Rescan sees only surviving old eligible fixtures' ((Find 'CleanerValidationStatus').Current.Name -match 'PreviewRows:3;')
  Assert 'Deleted fixture absent in rescan' ($null -eq (Find 'CleanerItem-00-delete.tmp'))
  Navigate 'NavSettings'
  $language=if($cycle%2 -eq 1){'ar'}else{'en'}
  $resources=if($language -eq 'ar'){$ar}else{$en}
  $theme=@('ThemeLight','ThemeDark','ThemeSystem')[$cycle%3]
  # Theme names use the language active before this switch.
  $old=if($cycle%2 -eq 1){$en}else{$ar}
  Choose 'ThemePicker' (($old | Where-Object name -eq $theme).value)
  Choose 'LanguagePicker' $(if($language -eq 'ar'){($ar | Where-Object name -eq 'Arabic').value}else{'English'})
  Navigate 'NavPerformance';Start-Sleep -Seconds 2
  Assert 'Shared monitoring remains responsive' ($null -ne (Find 'PerfCpu') -and $p.Responding)
  $null=[CleanerNative]::ShowWindow($p.MainWindowHandle,6);Start-Sleep -Milliseconds 500
  $null=[CleanerNative]::ShowWindow($p.MainWindowHandle,9);Start-Sleep -Milliseconds 300
  Navigate 'NavCleaner'
  Assert 'Cleaner localized after lifecycle' ((Find 'PageTitle').Current.Name -eq ($resources | Where-Object name -eq 'NavCleaner').value)
  Assert 'No stale cleaner tasks or row handlers after navigation' ((Find 'CleanerValidationStatus').Current.Name -match 'CleanerTasks:0;.*SelectionSubscriptions:0;PreviewRows:0;CleanerBusy:False')
  if($cycle -le 2){Picture "localized-$language"}
  # Release the automation driver's stale COM proxies. This collects PowerShell's
  # heap only, never the application heap; otherwise UIA can retain old providers.
  $item=$null;$box=$null;$list=$null
  [GC]::Collect();[GC]::WaitForPendingFinalizers()
  Start-Sleep -Seconds 2;Observe
  if($DurationSeconds -gt 0 -and $clock.Elapsed.TotalSeconds -lt $DurationSeconds){Start-Sleep -Seconds 15}
  } while($cycle -lt $MinimumCycles -or ($DurationSeconds -gt 0 -and $clock.Elapsed.TotalSeconds -lt $DurationSeconds))
  $completed=$true
  if($NativeMapTool){Capture-NativeMap 'end'}
  if($DiagnosticGcDumpTool) {
   # Explicitly separate diagnostic experiment ONLY: gcdump requests a full GC.
   # No acceptance memory sample is taken after this; never enabled in the soak.
   & $DiagnosticGcDumpTool collect --process-id $p.Id --output "$EvidencePath/diagnostic-forced-gc.gcdump" | Out-File "$EvidencePath/diagnostic-forced-gc.txt"
   if($LASTEXITCODE){throw 'Diagnostic gcdump failed'}
   if($DumpTool){
    & $DumpTool collect --process-id $p.Id --type Heap --output "$EvidencePath/heap-after-diagnostic-gc.dmp" | Out-File "$EvidencePath/heap-after-diagnostic-gc-collect.txt"
    if($LASTEXITCODE){throw 'Post-diagnostic heap dump failed'}
   }
  }
  $null=$p.CloseMainWindow();Assert 'Clean shutdown' ($p.WaitForExit(10000))
} catch {
 $_ | Out-String | Set-Content "$EvidencePath/driver-error.txt" -Encoding UTF8
 try {
  $tree=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle).FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
  $tree | ForEach-Object {[pscustomobject]@{Id=$_.Current.AutomationId;ControlType=$_.Current.ControlType.ProgrammaticName;Offscreen=$_.Current.IsOffscreen}} | ConvertTo-Json | Set-Content "$EvidencePath/failure-uia-tree.json" -Encoding utf8
  Picture 'failure-window'
 } catch { }
 throw
} finally {
 $duration=$clock.Elapsed.TotalSeconds
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(10000)};$p.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $checks | ConvertTo-Json -Depth 5 | Set-Content "$EvidencePath/checks.json" -Encoding UTF8
  [pscustomobject]@{Completed=$completed;DurationSeconds=$duration;Cycles=$cycle;Checks=$checks.Count;Failed=@($checks | Where-Object {!$_.Passed}).Count;MemoryStart=$observations[0].PrivateBytes;MemoryEnd=$observations[-1].PrivateBytes;MemoryPeak=($observations | Measure-Object PrivateBytes -Maximum).Maximum;HandlesStart=$observations[0].Handles;HandlesEnd=$observations[-1].Handles} | ConvertTo-Json | Set-Content "$EvidencePath/result.json" -Encoding UTF8
}
