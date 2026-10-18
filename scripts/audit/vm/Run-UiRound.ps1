# Runs INSIDE the VM interactive session: drives the real F1 UI via UIA.
# Captures screenshots + UIA facts for the audit UI round.
param(
    [string]$AppPath = 'C:\F1Audit\F1\F1.exe',
    [string]$OutDir = 'C:\F1Audit\ui-evidence',
    [switch]$DriveUninstall,
    [string]$TargetApp = '7-Zip'
)
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class W2 {
    [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Ri, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out R r);
}
"@
New-Item -ItemType Directory -Force $OutDir | Out-Null
$log = Join-Path $OutDir 'ui-round.txt'
"" | Set-Content $log
function Log([string]$line) { $line | Tee-Object $log -Append }
function Find($win, $id) { $win.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, $id))) }
function FindName($win, $name) { $win.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $name))) }
function Shot([string]$file) {
    $p = Get-Process -Name F1 -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $p) { Log "SHOT $file : no F1 window"; return }
    $null = [W2]::SetForegroundWindow($p.MainWindowHandle); Start-Sleep -Milliseconds 700
    $rect = New-Object W2+R
    $null = [W2]::GetWindowRect($p.MainWindowHandle, [ref]$rect)
    $bmp = New-Object Drawing.Bitmap(($rect.Ri - $rect.L), ($rect.B - $rect.T))
    $g = [Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
    try { $null = [W2]::PrintWindow($p.MainWindowHandle, $dc, 2) } finally { $g.ReleaseHdc($dc) }
    $bmp.Save((Join-Path $OutDir $file), [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Log "SHOT $file"
}

$p = Start-Process $AppPath -PassThru
$sw = [Diagnostics.Stopwatch]::StartNew()
do { Start-Sleep -Milliseconds 400; $p.Refresh() } while ($p.MainWindowHandle -eq [IntPtr]::Zero -and $sw.Elapsed.TotalSeconds -lt 60)
if ($p.MainWindowHandle -eq [IntPtr]::Zero) { Log 'NO WINDOW'; exit 1 }
Start-Sleep -Seconds 6
$win = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
Log "window title: $($win.Current.Name)"
Shot '00-dashboard.png'

# navigate to Uninstaller
$nav = Find $win 'NavUninstaller'
if ($nav) { $nav.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Log 'nav -> Uninstaller' } else { Log 'NavUninstaller MISSING' }
Start-Sleep -Seconds 20
Shot '10-uninstaller.png'
$status = Find $win 'UnStatus'
Log "status: $($status.Current.Name)"
$list = Find $win 'UnApplications'
if ($list) {
    $items = $list.FindAll([Windows.Automation.TreeScope]::Children, [Windows.Automation.Condition]::TrueCondition)
    Log "list items: $($items.Count)"
    foreach ($i in $items) { if ($i.Current.Name) { Log "  item: $($i.Current.Name)" } }
} else { Log 'UnApplications MISSING' }

if ($DriveUninstall) {
    $target = $null
    if ($list) {
        foreach ($i in $list.FindAll([Windows.Automation.TreeScope]::Children, [Windows.Automation.Condition]::TrueCondition)) {
            if ($i.Current.Name -like "*$TargetApp*") { $target = $i; break }
        }
    }
    if (-not $target) { Log "target app '$TargetApp' not found"; exit 2 }
    $target.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Seconds 2
    Shot '20-app-selected.png'
    $uninstall = Find $win 'UnUninstall'
    if (-not $uninstall) { Log 'UnUninstall MISSING'; exit 3 }
    $uninstall.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 4
    Shot '30-confirm-dialog.png'
    # ContentDialog primary button (English text "Uninstall")
    $primary = FindName $win 'Uninstall'
    if ($primary) { $primary.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Log 'confirmed uninstall' }
    else { Log 'dialog primary MISSING' }
    Start-Sleep -Seconds 30
    Shot '40-after-uninstall.png'
    $status = Find $win 'UnStatus'
    Log "status after: $($status.Current.Name)"
    $summary = Find $win 'UnLeftoverSummary'
    if ($summary) { Log "leftover summary: $($summary.Current.Name)" }
    Start-Sleep -Seconds 10
    Shot '50-leftovers.png'
}
Log 'UI ROUND DONE'
