param([string]$AppPath='artifacts/f1-ui/iterations/r2/publish/F1.exe',[string]$EvidencePath='artifacts/f1-ui/iterations/r2/runtime')
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
. ([scriptblock]::Create($source.Substring(0,$source.IndexOf('$p=Start-Process')))) -AppPath $AppPath -EvidencePath $EvidencePath
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
$saved=if(Test-Path $pref){[IO.File]::ReadAllBytes($pref)}else{$null}
if($null -ne $saved){[IO.File]::WriteAllBytes([IO.Path]::GetFullPath("$EvidencePath/original-preferences.bin"),$saved)}
try {
 [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"theme":"Dark","language":"ar"}')
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 $watch=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $watch.Elapsed.TotalSeconds -lt 30)
 Check 'F1 published launch' (!$p.HasExited -and $p.MainWindowHandle -ne [IntPtr]::Zero) $p.Id
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 $null=[ShellSmokeNative]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,10,10,1360,1000,0x14)
 Start-Sleep -Seconds 8
 $p.Refresh();Check 'F1 remains running through startup' (!$p.HasExited) $p.ExitCode
 Save-WindowPicture 'ar-dark-dashboard'
 Check 'Dashboard title loaded' ($null -ne (Find 'PageTitle')) (Find 'PageTitle').Current.Name
 $nodes=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
 @($nodes|ForEach-Object{[pscustomobject]@{Id=$_.Current.AutomationId;Name=$_.Current.Name;Bounds=$_.Current.BoundingRectangle.ToString();Offscreen=$_.Current.IsOffscreen}})|ConvertTo-Json -Depth 4|Set-Content "$EvidencePath/tree.json" -Encoding utf8
 $null=$p.CloseMainWindow();Check 'F1 normal close' ($p.WaitForExit(15000)) $null
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(15000)};$p.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $results|ConvertTo-Json -Depth 5|Set-Content "$EvidencePath/checks.json" -Encoding utf8
}
