param([string]$AppPath='artifacts/f1-ui/publish/F1.exe',[string]$EvidencePath='artifacts/f1-ui/test-results/ui')
$ErrorActionPreference='Stop'
# Reuse the accepted checks, with explicit changes to the approved rebrand contracts.
# Historic scripts/evidence retain their original SystemCore expectations.
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-ProductUx.ps1))
$source=$source.Replace('$resources.en.NavDashboard','$resources.en.F1Welcome').Replace('$resources.ar.NavDashboard','$resources.ar.F1Welcome')
$source=$source.Replace('$resources[$language]["Nav$page"]','$(if($page -eq ''Dashboard''){$resources[$language].F1Welcome}else{$resources[$language]["Nav$page"]})')
$source=$source.Replace('"SystemCore $ExpectedVersion X64"','"F1 $ExpectedVersion X64"')
$source=$source.Replace("(Find 'LanguagePicker').SetFocus();[Windows.Forms.SendKeys]::SendWait('{TAB}')",@'
$focusWait=[Diagnostics.Stopwatch]::StartNew()
do {
 $null=[ShellSmokeNative]::SetForegroundWindow($p.MainWindowHandle)
 (Find 'LanguagePicker').SetFocus();Start-Sleep -Milliseconds 200
}while((!(Find 'LanguagePicker').Current.HasKeyboardFocus -or [ShellSmokeNative]::GetForegroundWindow() -ne $p.MainWindowHandle) -and $focusWait.Elapsed.TotalSeconds -lt 5)
Check 'Keyboard test targets focused language control' ((Find 'LanguagePicker').Current.HasKeyboardFocus -and [ShellSmokeNative]::GetForegroundWindow() -eq $p.MainWindowHandle) $null
[Windows.Forms.SendKeys]::SendWait('{TAB}')
'@)
& ([scriptblock]::Create($source)) -AppPath $AppPath -EvidencePath $EvidencePath -ExpectedVersion '1.0.0'
