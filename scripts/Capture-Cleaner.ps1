param([string]$AppPath, [string]$EvidencePath, [int]$CleanerScanWatchdogSeconds = 900)
$ErrorActionPreference='Stop'
# Reuse the checked native UI helpers, without running the full regression suite.
$source=[IO.File]::ReadAllText((Resolve-Path 'scripts/Test-Shell.ps1'))
$prefix=$source.Substring(0,$source.IndexOf('$p=Start-Process'))
. ([scriptblock]::Create($prefix)) -AppPath $AppPath -EvidencePath $EvidencePath -CleanerScanWatchdogSeconds $CleanerScanWatchdogSeconds
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
$saved=if(Test-Path $pref){[IO.File]::ReadAllBytes($pref)}else{$null}
$p=$null
function Capture-Visible([string]$name) {
 $null=[ShellSmokeNative]::ShowWindow($p.MainWindowHandle,6)
 Start-Sleep -Milliseconds 200
 $null=[ShellSmokeNative]::ShowWindow($p.MainWindowHandle,9)
 $null=[ShellSmokeNative]::SetForegroundWindow($p.MainWindowHandle)
 Start-Sleep -Seconds 1
 Check 'Visible capture owns foreground' ([ShellSmokeNative]::GetForegroundWindow() -eq $p.MainWindowHandle) $name
 $rect=New-Object ShellSmokeNative+Rect
 $null=[ShellSmokeNative]::GetWindowRect($p.MainWindowHandle,[ref]$rect)
 $image=New-Object Drawing.Bitmap(($rect.Right-$rect.Left),($rect.Bottom-$rect.Top))
 $graphics=[Drawing.Graphics]::FromImage($image)
 try {
  $graphics.CopyFromScreen($rect.Left,$rect.Top,0,0,$image.Size)
  $image.Save([IO.Path]::GetFullPath("$EvidencePath/$name.png"),[Drawing.Imaging.ImageFormat]::Png)
 } finally {$graphics.Dispose();$image.Dispose()}
}
try {
 [IO.File]::WriteAllText($pref,'{"theme":"Light","language":"en"}')
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 $clock=[Diagnostics.Stopwatch]::StartNew()
 do {Start-Sleep -Milliseconds 300;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq 0 -and $clock.Elapsed.TotalSeconds -lt 30)
 Check 'Focused production launch' (!$p.HasExited -and $p.MainWindowHandle -ne 0) $null
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 Start-Sleep -Seconds 2
 Navigate 'NavCleaner'
 Check 'Production validation controls absent' ($null -eq (Find 'CleanerValidationPrepare')) $null
 Capture-Visible 'cleaner-en-light-before-scan'
 (Find 'CleanerScan').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
 Start-Sleep -Milliseconds 400;$clock.Restart()
 # Hang watchdog, not a speed budget: the real-device scan took 195.4s on one run and 279.3s on
 # another for the same commit, purely from user data and machine load. Only a genuine hang fails
 # here; Test-CleanerBenchmark.ps1 owns performance pass/fail on a fixed synthetic fixture.
 while(!(Find 'CleanerScan').Current.IsEnabled -and $clock.Elapsed.TotalSeconds -lt $CleanerScanWatchdogSeconds){Start-Sleep -Milliseconds 300}
 $realScanSeconds=$clock.Elapsed.TotalSeconds
 Check 'Focused real scan returned (hang watchdog, not a speed budget)' ((Find 'CleanerScan').Current.IsEnabled) ("{0:N1}s within {1}s watchdog" -f $realScanSeconds,$CleanerScanWatchdogSeconds)
 [pscustomobject]@{DurationSeconds=[Math]::Round($realScanSeconds,2);WatchdogSeconds=$CleanerScanWatchdogSeconds;Summary=(Find 'CleanerSummary').Current.Name;RecordedUtc=[DateTime]::UtcNow.ToString('o')} |
  ConvertTo-Json | Set-Content "$EvidencePath/cleaner-real-scan.json" -Encoding UTF8
 Capture-Visible 'cleaner-en-light-results'
 Check 'Foreground window matches capture target' ([ShellSmokeNative]::GetForegroundWindow() -eq $p.MainWindowHandle) $null
 (Find 'CleanerClean').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
 Start-Sleep -Milliseconds 300
 Check 'Focused native confirmation exists' ($null -ne (Find 'CleanerConfirmation')) $null
 Capture-Visible 'cleaner-en-light-confirmation'
 # Never confirm deletion against the real device in this capture script.
 (Find 'CloseButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
 Start-Sleep -Milliseconds 200
 Check 'Real confirmation cancelled' ($null -eq (Find 'CleanerConfirmation')) $null
 $null=$p.CloseMainWindow();Check 'Focused capture clean shutdown' ($p.WaitForExit(10000)) $null
} finally {
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(10000)};$p.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $results | ConvertTo-Json -Depth 4 | Set-Content "$EvidencePath/ui-results.json" -Encoding UTF8
}
