param([string]$EvidencePath='artifacts/f1-uninstaller/ui-03',[string]$AppPath='artifacts/current/F1/F1.exe')
$ErrorActionPreference='Stop'
$visual=[IO.File]::ReadAllText((Resolve-Path scripts/Test-F1Visual.ps1))
$entry=[regex]::Match($visual,'(?m)^try\{\r?$').Index
. ([scriptblock]::Create($visual.Substring(0,$entry))) -AppPath $AppPath -EvidencePath $EvidencePath
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class UninstallerFocus {
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,IntPtr pid);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from,uint to,bool attach);
 [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
 public static void Focus(IntPtr h){var from=GetCurrentThreadId();var to=GetWindowThreadProcessId(GetForegroundWindow(),IntPtr.Zero);var attached=false;try{if(to!=0 && to!=from)attached=AttachThreadInput(from,to,true);SetForegroundWindow(h);}finally{if(attached)AttachThreadInput(from,to,false);}}
}
'@
$capture=(Get-Item function:Save-WindowPicture).ScriptBlock
function Save-WindowPicture([string]$name){
 $focus=[Diagnostics.Stopwatch]::StartNew()
 do{[UninstallerFocus]::Focus($p.MainWindowHandle);Start-Sleep -Milliseconds 200}while([ShellSmokeNative]::GetForegroundWindow() -ne $p.MainWindowHandle -and $focus.Elapsed.TotalSeconds -lt 5)
 & $capture $name
}
function Rows {
 (Find 'UnApplications').FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem))
}
function Wait-Refresh {
 $clock=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 250}while(!(Find 'UnRefresh').Current.IsEnabled -and $clock.Elapsed.TotalSeconds -lt 120)
 Check 'Read-only refresh completes' (Find 'UnRefresh').Current.IsEnabled $clock.Elapsed.TotalSeconds
 Start-Sleep -Seconds 4
}
try{
 [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"theme":"Dark","language":"en"}')
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 $clock=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $clock.Elapsed.TotalSeconds -lt 30)
 Check 'Uninstaller publication launches' (!$p.HasExited -and $p.MainWindowHandle -ne [IntPtr]::Zero) $p.Id
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle);Start-Sleep -Seconds 5
 $null=[ShellSmokeNative]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,10,10,1360,1000,0x14)
 $language='en';$theme='Dark';Route 'Uninstaller';Invoke-Control 'UnRefresh';Wait-Refresh
 $list=Find 'UnApplications';$bounds=$list.Current.BoundingRectangle
 $visible=@((Rows)|Where-Object{!$_.Current.IsOffscreen -and $_.Current.BoundingRectangle.Height -ge 48 -and $_.Current.BoundingRectangle.Top -ge $bounds.Top -and $_.Current.BoundingRectangle.Bottom -le $bounds.Bottom})
 $visible|ForEach-Object{[pscustomobject]@{Name=$_.Current.Name;Bounds=$_.Current.BoundingRectangle.ToString()}}|ConvertTo-Json|Set-Content "$EvidencePath/visible-rows.json" -Encoding utf8
 Capture 'Uninstaller' '-loaded'
 Check 'At least twelve fully visible app rows at reference size' ($visible.Count -ge 12) @{Rows=$visible.Count;ListHeight=$bounds.Height;RowHeight=$visible[0].Current.BoundingRectangle.Height;Counts=(Find 'UnStatus').Current.HelpText}
 Check 'Virtualized item source supports scroll' $list.GetCurrentPropertyValue([Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty) $null
 $first=$visible[0].Current.Name
 # FIX 5: the unified list may lead with a launcher-managed game, which correctly exposes
 # no native uninstall control at all. Select the first visible row that really has native
 # removal rights, and separately assert the managed game exposes no destructive action.
 $nativeRow=$null;$managedRow=$null
 foreach($row in $visible){
  $row.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();Start-Sleep -Milliseconds 200
  $uninstall=Find 'UnUninstall'
  if($null -ne $uninstall -and $uninstall.Current.IsEnabled){if($null -eq $nativeRow){$nativeRow=$row}}
  elseif($null -eq $managedRow -and $null -ne (Find 'UnDetails') -and (Find 'UnDetails').Current.Name -like 'Managed by *'){$managedRow=$row}
 }
 Check 'A visible native app has an enabled native uninstall action' ($null -ne $nativeRow) $first
 if($null -ne $managedRow){
  $managedRow.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();Start-Sleep -Milliseconds 300
  $managedName=$managedRow.Current.Name
  Check 'Launcher-managed game exposes no native uninstall action' ($null -eq (Find 'UnUninstall') -or !(Find 'UnUninstall').Current.IsEnabled) $managedName
  Check 'Launcher-managed game exposes no leftover scan action' (!(Find 'UnScanLeftovers').Current.IsEnabled) $managedName
  Check 'Launcher-managed game still offers a separate launcher control' ($null -ne (Find 'UnOpenLauncher')) $managedName
 }
 if($null -eq $nativeRow){throw 'No visible native removable app row to continue the uninstall confirmation checks'}
 $nativeRow.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();Start-Sleep -Milliseconds 300
 Check 'Selected native app has enabled native uninstall action' (Find 'UnUninstall').Current.IsEnabled $nativeRow.Current.Name
 Capture 'Uninstaller' '-selected'
 Invoke-Control 'UnUninstall';Check 'Confirmation retained' ($null -ne (Find 'UnConfirmation')) $null
 [Windows.Forms.SendKeys]::SendWait('{ESC}');Start-Sleep -Milliseconds 600
 Check 'Confirmation cancelled without uninstall' ($null -eq (Find 'UnConfirmation')) $null
 (Find 'UnSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('F1-no-match-6A41');Start-Sleep -Milliseconds 500
 Check 'Search only removable rows, empty match' ((Rows).Count -eq 0 -and !(Find 'UnNext').Current.IsEnabled -and !(Find 'UnUninstall').Current.IsEnabled) (Find 'UnStatus').Current.Name
 (Find 'UnSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('');Start-Sleep -Milliseconds 500
 if((Find 'UnNext').Current.IsEnabled){Invoke-Control 'UnNext';Check 'Page changes without stale first row' ((Rows)[0].Current.Name -ne $first) (Find 'UnCount').Current.Name;Invoke-Control 'UnPrevious'}
 $scroll=(Find 'UnApplications').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
 $scroll.SetScrollPercent(-1,100);Start-Sleep -Milliseconds 500
 Check 'Application list scrolls independently of details' ($scroll.Current.VerticalScrollPercent -gt 0 -and !(Find 'UnUninstall').Current.IsOffscreen) $scroll.Current.VerticalScrollPercent
 $scroll.SetScrollPercent(-1,0)
 foreach($filterName in @('MSI','Microsoft Store / MSIX','All applications')){Choose 'UnFilter' $filterName;Check 'Filter clears stale selection' (!(Find 'UnUninstall').Current.IsEnabled) $filterName}
 Choose 'UnSort' 'Publisher';Choose 'UnSort' 'Name'
 Invoke-Control 'UnRefresh';Wait-Refresh;Check 'Refresh clears stale selection' (!(Find 'UnUninstall').Current.IsEnabled) (Find 'UnCount').Current.Name
 foreach($language in @('ar','en')){
  Route 'Settings';Choose 'LanguagePicker' $resources[$language][$(if($language -eq 'ar'){'Arabic'}else{'English'})]
  foreach($theme in @('Dark','Light')){
   Route 'Settings';Choose 'ThemePicker' $resources[$language]["Theme$theme"]
   Route 'Uninstaller';Start-Sleep -Seconds 2;Capture 'Uninstaller'
   Check 'Localized Uninstaller route' ((Find 'PageTitle').Current.Name -eq $resources[$language].NavUninstaller) "$language-$theme"
  }
 }
 $null=$p.CloseMainWindow();Check 'Uninstaller UI normal close' ($p.WaitForExit(15000)) $null
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(15000)};$p.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $shots|ConvertTo-Json -Depth 6|Set-Content "$EvidencePath/metadata.json" -Encoding utf8
 $results|ConvertTo-Json -Depth 6|Set-Content "$EvidencePath/checks.json" -Encoding utf8
}
