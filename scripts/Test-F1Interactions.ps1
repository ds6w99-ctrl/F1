$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
. ([scriptblock]::Create($source.Substring(0,$source.IndexOf('$p=Start-Process')))) -AppPath artifacts/f1-ui/publish/F1.exe -EvidencePath artifacts/f1-ui/test-results/interactions
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class F1Mouse {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
}
'@
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json';$saved=[IO.File]::ReadAllBytes($pref);$p=$null
try{
 [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"language":"en","theme":"Dark"}')
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt";Start-Sleep -Seconds 5;$p.Refresh()
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 Navigate 'NavDashboard'
 (Find 'F1Search').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();Start-Sleep -Milliseconds 400
 $edit=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Edit))|Where-Object {!$_.Current.IsOffscreen}|Select-Object -First 1
 Check 'Page search has usable text input' ($null -ne $edit) $null
 $edit.SetFocus();[Windows.Forms.SendKeys]::SendWait('Memory{ENTER}');Start-Sleep -Milliseconds 600
 Check 'Search navigates to existing Memory route' ((Find 'PageTitle').Current.Name -eq 'Memory') (Find 'PageTitle').Current.Name
 (Find 'F1Settings').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();Start-Sleep -Milliseconds 400
 Check 'Titlebar settings button routes' ((Find 'PageTitle').Current.Name -eq 'Settings') $null
 (Find 'F1SettingsAbout').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();Start-Sleep -Milliseconds 400
 Check 'Settings About link routes' ($null -ne (Find 'F1Attribution')) $null
 Navigate 'NavDashboard';$rect=New-Object ShellSmokeNative+Rect;$null=[ShellSmokeNative]::GetWindowRect($p.MainWindowHandle,[ref]$rect)
 $null=[ShellSmokeNative]::SetForegroundWindow($p.MainWindowHandle)
 $null=[F1Mouse]::SetCursorPos($rect.Left+400,$rect.Top+20);[F1Mouse]::mouse_event(2,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 150
 $null=[F1Mouse]::SetCursorPos($rect.Left+440,$rect.Top+50);Start-Sleep -Milliseconds 150;[F1Mouse]::mouse_event(4,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 300
 $moved=New-Object ShellSmokeNative+Rect;$null=[ShellSmokeNative]::GetWindowRect($p.MainWindowHandle,[ref]$moved)
 Check 'Custom title drag region moves native window' ($moved.Left -ne $rect.Left -or $moved.Top -ne $rect.Top) @{Before=@($rect.Left,$rect.Top);After=@($moved.Left,$moved.Top)}
 $pattern=$window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern)
 $pattern.SetWindowVisualState([Windows.Automation.WindowVisualState]::Maximized);Start-Sleep -Milliseconds 400
 Check 'Native maximize supported' ($pattern.Current.WindowVisualState -eq [Windows.Automation.WindowVisualState]::Maximized) $null
 $pattern.SetWindowVisualState([Windows.Automation.WindowVisualState]::Normal);Start-Sleep -Milliseconds 400
 Check 'Native restore supported' ($pattern.Current.WindowVisualState -eq [Windows.Automation.WindowVisualState]::Normal) $null
 Save-WindowPicture 'interaction-dashboard'
 $null=[ShellSmokeNative]::SetForegroundWindow($p.MainWindowHandle);[Windows.Forms.SendKeys]::SendWait('%{F4}')
 Check 'Alt F4 closes normally' ($p.WaitForExit(15000)) $null
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(15000)};$p.Dispose()}
 [IO.File]::WriteAllBytes($pref,$saved)
 $results|ConvertTo-Json -Depth 6|Set-Content "$EvidencePath/checks.json" -Encoding utf8
}
