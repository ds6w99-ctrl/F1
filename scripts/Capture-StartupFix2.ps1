param([string]$AppPath='artifacts/current/F1/F1.exe',[string]$EvidencePath='artifacts/current/review/startup-icons-fix2')
$ErrorActionPreference='Stop'
$visual=[IO.File]::ReadAllText((Resolve-Path scripts/Test-F1Visual.ps1))
$entry=[regex]::Match($visual,'(?m)^try\{\r?$').Index
. ([scriptblock]::Create($visual.Substring(0,$entry))) -AppPath $AppPath -EvidencePath $EvidencePath
$captureWindow=(Get-Item Function:Save-WindowPicture).ScriptBlock
function Save-WindowPicture([string]$name){Invoke-ForegroundCapture $captureWindow $name}
try {
 [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"theme":"Dark","language":"en"}')
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 $clock=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $clock.Elapsed.TotalSeconds -lt 30)
 Check 'Published F1 launched for read-only capture' (!$p.HasExited -and $p.MainWindowHandle -ne [IntPtr]::Zero) $null
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 Start-Sleep -Seconds 3
 $area=[Windows.Forms.Screen]::PrimaryScreen.WorkingArea
 $null=[ShellSmokeNative]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,$area.Left+10,$area.Top+10,[Math]::Min(1360,$area.Width-20),[Math]::Min(1000,$area.Height-20),0x14)
  Route 'Startup'
  Wait-InventoryButton 'StartRefresh'
  Check 'FIX7 Startup populated without refresh' ((Find 'StartStatus').Current.Name -match '^Inventory loaded|^Some startup sources') (Find 'StartStatus').Current.Name
 Invoke-Control 'StartRefresh'
 $clock.Restart()
 do{Start-Sleep -Milliseconds 200;$ready=(Find 'StartRefresh').Current.IsEnabled}while(!$ready -and $clock.Elapsed.TotalSeconds -lt 40)
 Check 'Read-only inventory returned' $ready $clock.Elapsed.TotalSeconds
 $rows=(Find 'StartItems').FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem))
 $first=$rows|Where-Object {!$_.Current.IsOffscreen}|Select-Object -First 1
 Check 'Manageable row available' ($null -ne $first) $null
 $first.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
 Check 'No contradictory active actions' (!((Find 'StartEnable').Current.IsEnabled -and (Find 'StartDisable').Current.IsEnabled)) $null
 Start-Sleep -Seconds 2
 Save-WindowPicture 'startup-en-selected'
 Route 'Settings';Choose 'LanguagePicker' $resources['ar']['Arabic'];Route 'Startup'
 Start-Sleep -Seconds 2
 Save-WindowPicture 'startup-ar-selected'
 Route 'About'
 Check 'About attribution preserved' ((Find 'F1Attribution').Current.Name -eq 'Made By Falcone') $null
 Save-WindowPicture 'sidebar-about-ar'
 $null=$p.CloseMainWindow();Check 'Capture app closed normally' ($p.WaitForExit(15000)) $null
 [pscustomobject]@{AppPath=$AppPath;AssemblySha256=(Get-FileHash (Join-Path (Split-Path $AppPath) 'F1.dll')).Hash;ReadOnly=$true;VisualReviewPerformed=$false;Utc=[DateTimeOffset]::UtcNow}|ConvertTo-Json|Set-Content "$EvidencePath/capture.json" -Encoding utf8
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(15000)};$p.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $results|ConvertTo-Json -Depth 5|Set-Content "$EvidencePath/checks.json" -Encoding utf8
}
