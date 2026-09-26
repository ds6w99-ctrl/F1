$ErrorActionPreference='Stop'
$visual=[IO.File]::ReadAllText((Resolve-Path scripts/Test-F1Visual.ps1))
$entry=[regex]::Match($visual,'(?m)^try\{\r?$').Index
. ([scriptblock]::Create($visual.Substring(0,$entry))) -AppPath artifacts/f1-logo/publish/F1.exe -EvidencePath artifacts/f1-logo/screenshots
try{
 [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"theme":"Dark","language":"en"}')
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 $clock=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $clock.Elapsed.TotalSeconds -lt 30)
 Check 'Logo build launches' (!$p.HasExited -and $p.MainWindowHandle -ne [IntPtr]::Zero) $p.Id
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 Start-Sleep -Seconds 5
 $null=[ShellSmokeNative]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,10,10,1120,900,0x14)
 $language='en'
 foreach($theme in @('Dark','Light')){
  Route 'Settings';Choose 'ThemePicker' $resources.en["Theme$theme"]
  Route 'Dashboard';Capture 'Dashboard'
  Route 'About';Capture 'About'
  Check 'Attribution retained' ((Find 'F1Attribution').Current.Name -eq 'Made By Falcone') $theme
 }
 $null=$p.CloseMainWindow();Check 'Normal close' ($p.WaitForExit(15000)) $null
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(15000)};$p.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $shots|ConvertTo-Json -Depth 5|Set-Content "$EvidencePath/metadata.json" -Encoding utf8
 $results|ConvertTo-Json -Depth 5|Set-Content "$EvidencePath/checks.json" -Encoding utf8
}
