param([string]$AppPath='artifacts/phase8/app/SystemCore.App.exe',[string]$EvidencePath='artifacts/phase8/completion/ui',[string]$ExpectedVersion='0.8.0-dev')
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
. ([scriptblock]::Create($source.Substring(0,$source.IndexOf('$p=Start-Process')))) -AppPath $AppPath -EvidencePath $EvidencePath
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
$saved=if(Test-Path $pref){[IO.File]::ReadAllBytes($pref)}else{$null}
if($null -ne $saved){[IO.File]::WriteAllBytes([IO.Path]::GetFullPath("$EvidencePath/original-preferences.bin"),$saved)}
$calibration=@{};$p=$null
$resources=@{}
foreach($language in @('en','ar')){
 $file=if($language -eq 'ar'){'Shell.ar.resx'}else{'Shell.resx'}
 $table=@{};([xml][IO.File]::ReadAllText((Resolve-Path "src/SystemCore.App/Resources/$file"))).root.data|ForEach-Object{$table[$_.name]=$_.value}
 $resources[$language]=$table
}
$pages=@('Dashboard','Cleaner','Uninstaller','Performance','SystemInfo','MemoryTools','Startup','Tools','Settings','About')
function Start-App {
 Set-Variable -Scope 1 -Name p -Value (Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr-$([Guid]::NewGuid().ToString('N')).txt")
 $clock=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $clock.Elapsed.TotalSeconds -lt 30)
 Check 'Product app launched' (!$p.HasExited -and $p.MainWindowHandle -ne [IntPtr]::Zero) $p.Id
 Set-Variable -Scope 1 -Name window -Value ([Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle))
 $clock.Restart()
 do{Start-Sleep -Milliseconds 100;$title=Find 'PageTitle'}while((!$title -or [string]::IsNullOrWhiteSpace($title.Current.Name)) -and $clock.Elapsed.TotalSeconds -lt 15)
 Check 'Initial XAML page is ready' ($title -and ![string]::IsNullOrWhiteSpace($title.Current.Name)) $title.Current.Name
 $elevated=& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Get-ProcessElevation.ps1)))) -ProcessId $p.Id
 Check 'Product app is non-elevated' (!$elevated) $elevated
}
function Invoke-Control([string]$id){if($id -in @('UnRefresh','StartRefresh','ToolsRefresh','InfoRefresh')){Wait-InventoryButton $id};(Find $id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();Start-Sleep -Milliseconds 250}
function Wait-Enabled([string]$id,[int]$seconds=30){
 $clock=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 200;$control=Find $id}while((!$control -or !$control.Current.IsEnabled) -and $clock.Elapsed.TotalSeconds -lt $seconds)
 Check "$id returns enabled" ($control -and $control.Current.IsEnabled) $clock.Elapsed.TotalSeconds
}
function Reveal([string]$id){
 $node=Find $id
 $pattern=$null
 if($node.TryGetCurrentPattern([Windows.Automation.ScrollItemPattern]::Pattern,[ref]$pattern)){$pattern.ScrollIntoView();Start-Sleep -Milliseconds 250}
}
function Scroll-Containing([string]$id,[double]$percent){
 $node=Find $id
 while($node -and !$node.GetCurrentPropertyValue([Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty)){$node=[Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($node)}
 if($node){$node.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1,$percent);Start-Sleep -Milliseconds 250}
}
function Calibrate([string]$language,[string]$id){
 $node=Find $id;$r=New-Object ShellSmokeNative+Rect;$null=[ShellSmokeNative]::GetWindowRect($p.MainWindowHandle,[ref]$r)
 $b=$node.Current.BoundingRectangle
 if($node.Current.IsOffscreen -or $b.Width -le 0 -or $b.Height -le 0){throw "Cannot calibrate $id"}
 # A navigation row's reported rect is not reliably clickable at its centre. An item that owns
 # child rows spans the item and its children, and the branded footer row is padded by its style
 # margins, so in both cases the centre of the rect falls outside the row's hit area. Clicking
 # there selects nothing and the page never navigates, which reads as an unverified route rather
 # than as a bad coordinate. The upper part of a navigation row is the row itself. Anything else
 # calibrated here is a button or a picker whose whole rect is the control, so its centre stands.
 $offset=[int]($b.Height/2)
 if($node.Current.ControlType -eq [Windows.Automation.ControlType]::ListItem){$offset=[int]($b.Height/4)}
 $calibration[$language][$id]=@{X=[int]($b.X+$b.Width/2-$r.Left);Y=[int]($b.Y+$offset-$r.Top)}
}
try{
 [IO.Directory]::CreateDirectory((Split-Path $pref))|Out-Null
 [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"theme":"Light","language":"en"}')
 Start-App
  Check 'First launch opens Dashboard' ((Find 'PageTitle').Current.Name -eq $resources.en.F1Welcome -and (Find 'NavDashboard').GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) $null
 Check 'No automatic Cleaner scan' ((Find 'JunkStatus').Current.Name -eq $resources.en.NotScanned) (Find 'JunkStatus').Current.Name
 foreach($language in @('en','ar')){
  $calibration[$language]=@{}
  Navigate 'NavSettings';Choose 'LanguagePicker' $resources[$language][$(if($language -eq 'ar'){'Arabic'}else{'English'})]
  $storedTheme=(Get-Content $pref -Raw|ConvertFrom-Json).theme
  $selectedTheme=(Find 'ThemePicker').GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()[0].Current.Name
  Check "Selected theme item localizes immediately to $language" ($selectedTheme -eq $resources[$language]["Theme$storedTheme"]) $selectedTheme
  Save-WindowPicture "$language-language-switch-selected-theme"
  foreach($theme in @('Light','Dark','System')){
   Navigate 'NavSettings';Scroll-Containing 'LanguagePicker' 0;Choose 'ThemePicker' $resources[$language]["Theme$theme"]
   foreach($page in $pages){
    Navigate "Nav$page"
    if($language -eq 'en' -and $theme -eq 'Light'){
     if($page -eq 'Uninstaller'){Invoke-Control 'UnRefresh';Wait-Enabled 'UnRefresh' 60}
     if($page -eq 'Startup'){Invoke-Control 'StartRefresh';Wait-Enabled 'StartRefresh' 60}
     if($page -eq 'SystemInfo'){Wait-Info 'Device overview'}
    }
    if($page -eq 'Tools'){Wait-Enabled 'ToolsRefresh'}
    $title=(Find 'PageTitle').Current.Name
     $titleKey=if($page -eq 'Dashboard'){'F1Welcome'}else{"Nav$page"}
     Check "$language $theme $page localized title" ($title -eq $resources[$language][$titleKey]) $title
    $nav=Find "Nav$page"
    Check "$language $theme $page active navigation" ($nav.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) $null
    if($theme -eq 'Light'){
     Calibrate $language "Nav$page"
     foreach($id in $(switch($page){'Cleaner'{'CleanerScan';'CleanerCancel'} 'Uninstaller'{'UnRefresh'} 'Startup'{'StartRefresh'} 'Settings'{'LanguagePicker';'ThemePicker'} 'MemoryTools'{'MemAnalyze'} 'Tools'{'ToolsRefresh'}})){Calibrate $language $id}
    }
    Save-WindowPicture "$language-$theme-$page"
   }
  }
 }
 $calibration|ConvertTo-Json -Depth 6|Set-Content "$EvidencePath/native-coordinates.json" -Encoding utf8
 Navigate 'NavSettings';Scroll-Containing 'LanguagePicker' 0;Choose 'LanguagePicker' $resources.en.English;Choose 'ThemePicker' $resources.en.ThemeLight
 Check 'Settings saved status truthful' ((Find 'SettingsSaveStatus').Current.Name -eq $resources.en.SettingsSaved) (Find 'SettingsSaveStatus').Current.Name
 (Find 'LanguagePicker').SetFocus();[Windows.Forms.SendKeys]::SendWait('{TAB}');Start-Sleep -Milliseconds 250
 Check 'Tab reaches theme picker' ([Windows.Automation.AutomationElement]::FocusedElement.Current.AutomationId -eq 'ThemePicker') ([Windows.Automation.AutomationElement]::FocusedElement.Current.AutomationId)
 [Windows.Forms.SendKeys]::SendWait('+{TAB}');Start-Sleep -Milliseconds 250
 Check 'Shift Tab returns to language picker' ([Windows.Automation.AutomationElement]::FocusedElement.Current.AutomationId -eq 'LanguagePicker') $null
 Navigate 'NavTools';Wait-Enabled 'ToolsRefresh'
 $toolIds=@('TaskManager','DeviceManager','DiskManagement','SystemInformation','Services','EventViewer','WindowsUpdate','NetworkConnections','InstalledApps','StorageSettings','SystemRestore','WindowsSecurity','PowerOptions','OptionalFeatures')
 foreach($id in $toolIds){
  $button=Find "ToolOpen$id";$state=(Find "ToolAvailability$id").Current.Name
  Check "Tool $id availability controls Open" ($button.Current.IsEnabled -eq ($state.StartsWith($resources.en.ToolAvailabilityAvailable))) $state
  Check "Tool $id accessible action name" ($button.Current.Name -eq ('Open '+$resources.en["Tool$id"])) $button.Current.Name
 }
 $priorUtilities=@(Get-Process msinfo32 -ErrorAction SilentlyContinue|ForEach-Object Id)
 Invoke-Control 'ToolOpenSystemInformation';Wait-Enabled 'ToolsRefresh'
 Check 'Tool button dispatches real Windows utility' ((Find 'ToolsStatus').Current.Name -eq ($resources.en.ToolResult -f $resources.en.ToolSystemInformation,$resources.en.ToolResultLaunched)) (Find 'ToolsStatus').Current.Name
 Start-Sleep -Seconds 2
 Get-Process msinfo32 -ErrorAction SilentlyContinue|Where-Object{$_.Id -notin $priorUtilities}|ForEach-Object{$null=$_.CloseMainWindow();Check 'UI-launched utility closes normally' ($_.WaitForExit(15000)) $null;$_.Dispose()}
 Navigate 'NavTools'
 (Find 'ToolsRefresh').SetFocus();[Windows.Forms.SendKeys]::SendWait(' ');Wait-Enabled 'ToolsRefresh'
 Check 'Space refreshes tools safely' ((Find 'ToolsStatus').Current.Name -eq $resources.en.ToolsReady) $null
 Navigate 'NavUninstaller';Invoke-Control 'UnRefresh';Wait-Enabled 'UnRefresh' 60
 (Find 'UnSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('SystemCore-no-match-6ed9f71d');Start-Sleep -Milliseconds 300
 Check 'Uninstaller empty filter state' ((Find 'UnStatus').Current.Name.Contains($resources.en.ProductNoMatches)) (Find 'UnStatus').Current.Name
 (Find 'UnSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('')
 Navigate 'NavStartup';Invoke-Control 'StartRefresh';Wait-Enabled 'StartRefresh' 60
 (Find 'StartSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('SystemCore-no-match-6ed9f71d');Start-Sleep -Milliseconds 300
 Check 'Startup empty filter state' ((Find 'StartStatus').Current.Name.Contains($resources.en.ProductNoMatches)) (Find 'StartStatus').Current.Name
 (Find 'StartSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('')
 Navigate 'NavAbout';Invoke-Control 'AboutCopyVersion'
  $expectedProduct=[Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path $AppPath)).ProductName
  Check 'About copies assembly version' ([Windows.Forms.Clipboard]::GetText() -eq "$expectedProduct $ExpectedVersion X64") ([Windows.Forms.Clipboard]::GetText())
 Check 'About reports x64' ((Find 'AboutArchitecture').Current.Name -match 'X64') $null
 (Find 'AboutLicenses').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand();Start-Sleep -Seconds 2
 Check 'Original license texts displayed' ((Find 'AboutNotices').Current.Name.Contains('Permission is hereby granted')) $null
 Save-WindowPicture 'about-licenses'
 (Find 'AboutLicenses').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
 Scroll-Containing 'AboutCopyVersion' 100
 Invoke-Control 'DiagnosticsClear';Start-Sleep -Milliseconds 300
 Check 'Clear logs confirmation visible' ($null -ne (Find 'DiagnosticsClearConfirmation')) $null
 [Windows.Forms.SendKeys]::SendWait('{ESC}');Start-Sleep -Milliseconds 300
 Check 'Escape cancels clear logs' ($null -eq (Find 'DiagnosticsClearConfirmation')) $null
 Invoke-Control 'DiagnosticsClear';Start-Sleep -Milliseconds 300
 [Windows.Forms.SendKeys]::SendWait('{ENTER}');Start-Sleep -Milliseconds 300
 Check 'Default Enter cancels destructive confirmation' ($null -eq (Find 'DiagnosticsClearConfirmation')) $null
 $exportPath=Join-Path $env:LOCALAPPDATA 'SystemCore/Exports';$before=@(Get-ChildItem $exportPath -Filter '*.zip' -ErrorAction SilentlyContinue|ForEach-Object Name)
 Invoke-Control 'DiagnosticsExport';Wait-Enabled 'DiagnosticsExport'
 Check 'Manual diagnostic export verified' ((Find 'DiagnosticsStatus').Current.Name -eq $resources.en.DiagnosticsExported) (Find 'DiagnosticsStatus').Current.Name
 $created=@(Get-ChildItem $exportPath -Filter '*.zip'|Where-Object{$_.Name -notin $before})
 Check 'Exactly one user-requested export' ($created.Count -eq 1) $created.Count
 Move-Item $created[0].FullName "$EvidencePath/diagnostic-export.zip"
 Save-WindowPicture 'diagnostics-export'
 foreach($page in @('Tools','Settings','About','Uninstaller','Startup')){
  Navigate "Nav$page"
  if($page -eq 'About'){Scroll-Containing 'AboutCopyVersion' 0}
  if($page -eq 'Settings'){Scroll-Containing 'LanguagePicker' 0}
  $null=[ShellSmokeNative]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,40,40,900,600,0x14);Start-Sleep -Milliseconds 400
  Check "Minimum window $page title visible" (!(Find 'PageTitle').Current.IsOffscreen) $null
  Save-WindowPicture "minimum-$page"
 }
 Navigate 'NavSettings';Scroll-Containing 'LanguagePicker' 0;Choose 'LanguagePicker' $resources.ar.Arabic;Choose 'ThemePicker' $resources.ar.ThemeDark
 $null=$p.CloseMainWindow();Check 'Product app closes normally' ($p.WaitForExit(15000)) $null;$p.Dispose();$p=$null
 $stored=Get-Content $pref -Raw|ConvertFrom-Json
 Check 'Theme/language persisted' ($stored.theme -eq 'Dark' -and $stored.language -eq 'ar' -and $stored.schemaVersion -eq 1) $stored
 Start-App
 $restartWait=[Diagnostics.Stopwatch]::StartNew()
  do{Start-Sleep -Milliseconds 100;$restartTitle=(Find 'PageTitle').Current.Name}while($restartTitle -ne $resources.ar.F1Welcome -and $restartWait.Elapsed.TotalSeconds -lt 15)
 Save-WindowPicture 'restart-dashboard'
  Check 'Restart opens localized Dashboard' ($restartTitle -eq $resources.ar.F1Welcome -and (Find 'NavDashboard').GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) $restartTitle
 Navigate 'NavSettings'
 Check 'Restart restores language selection' ((Find 'LanguagePicker').GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()[0].Current.Name -eq $resources.ar.Arabic) $null
 Check 'Restart restores theme selection' ((Find 'ThemePicker').GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()[0].Current.Name -eq $resources.ar.ThemeDark) $null
 Save-WindowPicture 'restart-ar-dark'
 $null=$p.CloseMainWindow();Check 'Restart closes normally' ($p.WaitForExit(15000)) $null
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(15000)};$p.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $results|ConvertTo-Json -Depth 6|Set-Content "$EvidencePath/checks.json" -Encoding utf8
}
