param([string]$AppPath='artifacts/phase6/validation-app/SystemCore.App.exe',[string]$EvidencePath='artifacts/phase6/synthetic-ui')
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
. ([scriptblock]::Create($source.Substring(0,$source.IndexOf('$p=Start-Process')))) -AppPath $AppPath -EvidencePath $EvidencePath
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
$saved=if(Test-Path $pref){[IO.File]::ReadAllBytes($pref)}else{$null}
[IO.File]::WriteAllText($pref,'{"theme":"Light","language":"en"}')
$prior=@(Get-ChildItem $env:TEMP -Directory -Filter 'SystemCore-Startup-UI-*'|ForEach-Object FullName)
# Resolve the expected status label from the shipped resources and the current
# product identity instead of hardcoding a brand string. The previous literal
# 'Disabled by SystemCore' was pre-rebrand product copy and could never match the
# rendered text again.
$en=([xml][IO.File]::ReadAllText((Resolve-Path src/SystemCore.App/Resources/Shell.resx))).root.data
$brand=[regex]::Match([IO.File]::ReadAllText((Resolve-Path src/SystemCore.Core/ProductIdentity.cs)),'(?<![A-Za-z])Name\s*=\s*"([^"]+)"').Groups[1].Value
$disabledLabel=((($en|Where-Object name -eq 'StartStatusDisabledBySystemCore').value) -replace '\{Brand\}',$brand)
if(!$brand -or !$disabledLabel){throw 'Could not resolve the expected Startup status label'}
$p=$null
function WaitStartup([switch]$Changed) {
 $watch=[Diagnostics.Stopwatch]::StartNew()
 do{
  Start-Sleep -Milliseconds 150
  $refresh=Find 'StartRefresh';$status=Find 'StartStatus'
  $ready=$refresh -and $refresh.Current.IsEnabled -and $status -and !(Find 'StartConfirmation')
  if($Changed){$details=Find 'StartDetails';$ready=$ready -and $status.Current.Name -notmatch '^Inventory loaded|^Loading|^Refresh to read' -and (!$details -or $details.Current.Name -eq '')}
  else{$ready=$ready -and $status.Current.Name -match '^Inventory loaded|^Some startup sources'}
 }while(!$ready -and $watch.Elapsed.TotalSeconds -lt 30)
 Check 'Startup operation completed' $ready $watch.Elapsed.TotalSeconds
}
function WaitFor([string]$id,[int]$timeoutMs=5000,[switch]$Gone) {
 # Condition-based wait. A fixed sleep is not a valid readiness signal: the
 # ContentDialog close animation can outlive any chosen constant, and the view
 # re-entrancy guard correctly rejects a second invoke until ShowAsync completes.
 $clock=[Diagnostics.Stopwatch]::StartNew()
 do{
  $element=Find $id
  if($Gone){if($null -eq $element){return $true}}
  elseif($null -ne $element){return $element}
  Start-Sleep -Milliseconds 50
 }while($clock.Elapsed.TotalMilliseconds -lt $timeoutMs)
 return $null
}
function OpenConfirmation([string]$trigger,[int]$timeoutMs=6000) {
 # Bounded retry instead of a fixed sleep. After a cancelled confirmation the
 # view re-entrancy guard keeps rejecting invokes until the previous ShowAsync
 # completes (measured ~340 ms on this host, while the dialog leaves the UIA tree
 # after ~25 ms). Extra invokes are no-ops because the same guard rejects them, so
 # retrying cannot open a second dialog.
 $clock=[Diagnostics.Stopwatch]::StartNew()
 do{
  $open=Find 'StartConfirmation'
  if($open){return $open}
  $button=Find $trigger
  if($button -and $button.Current.IsEnabled){$button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()}
  Start-Sleep -Milliseconds 100
 }while($clock.Elapsed.TotalMilliseconds -lt $timeoutMs)
 return (Find 'StartConfirmation')
}
function SelectFixture([string]$name) {
 (Find 'StartSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($name)
 Start-Sleep -Milliseconds 150
 $wait=[Diagnostics.Stopwatch]::StartNew()
 do{ $list=Find 'StartItems';if(!$list){Start-Sleep -Milliseconds 100} }while(!$list -and $wait.Elapsed.TotalSeconds -lt 5)
 if(!$list){
  $window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|ForEach-Object {[pscustomobject]@{Id=$_.Current.AutomationId;Name=$_.Current.Name}}|ConvertTo-Json -Depth 3|Set-Content "$EvidencePath/missing-list-tree.json" -Encoding utf8
  throw 'Synthetic Startup list not present after search'
 }
 $items=$list.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem))
 $row=$items|Where-Object {$_.Current.Name -like "$name*"}|Select-Object -First 1
 Check "Synthetic item displayed: $name" ($null -ne $row) $null
 $row.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();Start-Sleep -Milliseconds 150
}
try {
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 $watch=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $watch.Elapsed.TotalSeconds -lt 30)
 Check 'Synthetic UI launched' (!$p.HasExited -and $p.MainWindowHandle -ne [IntPtr]::Zero) $null
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 Navigate 'NavStartup'
 (Find 'StartRefresh').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();WaitStartup
 foreach($name in @('SystemCore Synthetic Registry','SystemCore Synthetic Folder','SystemCore Synthetic Task')) {
  SelectFixture $name
  Check 'Synthetic source initially enabled' ((Find 'StartDisable').Current.IsEnabled -and !(Find 'StartEnable').Current.IsEnabled) $name
  $null=OpenConfirmation 'StartDisable'
  Check 'Disable confirmation present' ($null -ne (Find 'StartConfirmation')) $name
  Check 'Disable confirmation defaults focus to Cancel' ((Find 'CloseButton').Current.HasKeyboardFocus) $name
  (Find 'CloseButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
  $torndown=WaitFor 'StartConfirmation' 5000 -Gone
  Check 'Cancelled confirmation closes completely' $torndown $name
  Check 'Cancelled confirmation preserves enabled source' ((Find 'StartDisable').Current.IsEnabled) $name
  $primary=OpenConfirmation 'StartDisable'
  Check 'Disable confirmation reopens after cancel' ($null -ne $primary) $name
  $primaryButton=WaitFor 'PrimaryButton'
  Check 'Disable confirmation primary becomes available' ($null -ne $primaryButton) $name
  $primaryButton.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();WaitStartup -Changed
  Check 'Disable verified status' ((Find 'StartStatus').Current.Name -eq 'Startup change verified. Inventory refreshed.') (Find 'StartStatus').Current.Name
  SelectFixture $name
  Check 'DisabledBySystemCore after refresh' ((Find 'StartDetails').Current.Name.Contains($disabledLabel) -and (Find 'StartEnable').Current.IsEnabled) "$name | expected '$disabledLabel' | actual '$((Find 'StartDetails').Current.Name)'"
  Navigate 'NavDashboard';Navigate 'NavStartup';SelectFixture $name
  $null=OpenConfirmation 'StartEnable'
  Check 'Enable confirmation present' ($null -ne (Find 'StartConfirmation')) $name
  $enablePrimary=WaitFor 'PrimaryButton'
  Check 'Enable confirmation primary becomes available' ($null -ne $enablePrimary) $name
  $enablePrimary.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();WaitStartup -Changed
  Check 'Enable verified status' ((Find 'StartStatus').Current.Name -eq 'Startup change verified. Inventory refreshed.') (Find 'StartStatus').Current.Name
  SelectFixture $name
  Check 'Restored source enabled after refresh' ((Find 'StartDisable').Current.IsEnabled -and !(Find 'StartEnable').Current.IsEnabled) $name
 }
 $fixture=@(Get-ChildItem $env:TEMP -Directory -Filter 'SystemCore-Startup-UI-*'|Where-Object {$_.FullName -notin $prior})
 Check 'Exactly one isolated fixture root created' ($fixture.Count -eq 1) $null
 $audit=Get-Content (Join-Path $fixture[0].FullName 'audit.json') -Raw|ConvertFrom-Json
 Check 'Six backend operations succeeded' ($audit.Count -eq 6 -and @($audit|Where-Object {$_.Outcome -ne 0}).Count -eq 0) $null
 Check 'Each disable retained state and each enable finalized it' (@($audit|Where-Object {($_.Action -eq 0 -and !$_.StateExists) -or ($_.Action -eq 1 -and $_.StateExists)}).Count -eq 0) $null
 Copy-Item (Join-Path $fixture[0].FullName 'audit.json') "$EvidencePath/backend-audit.json"
 $null=$p.CloseMainWindow();Check 'Synthetic app closed normally' ($p.WaitForExit(10000)) $null
 $completion=Get-Content (Join-Path $fixture[0].FullName 'completed.json') -Raw|ConvertFrom-Json
 Check 'Fixture source and state teardown verified' ($completion.TaskEnabled -and $completion.FolderRestored -and $completion.RemainingStates -eq 0) $null
 Copy-Item (Join-Path $fixture[0].FullName 'completed.json') "$EvidencePath/backend-completion.json"
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(10000)};$p.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $results|ConvertTo-Json -Depth 5|Set-Content "$EvidencePath/checks.json" -Encoding utf8
}
