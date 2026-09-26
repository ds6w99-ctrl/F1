$ErrorActionPreference='Stop'
$visual=[IO.File]::ReadAllText((Resolve-Path scripts/Test-F1Visual.ps1))
$entry=[regex]::Match($visual,'(?m)^try\{\r?$').Index
. ([scriptblock]::Create($visual.Substring(0,$entry))) -AppPath artifacts/current/F1/F1.exe -EvidencePath artifacts/project-cleanup/runtime
try {
 [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"theme":"Dark","language":"en"}')
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 $clock=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $clock.Elapsed.TotalSeconds -lt 30)
 Check 'Launch' (!$p.HasExited -and $p.MainWindowHandle -ne [IntPtr]::Zero) $p.Id
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 Start-Sleep -Seconds 6
 foreach($page in @('Dashboard','Uninstaller','Startup','Cleaner','SystemInfo','Performance','MemoryTools','Settings','Tools','About')){
  Route $page
  Check "$page title available" ($null -ne (Find 'PageTitle')) $null
  Check 'Application remains running' (!$p.HasExited) $page
 }
 Check 'Attribution retained' ((Find 'F1Attribution').Current.Name -eq 'Made By Falcone') $null
 Check 'Premium card absent' ($null -eq (Find 'F1AboutPremium')) $null
 $null=$p.CloseMainWindow();Check 'Normal close' ($p.WaitForExit(15000)) $null
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(15000)};$p.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $results|ConvertTo-Json -Depth 6|Set-Content "$EvidencePath/checks.json" -Encoding utf8
}
