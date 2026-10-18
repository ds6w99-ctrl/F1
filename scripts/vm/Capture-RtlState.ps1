param([string]$AppPath = 'G:\F1\artifacts\publish\App\F1.exe', [string]$OutDir = 'G:\F1\artifacts\f1-ui\local-validation\rtl-check', [switch]$SkipArabic)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class W {
    [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Ri, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
"@
New-Item -ItemType Directory -Force $OutDir | Out-Null
$log = Join-Path $OutDir 'rtl.txt'
Remove-Item $log -ErrorAction SilentlyContinue
function Find($win, $id) { $win.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, $id))) }

$p = Start-Process $AppPath -PassThru
$sw = [Diagnostics.Stopwatch]::StartNew()
do { Start-Sleep -Milliseconds 400; $p.Refresh() } while ($p.MainWindowHandle -eq [IntPtr]::Zero -and $sw.Elapsed.TotalSeconds -lt 40)
if ($p.MainWindowHandle -eq [IntPtr]::Zero) { "NO WINDOW" | Out-File $log; exit 1 }
Start-Sleep -Seconds 4
$win = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)

if (-not $SkipArabic) {
    $picker = $null; $sw.Restart()
    do { $picker = Find $win 'LanguagePicker'; if (-not $picker) { Start-Sleep -Milliseconds 400 } } while (-not $picker -and $sw.Elapsed.TotalSeconds -lt 20)
    "PICKER: $([bool]$picker)" | Out-File $log -Append
    if ($picker) {
        try {
            $picker.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
            Start-Sleep -Seconds 1
            $arabic = Find $win 'العربية'
            if (-not $arabic) { $arabic = $win.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, 'العربية'))) }
            "ITEM: $([bool]$arabic)" | Out-File $log -Append
            if ($arabic) { $arabic.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Seconds 3 }
        } catch { "ERR: $($_.Exception.Message)" | Out-File $log -Append }
    }
}
Start-Sleep -Seconds 2
foreach ($id in @('NavCleaner', 'NavDashboard', 'ContentHost', 'PageTitle')) {
    $element = Find $win $id
    if ($element) { $r = $element.Current.BoundingRectangle; "$id -> X=$([int]$r.X) W=$([int]$r.Width) name='$($element.Current.Name)'" | Out-File $log -Append }
    else { "$id -> MISSING" | Out-File $log -Append }
}
# Whole-window bounds tell the pane side unambiguously: the pane is on the side nearest the nav item.
$null = [W]::SetForegroundWindow($p.MainWindowHandle); Start-Sleep -Milliseconds 800
$rect = New-Object W+R
$null = [W]::GetWindowRect($p.MainWindowHandle, [ref]$rect)
$bmp = New-Object Drawing.Bitmap(($rect.Ri - $rect.L), ($rect.B - $rect.T))
$g = [Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
try { $null = [W]::PrintWindow($p.MainWindowHandle, $dc, 2) } finally { $g.ReleaseHdc($dc) }
if ([W]::GetForegroundWindow() -eq $p.MainWindowHandle) { $g.CopyFromScreen($rect.L, $rect.T, 0, 0, $bmp.Size) }
$bmp.Save((Join-Path $OutDir 'window.png'), [Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
$null = $p.CloseMainWindow(); $null = $p.WaitForExit(10000)
if (-not $p.HasExited) { $p.Kill() }
Get-Content $log
