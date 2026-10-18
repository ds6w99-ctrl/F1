param([string]$AppPath = 'G:\F1\artifacts\publish\App\F1.exe', [string]$OutDir = 'G:\F1\artifacts\f1-ui\local-validation\backgrounds')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class WB {
    [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Ri, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
"@
New-Item -ItemType Directory -Force $OutDir | Out-Null
function Find($win, $id) { $win.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, $id))) }
function Capture($proc, [string]$name) {
    $null = [WB]::SetForegroundWindow($proc.MainWindowHandle); Start-Sleep -Milliseconds 900
    $r = New-Object WB+R; $null = [WB]::GetWindowRect($proc.MainWindowHandle, [ref]$r)
    $bmp = New-Object Drawing.Bitmap(($r.Ri - $r.L), ($r.B - $r.T))
    $g = [Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
    try { $null = [WB]::PrintWindow($proc.MainWindowHandle, $dc, 2) } finally { $g.ReleaseHdc($dc) }
    if ([WB]::GetForegroundWindow() -eq $proc.MainWindowHandle) { $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size) }
    $bmp.Save((Join-Path $OutDir "$name.png"), [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
}
$log = Join-Path $OutDir 'backgrounds.txt'; Remove-Item $log -ErrorAction SilentlyContinue
$p = Start-Process $AppPath -PassThru
$sw = [Diagnostics.Stopwatch]::StartNew()
do { Start-Sleep -Milliseconds 400; $p.Refresh() } while ($p.MainWindowHandle -eq [IntPtr]::Zero -and $sw.Elapsed.TotalSeconds -lt 40)
Start-Sleep -Seconds 4
$win = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
# Open Settings, then switch the theme through the ThemePicker.
$nav = Find $win 'NavSettings'
if ($nav) { $nav.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Seconds 2 }
foreach ($theme in @('Light', 'Dark')) {
    $picker = Find $win 'ThemePicker'
    if (-not $picker) { "ThemePicker missing" | Out-File $log -Append; break }
    $picker.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 700
    $item = $win.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $theme)))
    if ($item) { $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Seconds 2 }
    Capture $p ("theme-" + $theme.ToLower())
    "captured $theme" | Out-File $log -Append
}
$null = $p.CloseMainWindow(); $null = $p.WaitForExit(10000); if (-not $p.HasExited) { $p.Kill() }
Get-Content $log
