param([string]$AppPath='artifacts/f1-ui/publish/F1.exe',[string]$EvidencePath='artifacts/f1-ui/screenshots-populated')
$ErrorActionPreference='Stop'
# Use the same capture/metadata helpers as the matrix; this adds read-only populated
# states in a functional controller separate from passive measurement.
$visual=[IO.File]::ReadAllText((Resolve-Path scripts/Test-F1Visual.ps1))
$entry=[regex]::Match($visual,'(?m)^try\{\r?$').Index
if($entry -le 0){throw 'Capture helper boundary missing'}
. ([scriptblock]::Create($visual.Substring(0,$entry))) -AppPath $AppPath -EvidencePath $EvidencePath
$captureWindow=(Get-Item Function:Save-WindowPicture).ScriptBlock
function Save-WindowPicture([string]$name){Invoke-ForegroundCapture $captureWindow $name}
function Wait-Enabled([string]$id,[int]$seconds=90){
 $clock=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 250;$control=Find $id}while((!$control -or !$control.Current.IsEnabled) -and $clock.Elapsed.TotalSeconds -lt $seconds)
 Check "$id completes read-only work" ($control -and $control.Current.IsEnabled) $clock.Elapsed.TotalSeconds
}
try{
 [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"theme":"Dark","language":"ar"}')
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
  $startup=[Diagnostics.Stopwatch]::StartNew()
  do{Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $startup.Elapsed.TotalSeconds -lt 30)
 Check 'Published F1 launch' (!$p.HasExited -and $p.MainWindowHandle -ne [IntPtr]::Zero) $p.Id
  $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
  Start-Sleep -Seconds 6
 $null=[ShellSmokeNative]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,10,10,1360,1000,0x14)
 foreach($language in @('ar','en')){
  Route 'Settings';Choose 'LanguagePicker' $resources[$language][$(if($language -eq 'ar'){'Arabic'}else{'English'})]
  foreach($theme in @('Dark','Light')){
   Route 'Settings';Choose 'ThemePicker' $resources[$language]["Theme$theme"]
   Route 'Cleaner'
   Invoke-Control 'CleanerScan';Wait-Enabled 'CleanerScan' 180
   $scanClock=[Diagnostics.Stopwatch]::StartNew()
   do{
    Start-Sleep -Milliseconds 200
    $categories=(Find 'CleanerCategories').FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::CheckBox))
   }while($categories.Count -eq 0 -and $scanClock.Elapsed.TotalSeconds -lt 5)
   Check 'Actual Cleaner scan categories rendered' ($categories.Count -gt 0) (Find 'CleanerSummary').Current.Name
   $category=(Find 'CleanerCategories').FindFirst([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem))
   if($category){$category.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();Start-Sleep -Milliseconds 300}
   Capture 'Cleaner' '-scanned';Scroll-Containing 'CleanerScan' 100;Capture 'Cleaner' '-review';Scroll-Containing 'CleanerScan' 0
    Route 'Uninstaller'
    Wait-InventoryButton 'UnRefresh'
    Check 'FIX7 applications available before manual override' ((Find 'UnApplications').FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem)).Count -gt 0) $language
   if($language -eq 'ar' -and $theme -eq 'Dark'){Invoke-Control 'UnRefresh';Wait-Enabled 'UnRefresh'}
   $rows=(Find 'UnApplications').FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem))
   Check 'Actual program inventory populated' ($rows.Count -gt 0) $rows.Count
   $rows[0].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
   Capture 'Uninstaller' '-loaded';Scroll-Containing 'UnSearch' 100;Capture 'Uninstaller' '-selection';Scroll-Containing 'UnSearch' 0
    Route 'Startup'
    Wait-InventoryButton 'StartRefresh'
    Check 'FIX7 startup available before manual override' ((Find 'StartItems').FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem)).Count -gt 0) $language
   if($language -eq 'ar' -and $theme -eq 'Dark'){Invoke-Control 'StartRefresh';Wait-Enabled 'StartRefresh'}
   $rows=(Find 'StartItems').FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem))
   Check 'Actual Startup inventory populated' ($rows.Count -gt 0) $rows.Count
   $rows[0].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
   Capture 'Startup' '-loaded';Scroll-Containing 'StartSearch' 100;Capture 'Startup' '-selection';Scroll-Containing 'StartSearch' 0
   Route 'Performance';Start-Sleep -Seconds 12;Capture 'Performance' '-history'
   Route 'MemoryTools';Scroll-Containing 'MemPhysical' 0;Start-Sleep -Seconds 12;Capture 'MemoryTools' '-history';Scroll-Containing 'MemPhysical' 100;Capture 'MemoryTools' '-consumers';Scroll-Containing 'MemPhysical' 0
  }
 }
 $null=$p.CloseMainWindow();Check 'Populated capture closes normally' ($p.WaitForExit(15000)) $null
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(15000)};$p.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $shots|ConvertTo-Json -Depth 5|Set-Content "$EvidencePath/metadata.json" -Encoding utf8
 $results|ConvertTo-Json -Depth 5|Set-Content "$EvidencePath/checks.json" -Encoding utf8
}
