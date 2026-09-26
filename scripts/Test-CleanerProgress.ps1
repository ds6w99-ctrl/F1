param([string]$AppPath, [string]$EvidencePath)
$ErrorActionPreference='Stop'
# Read-only native UI check: scan/cancel only. Never invokes Clean or confirmation.
$source=[IO.File]::ReadAllText((Resolve-Path 'scripts/Test-Shell.ps1'))
. ([scriptblock]::Create($source.Substring(0,$source.IndexOf('$p=Start-Process')))) -AppPath $AppPath -EvidencePath $EvidencePath
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
$saved=if(Test-Path $pref){[IO.File]::ReadAllBytes($pref)}else{$null}
$p=$null
try {
 [IO.File]::WriteAllText($pref,'{"theme":"System","language":"en"}')
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 $watch=[Diagnostics.Stopwatch]::StartNew()
 do {Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq 0 -and $watch.Elapsed.TotalSeconds -lt 30)
 Check 'Progress test native launch' (!$p.HasExited -and $p.MainWindowHandle -ne 0) $null
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 Start-Sleep -Seconds 2
 foreach($language in @('en','ar')) {
  $resource=if($language -eq 'en'){'src/SystemCore.App/Resources/Shell.resx'}else{'src/SystemCore.App/Resources/Shell.ar.resx'}
  $strings=([xml][IO.File]::ReadAllText((Resolve-Path $resource))).root.data
  if($language -eq 'ar') {Navigate 'NavSettings';Choose 'LanguagePicker' (($strings|Where-Object name -eq 'Arabic').value)}
  Navigate 'NavCleaner'
  Check "$language progress element exists" ($null -ne (Find 'CleanerLiveProgress')) $null
  (Find 'CleanerScan').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
  $samples=[Collections.Generic.List[string]]::new();$watch.Restart()
  do {
   Start-Sleep -Milliseconds 50
   $text=(Find 'CleanerLiveProgress').Current.Name
   if($text -and ($samples.Count -eq 0 -or $samples[$samples.Count-1] -ne $text)){$samples.Add($text)}
  }while($watch.Elapsed.TotalSeconds -lt 3)
  Check "$language changing live progress while scan busy" ($samples.Count -ge 3 -and !(Find 'CleanerScan').Current.IsEnabled) $samples.Count
  $template=($strings|Where-Object name -eq 'CleanLiveProgress').value
  foreach($part in @($template.Split([char]0xB7)[1..3])) {
   $label=$part.Split(':')[0].Trim()
   Check "$language live field $label" ($samples[$samples.Count-1].Contains($label)) $samples[$samples.Count-1]
  }
  Check "$language UI responds during scan" $p.Responding $null
  Check "$language Clean disabled during scan" (!(Find 'CleanerClean').Current.IsEnabled) $null
  Save-WindowPicture "progress-$language"
  (Find 'CleanerCancel').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();$watch.Restart()
  do {Start-Sleep -Milliseconds 50}while(!(Find 'CleanerScan').Current.IsEnabled -and $watch.Elapsed.TotalSeconds -lt 5)
  Check "$language cancellation returns idle" ((Find 'CleanerScan').Current.IsEnabled -and $watch.Elapsed.TotalSeconds -lt 5) $watch.Elapsed.TotalMilliseconds
  Start-Sleep -Milliseconds 250
  Check "$language queued progress cleared after cancel" ([string]::IsNullOrEmpty((Find 'CleanerLiveProgress').Current.Name)) $null
  $samples|ConvertTo-Json|Set-Content "$EvidencePath/progress-$language.json" -Encoding utf8
 }
 $null=$p.CloseMainWindow();Check 'Progress test clean shutdown' ($p.WaitForExit(10000)) $null
} finally {
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(10000)};$p.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $results|ConvertTo-Json -Depth 4|Set-Content "$EvidencePath/ui-results.json" -Encoding utf8
}
