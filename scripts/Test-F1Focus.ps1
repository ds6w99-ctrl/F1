$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
. ([scriptblock]::Create($source.Substring(0,$source.IndexOf('$p=Start-Process')))) -AppPath artifacts/f1-ui/publish/F1.exe -EvidencePath artifacts/f1-ui/test-results/focus-probe
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json';$saved=[IO.File]::ReadAllBytes($pref)
try{
 [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"theme":"Dark","language":"ar"}')
 $p=Start-Process (Resolve-Path $AppPath) -PassThru;Start-Sleep -Seconds 5;$p.Refresh()
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 Navigate 'NavSettings';Choose 'LanguagePicker' 'English';Choose 'ThemePicker' 'Light'
 (Find 'LanguagePicker').SetFocus()
 $focused=[Windows.Automation.AutomationElement]::FocusedElement
 [pscustomobject]@{Stage='Before';Id=$focused.Current.AutomationId;Name=$focused.Current.Name;Type=$focused.Current.ControlType.ProgrammaticName;NativeForeground=[ShellSmokeNative]::GetForegroundWindow().ToInt64();App=$p.MainWindowHandle.ToInt64()}|ConvertTo-Json
 [Windows.Forms.SendKeys]::SendWait('{TAB}');Start-Sleep -Milliseconds 250
 $focused=[Windows.Automation.AutomationElement]::FocusedElement
 [pscustomobject]@{Stage='After';Id=$focused.Current.AutomationId;Name=$focused.Current.Name;Type=$focused.Current.ControlType.ProgrammaticName}|ConvertTo-Json
 Save-WindowPicture 'focus'
 Start-Sleep -Seconds 1;(Find 'LanguagePicker').SetFocus();Start-Sleep -Milliseconds 250;[Windows.Forms.SendKeys]::SendWait('{TAB}');Start-Sleep -Milliseconds 250
 $focused=[Windows.Automation.AutomationElement]::FocusedElement
 [pscustomobject]@{Stage='Settled';Id=$focused.Current.AutomationId;Name=$focused.Current.Name;Type=$focused.Current.ControlType.ProgrammaticName}|ConvertTo-Json
}finally{if($p){$null=$p.CloseMainWindow();$null=$p.WaitForExit(15000);$p.Dispose()};[IO.File]::WriteAllBytes($pref,$saved)}
