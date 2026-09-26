param([string]$AppPath='artifacts/phase5/app/SystemCore.App.exe',[string]$EvidencePath='artifacts/phase5/5a-ui')
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
. ([scriptblock]::Create($source.Substring(0,$source.IndexOf('$p=Start-Process')))) -AppPath $AppPath -EvidencePath $EvidencePath
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
$saved=if(Test-Path $pref){[IO.File]::ReadAllBytes($pref)}else{$null}
[IO.File]::WriteAllText($pref,'{"theme":"Light","language":"en"}')
$name='SystemCorePhase5Test-'+[guid]::NewGuid().ToString('N');$nonce=[guid]::NewGuid().ToString('N')
$root='Software\Microsoft\Windows\CurrentVersion\Uninstall'
$registry=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Registry64)
$fixtureRoot=Join-Path $env:LOCALAPPDATA $name
$siblingRoot=$fixtureRoot+'-shared'
$p=$null
function Invoke-Button([string]$id){(Find $id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();Start-Sleep -Milliseconds 250}
function Wait-Inventory {
 param([switch]$ObserveAutomaticScan)
  $watch=[Diagnostics.Stopwatch]::StartNew()
  $scanObservedAt=$null
  do {
   Start-Sleep -Milliseconds 100;$control=Find 'UnRefresh'
   if($ObserveAutomaticScan -and $null -eq $scanObservedAt -and (Find 'UnStatus').Current.Name -eq 'Scanning verified application leftovers…'){$scanObservedAt=$watch.Elapsed.TotalMilliseconds}
  }while((!$control -or !$control.Current.IsEnabled) -and $watch.Elapsed.TotalSeconds -lt 30)
  if($ObserveAutomaticScan){
   [pscustomobject]@{Scope='Synthetic native uninstall; real-device inventory read-only';WaitAfterConfirmationMilliseconds=$watch.Elapsed.TotalMilliseconds;ObservedScanningLowerBoundMilliseconds=$(if($null -ne $scanObservedAt){$watch.Elapsed.TotalMilliseconds-$scanObservedAt}else{$null});SampleIntervalMilliseconds=100;Note='Polling lower bound, not exact scan duration; confirmation helper already waited 250 ms'}|ConvertTo-Json|Set-Content "$EvidencePath/automatic-scan-timing.json" -Encoding utf8
  }
 Check 'Inventory returns control' ($control -and $control.Current.IsEnabled) $watch.Elapsed.TotalSeconds
}
try {
 $fixture=(Resolve-Path tests/SystemCore.UninstallerTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.UninstallerTests.exe).Path
 [IO.Directory]::CreateDirectory($fixtureRoot)|Out-Null
 [IO.Directory]::CreateDirectory($siblingRoot)|Out-Null
 [IO.File]::WriteAllText((Join-Path $fixtureRoot 'owned.txt'),'leftover')
 [IO.File]::WriteAllText((Join-Path $siblingRoot 'retain.txt'),'retain')
 $ownership=$registry.CreateSubKey("Software\$name")
 try {$ownership.SetValue('Publisher','SystemCore synthetic tests');$ownership.SetValue('InstallLocation',$fixtureRoot);$ownership.SetValue('DataPath',$fixtureRoot)}finally{$ownership.Dispose()}
 $key=$registry.CreateSubKey("$root\$name")
 try{$key.SetValue('DisplayName',$name);$key.SetValue('Publisher','SystemCore synthetic tests');$key.SetValue('InstallLocation',$fixtureRoot);$key.SetValue('SystemCoreTestNonce',$nonce);$key.SetValue('UninstallString',('"'+$fixture+'" --synthetic-uninstall '+$name+' '+$nonce))}finally{$key.Dispose()}
 $env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 $launch=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $launch.Elapsed.TotalSeconds -lt 60)
 Check 'App launched' (!$p.HasExited -and $p.MainWindowHandle -ne 0) $p.Id
 $elevated=& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Get-ProcessElevation.ps1)))) -ProcessId $p.Id
 Check 'Production app token is not elevated' (!$elevated) $elevated
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 Start-Sleep -Seconds 3
 Navigate 'NavUninstaller'
 Check 'Uninstall disabled before selection' (!(Find 'UnUninstall').Current.IsEnabled) $null
 Invoke-Button 'UnRefresh';Wait-Inventory
 (Find 'UnSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($name)
 Start-Sleep -Milliseconds 400
 $rows=(Find 'UnApplications').FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem))
 $fixtureRow=$rows|Where-Object {$_.Current.Name -eq $name}|Select-Object -First 1
 Check 'Synthetic app appears in inventory' ($null -ne $fixtureRow) $null
 $fixtureRow.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();Start-Sleep -Milliseconds 250
 Check 'Selected synthetic app details' ((Find 'UnDetails').Current.Name.Contains($name)) $null
 Invoke-Button 'UnUninstall'
 Check 'Explicit uninstall confirmation' ($null -ne (Find 'UnConfirmation')) $null
 Invoke-Button 'CloseButton'
 $retained=$registry.OpenSubKey("$root\$name");Check 'Cancel preserves synthetic registration' ($null -ne $retained) $null;if($retained){$retained.Dispose()}
 Save-WindowPicture 'inventory-en'
 Invoke-Button 'UnUninstall'
 # Only the nonce-protected, GUID-named synthetic registration selected above.
  Invoke-Button 'PrimaryButton';Wait-Inventory -ObserveAutomaticScan
  # FIX 6: the verified-removal operation now includes its read-only scan.
  Check 'Verified removal automatically reaches leftover results' ((Find 'UnStatus').Current.Name -eq 'Leftovers ready.') (Find 'UnStatus').Current.Name
 $remaining=$registry.OpenSubKey("$root\$name");Check 'Synthetic registration removed' ($null -eq $remaining) $null;if($remaining){$remaining.Dispose()}
 Save-WindowPicture 'synthetic-removed'
 Check 'Leftover scan offered after verified removal' ((Find 'UnScanLeftovers').Current.IsEnabled) $null
 Check 'No automatic leftover deletion' ([IO.File]::Exists((Join-Path $fixtureRoot 'owned.txt'))) $null
  Check 'Automatic leftover results shown without manual scan' ((Find 'UnLeftoverSummary').Current.Name -like "Leftovers found*") (Find 'UnLeftoverSummary').Current.Name
 $boxes=(Find 'UnApplications').FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::CheckBox))
  $choices=@($boxes|Where-Object {$_.Current.Name.Contains($name)})
  Check 'Synthetic file directory and registry leftovers found' ($choices.Count -eq 3) $choices.Count
  # Removed is assigned and immediately advanced to ScanningLeftovers without a
  # render/yield, so polling that transient status cannot prove the identity gate.
  # Assert its durable equivalent separately: the exact hive/view/key identity is
  # absent AND receipt-backed results belong to that same GUID-named fixture.
  $identityKey=$registry.OpenSubKey("$root\$name")
  $identityAbsent=$null -eq $identityKey
  if($identityKey){$identityKey.Dispose()}
  $identityLocations=@($choices|ForEach-Object {$_.Current.Name})
  Check 'Removal verified by identity' ($identityAbsent -and $identityLocations -contains "CurrentUser/Registry64\Software\$name" -and $identityLocations -contains $fixtureRoot -and $identityLocations -contains (Join-Path $fixtureRoot 'owned.txt')) @{Hive='CurrentUser';View='Registry64';Key=$name;RegistrationAbsent=$identityAbsent;ReceiptBackedLocations=$identityLocations}
  foreach($choice in $choices){Check 'Leftover not selected automatically' ($choice.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [Windows.Automation.ToggleState]::Off) $null}
  # DataPath ownership is not permission to delete personal data. FIX 6 classifies
  # this fixture's directory/file Review; only its identity-only registry is Safe.
  $safeChoices=@($choices|Where-Object {$_.Current.IsEnabled})
  $reviewChoices=@($choices|Where-Object {!$_.Current.IsEnabled})
  Check 'DataPath file and directory are unselectable Review' ($reviewChoices.Count -eq 2 -and $safeChoices.Count -eq 1) @{Review=$reviewChoices.Count;Safe=$safeChoices.Count}
  foreach($choice in $safeChoices){$choice.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle();Start-Sleep -Milliseconds 100}
 Invoke-Button 'UnDeleteLeftovers'
 Check 'Explicit leftover deletion confirmation' ($null -ne (Find 'UnLeftoverConfirmation')) $null
 Invoke-Button 'CloseButton'
 Check 'Cancel leaves synthetic leftover intact' ([IO.File]::Exists((Join-Path $fixtureRoot 'owned.txt'))) $null
 Save-WindowPicture 'leftover-review'
 Invoke-Button 'UnDeleteLeftovers';Invoke-Button 'PrimaryButton';Wait-Inventory
  Check 'Review DataPath remains intact after confirmed safe-only deletion' ([IO.File]::ReadAllText((Join-Path $fixtureRoot 'owned.txt')) -eq 'leftover') $null
 $registryRemaining=$registry.OpenSubKey("Software\$name");Check 'Selected leftover registry key removed' ($null -eq $registryRemaining) $null;if($registryRemaining){$registryRemaining.Dispose()}
 Check 'Sibling/shared fixture preserved' ([IO.File]::ReadAllText((Join-Path $siblingRoot 'retain.txt')) -eq 'retain') $null
  Check 'Registry-only deletion reports zero file bytes' ((Find 'UnCleanupSummary').Current.Name -match '0 B') (Find 'UnCleanupSummary').Current.Name
 Save-WindowPicture 'leftover-results'
 Invoke-Button 'UnScanLeftovers';Wait-Inventory
  Check 'Rescan retains only personal-data leftovers' ((Find 'UnLeftoverSummary').Current.Name -match '2 items') (Find 'UnLeftoverSummary').Current.Name
  Check 'Review-only rescan cannot delete' (!(Find 'UnDeleteLeftovers').Current.IsEnabled) $null
  Invoke-Button 'UnKeepLeftovers'
  Check 'Keep retains personal-data fixture' ([IO.File]::ReadAllText((Join-Path $fixtureRoot 'owned.txt')) -eq 'leftover') $null
  # Explicit test-fixture teardown, NOT product deletion. Verify the empty retry UI.
  [IO.Directory]::Delete($fixtureRoot,$true)
  Invoke-Button 'UnScanLeftovers';Wait-Inventory
  Check 'Rescan has no stale leftovers' ((Find 'UnLeftoverSummary').Current.Name -eq 'No leftovers.') (Find 'UnLeftoverSummary').Current.Name
 Navigate 'NavDashboard';Navigate 'NavUninstaller'
 Check 'Consumed selection cannot uninstall again' (!(Find 'UnUninstall').Current.IsEnabled) $null
 Invoke-Button 'UnRefresh';Wait-Inventory
 (Find 'UnSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('')
 foreach($filterName in @('MSI','Microsoft Store / MSIX','Last 30 days','At least 1.0 GB','All applications')) {Choose 'UnFilter' $filterName}
 foreach($sortName in @('Publisher','Install date','Reported size','Name')) {Choose 'UnSort' $sortName}
 Navigate 'NavSettings'
 $ar=([xml][IO.File]::ReadAllText((Resolve-Path src/SystemCore.App/Resources/Shell.ar.resx))).root.data
 Choose 'LanguagePicker' (($ar|Where-Object name -eq 'Arabic').value)
 Navigate 'NavUninstaller'
 Check 'Arabic Uninstaller title' ((Find 'PageTitle').Current.Name -eq ($ar|Where-Object name -eq 'NavUninstaller').value) $null
 Check 'Arabic refresh label' ((Find 'UnRefresh').Current.Name -eq ($ar|Where-Object name -eq 'UnRefresh').value) $null
 Save-WindowPicture 'inventory-ar'
 $null=$p.CloseMainWindow();Check 'Normal shutdown' ($p.WaitForExit(10000)) $null
} finally {
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(10000)};$p.Dispose()}
 $registry.DeleteSubKey("$root\$name",$false);$registry.Dispose()
 $cleanupKey=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Registry64)
 try {$cleanupKey.DeleteSubKeyTree("Software\$name",$false)}finally{$cleanupKey.Dispose()}
 if(Test-Path $fixtureRoot){[IO.Directory]::Delete($fixtureRoot,$true)}
 if(Test-Path $siblingRoot){[IO.Directory]::Delete($siblingRoot,$true)}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $results|ConvertTo-Json -Depth 5|Set-Content "$EvidencePath/checks.json" -Encoding utf8
}
