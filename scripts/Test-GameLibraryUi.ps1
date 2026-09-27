param([string]$AppPath='artifacts/current/F1/F1.exe',[string]$EvidencePath='artifacts/current/game-library-ui')
$ErrorActionPreference='Stop'
$visual=[IO.File]::ReadAllText((Resolve-Path scripts/Test-F1Visual.ps1))
$entry=[regex]::Match($visual,'(?m)^try\{\r?$').Index
. ([scriptblock]::Create($visual.Substring(0,$entry))) -AppPath $AppPath -EvidencePath $EvidencePath
$captureWindow=(Get-Item Function:Save-WindowPicture).ScriptBlock
function Save-WindowPicture([string]$name){Invoke-ForegroundCapture $captureWindow $name}
function Rows { (Find 'UnApplications').FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem)) }
try {
 [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"theme":"Dark","language":"en"}')
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 $clock=[Diagnostics.Stopwatch]::StartNew()
 do { Start-Sleep -Milliseconds 200; $p.Refresh() } while(!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $clock.Elapsed.TotalSeconds -lt 30)
 Check 'Unified library app launches' (!$p.HasExited -and $p.MainWindowHandle -ne [IntPtr]::Zero) $p.Id
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 Start-Sleep -Seconds 5
 $language='en';$theme='Dark'
  Route 'Uninstaller'; Wait-InventoryButton 'UnRefresh'
  Check 'FIX7 unified inventory populated without refresh' ((Rows).Count -gt 0) (Find 'UnCount').Current.Name
  Invoke-Control 'UnRefresh'
 $clock.Restart()
 do { Start-Sleep -Milliseconds 200 } while(!(Find 'UnRefresh').Current.IsEnabled -and $clock.Elapsed.TotalSeconds -lt 120)
 Check 'Read-only game discovery completes' (Find 'UnRefresh').Current.IsEnabled $clock.Elapsed.TotalSeconds
 Check 'Original status/size filter retained' ($null -ne (Find 'UnFilter')) $null
 Check 'Small item-kind filter present' ($null -ne (Find 'UnKindFilter')) $null
 Choose 'UnKindFilter' $resources.en.UnGamesKind
 $rows=Rows
 Check 'Games appear inside existing unified list' ($rows.Count -gt 0) $rows.Count
 $managed=$false
 foreach($row in $rows){
  $row.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();Start-Sleep -Milliseconds 150
  if((Find 'UnDetails').Current.Name.StartsWith('Managed by ')){$managed=$true;break}
 }
 Check 'Managed game status is explicit' $managed (Find 'UnDetails').Current.Name
 $uninstall=Find 'UnUninstall'
 Check 'Managed game has no destructive action' ($null -eq $uninstall -or !$uninstall.Current.IsEnabled) $null
 Check 'Launcher action has its own control' ($null -ne (Find 'UnOpenLauncher')) $null
 Check 'Managed game cannot scan leftovers' (!(Find 'UnScanLeftovers').Current.IsEnabled) $null
 Capture 'Uninstaller' '-games-managed'
 # No game, launcher or uninstall action is invoked. Search/filter only.
 (Find 'UnSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('F1-no-game-match-96A1')
 Start-Sleep -Milliseconds 250
 Check 'Game search preserves empty state' ((Rows).Count -eq 0) $null
 (Find 'UnSearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('')
 Choose 'UnKindFilter' $resources.en.UnApplicationsKind
 Check 'Applications filter retains native inventory' ((Rows).Count -gt 0) (Rows).Count
 Choose 'UnKindFilter' $resources.en.UnAllKinds
 $language='ar'; Route 'Settings';Choose 'LanguagePicker' $resources.ar.Arabic
 Route 'Uninstaller';Choose 'UnKindFilter' $resources.ar.UnGamesKind
 Check 'Arabic game filter uses same list' ((Rows).Count -gt 0) $null
 Capture 'Uninstaller' '-games-ar'
 $null=$p.CloseMainWindow();Check 'Game library normal close' ($p.WaitForExit(15000)) $null
} finally {
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(15000)};$p.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $shots|ConvertTo-Json -Depth 6|Set-Content "$EvidencePath/metadata.json" -Encoding utf8
 $results|ConvertTo-Json -Depth 6|Set-Content "$EvidencePath/checks.json" -Encoding utf8
}
