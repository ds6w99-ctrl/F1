param([string]$AppPath='artifacts/phase6/app/SystemCore.App.exe',[string]$EvidencePath='artifacts/phase6/6a-ui')
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
. ([scriptblock]::Create($source.Substring(0,$source.IndexOf('$p=Start-Process')))) -AppPath $AppPath -EvidencePath $EvidencePath
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
$saved=if(Test-Path $pref){[IO.File]::ReadAllBytes($pref)}else{$null}
[IO.File]::WriteAllText($pref,'{"theme":"Light","language":"en"}')
$p=$null
try {
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 $clock=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $clock.Elapsed.TotalSeconds -lt 30)
 Check 'Startup app launched' (!$p.HasExited -and $p.MainWindowHandle -ne [IntPtr]::Zero) $null
 $elevated=& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Get-ProcessElevation.ps1)))) -ProcessId $p.Id
 Check 'App token is unelevated' (!$elevated) $elevated
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 Navigate 'NavStartup'
 Check 'Enable disabled without selection' (!(Find 'StartEnable').Current.IsEnabled) $null
 Check 'Disable disabled without selection' (!(Find 'StartDisable').Current.IsEnabled) $null
 $watch=[Diagnostics.Stopwatch]::StartNew()
 (Find 'StartRefresh').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
 do{Start-Sleep -Milliseconds 200;$refresh=Find 'StartRefresh'}while(!$refresh.Current.IsEnabled -and $watch.Elapsed.TotalSeconds -lt 40)
 Check 'Startup refresh returned' ($refresh.Current.IsEnabled) $watch.Elapsed.TotalSeconds
 Check 'Startup source status reported' ((Find 'StartStatus').Current.Name -match 'Inventory loaded|Some startup sources') $null
 $rows=(Find 'StartItems').FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem))
 $first=$rows|Where-Object {!$_.Current.IsOffscreen}|Select-Object -First 1
 Check 'Real startup item displayed' ($null -ne $first) $null
 $first.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();Start-Sleep -Milliseconds 200
 Check 'Startup details report unmeasured impact' ((Find 'StartDetails').Current.Name.Contains('Not measured')) $null
 if((Find 'StartDisable').Current.IsEnabled){
  (Find 'StartDisable').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();Start-Sleep -Milliseconds 300
  Check 'Startup change requires confirmation' ($null -ne (Find 'StartConfirmation')) $null
  (Find 'CloseButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();Start-Sleep -Milliseconds 200
  Check 'Real item confirmation cancelled without dispatch' ($null -eq (Find 'StartConfirmation')) $null
 }else{Check 'Selected protected item remains read-only' (!(Find 'StartEnable').Current.IsEnabled) $null}
 foreach($name in @('Registry','Startup folders','Scheduled tasks','Protected','All')){Choose 'StartFilter' $name}
 foreach($name in @('Publisher','Status','Source','Scope','Name')){Choose 'StartSort' $name}
 (Find 'StartSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('SystemCore-DefinitelyAbsent-Startup')
 Start-Sleep -Milliseconds 200
 Check 'Empty filter clears old actionable selection' (!(Find 'StartDisable').Current.IsEnabled -and !(Find 'StartEnable').Current.IsEnabled) $null
 Save-WindowPicture 'startup-en-empty-filter'
 Navigate 'NavDashboard';Navigate 'NavStartup'
 Check 'Navigation preserves live page' ($null -ne (Find 'StartSearch')) $null
 Navigate 'NavSettings'
 $ar=([xml][IO.File]::ReadAllText((Resolve-Path src/SystemCore.App/Resources/Shell.ar.resx))).root.data
 Choose 'LanguagePicker' (($ar|Where-Object name -eq 'Arabic').value)
 Navigate 'NavStartup'
 Check 'Arabic Startup title' ((Find 'PageTitle').Current.Name -eq ($ar|Where-Object name -eq 'NavStartup').value) $null
 Check 'Arabic Startup refresh' ((Find 'StartRefresh').Current.Name -eq ($ar|Where-Object name -eq 'StartRefresh').value) $null
 Save-WindowPicture 'startup-ar-empty-filter'
 $null=$p.CloseMainWindow();Check 'Startup clean shutdown' ($p.WaitForExit(10000)) $null
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(10000)};$p.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $results|ConvertTo-Json -Depth 5|Set-Content "$EvidencePath/checks.json" -Encoding utf8
}
