$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
. ([scriptblock]::Create($source.Substring(0,$source.IndexOf('$p=Start-Process')))) -AppPath artifacts/f1-ui/publish/F1.exe -EvidencePath artifacts/f1-ui/test-results/about-direction
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json';$saved=[IO.File]::ReadAllBytes($pref);$p=$null
try{
 [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"language":"ar","theme":"Dark"}')
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 $startup=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $startup.Elapsed.TotalSeconds -lt 30)
 Check 'About published startup ready' (!$p.HasExited -and $p.MainWindowHandle -ne [IntPtr]::Zero) $p.Id
 Start-Sleep -Seconds 2
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 Navigate 'NavAbout';Save-WindowPicture 'ar-version-ltr'
 Check 'Real version text available' ((Find 'F1AboutVersion').Current.Name.Contains('0.9.0-dev')) (Find 'F1AboutVersion').Current.Name
 $null=$p.CloseMainWindow();Check 'Normal close' ($p.WaitForExit(15000)) $null
}finally{if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(15000)};$p.Dispose()};[IO.File]::WriteAllBytes($pref,$saved);$results|ConvertTo-Json|Set-Content "$EvidencePath/checks.json" -Encoding utf8}
