# Runs INSIDE the VM interactive session: page/theme/language screenshot pass for the audit.
param([string]$AppPath = 'C:\F1Audit\F1\F1.exe', [string]$OutDir = 'C:\F1Audit\ui-pages')
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class WP {
    [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Ri, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
}
"@
New-Item -ItemType Directory -Force $OutDir | Out-Null
$log = Join-Path $OutDir 'pages.txt'
"" | Set-Content $log
function Log([string]$l) { $l | Tee-Object $log -Append }
function Find($win, $id) { $win.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, $id))) }
function Shot($p, [string]$file) {
    $null = [WP]::SetForegroundWindow($p.MainWindowHandle); Start-Sleep -Milliseconds 900
    $r = New-Object WP+R; $null = [WP]::GetWindowRect($p.MainWindowHandle, [ref]$r)
    $bmp = New-Object Drawing.Bitmap(($r.Ri - $r.L), ($r.B - $r.T))
    $g = [Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
    try { $null = [WP]::PrintWindow($p.MainWindowHandle, $dc, 2) } finally { $g.ReleaseHdc($dc) }
    $bmp.Save((Join-Path $OutDir $file), [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose(); Log "SHOT $file"
}
function Nav($win, $id) {
    $n = Find $win $id
    if (-not $n) { Log "$id MISSING"; return }
    try { $n.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch { try { $n.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() } catch { Log "$id click failed: $($_.Exception.Message)" } }
    Log "nav $id"
}

$p = Start-Process $AppPath -PassThru
$sw = [Diagnostics.Stopwatch]::StartNew()
do { Start-Sleep -Milliseconds 400; $p.Refresh() } while ($p.MainWindowHandle -eq [IntPtr]::Zero -and $sw.Elapsed.TotalSeconds -lt 60)
if ($p.MainWindowHandle -eq [IntPtr]::Zero) { Log 'NO WINDOW'; exit 1 }
Start-Sleep -Seconds 6
$win = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)

# SystemInfo page
Nav $win 'NavSystemInfo'
Start-Sleep -Seconds 12
Shot $p '20-systeminfo-en.png'
$title = Find $win 'PageTitle'
Log "systeminfo title: $($title.Current.Name)"

# Settings + themes
Nav $win 'NavSettings'
Start-Sleep -Seconds 5
Shot $p '30-settings.png'
foreach ($theme in @('Light','Dark')) {
    $picker = Find $win 'ThemePicker'
    if ($picker) {
        $picker.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 800
        $item = $win.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $theme)))
        if ($item) { $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Seconds 3 }
    }
    Shot $p ("40-theme-" + $theme.ToLower() + ".png")
    Log "theme $theme captured"
}

# Arabic language + Uninstaller page
Nav $win 'NavUninstaller'
Start-Sleep -Seconds 15
$picker = Find $win 'LanguagePicker'
if ($picker) {
    try {
        $picker.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 800
        $arabic = $win.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, 'العربية')))
        if ($arabic) { $arabic.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Seconds 5; Log 'arabic selected' } else { Log 'arabic item MISSING' }
    } catch { Log "language picker error: $($_.Exception.Message)" }
}
Shot $p '50-uninstaller-ar.png'
$title = Find $win 'PageTitle'
Log "uninstaller ar title: $($title.Current.Name)"

$null = $p.CloseMainWindow(); $null = $p.WaitForExit(10000); if (-not $p.HasExited) { $p.Kill() }
Log 'UI PAGES DONE'
