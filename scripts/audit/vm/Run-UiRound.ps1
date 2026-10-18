# Runs INSIDE the VM interactive session: drives the real F1 UI via UIA.
# Captures screenshots + UIA facts for the audit UI round.
param(
    [string]$AppPath = 'C:\F1Audit\F1\F1.exe',
    [string]$OutDir = 'C:\F1Audit\ui-evidence',
    [switch]$DriveUninstall,
    [string]$TargetApp = '7-Zip'
)
$ErrorActionPreference = 'Continue'
$script:lastEnter = $null
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
function ShotScreen([string]$file) {
    Add-Type -AssemblyName System.Windows.Forms
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object Drawing.Bitmap $bounds.Width, $bounds.Height
    $g = [Drawing.Graphics]::FromImage($bmp)
    try { $g.CopyFromScreen($bounds.Location, [Drawing.Point]::Empty, $bounds.Size) } catch { Log "SHOTSCREEN $file : $($_.Exception.Message)" }
    $bmp.Save((Join-Path $OutDir $file), [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Log "SHOTSCREEN $file"
}
function AllButtons($win) {
    $buttons = $win.FindAll([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Button)))
    foreach ($b in $buttons) { if ($b.Current.Name) { Log "  btn: '$($b.Current.Name)' id='$($b.Current.AutomationId)' enabled=$($b.Current.IsEnabled)" } }
}
function ClickNativeInstallerDialog {
    # Simulates the real user closing the native uninstaller's completion dialog (msiexec/NSIS
    # finish screens). Runs in the same session as F1, so UIA can see those windows.
    $rootEl = [Windows.Automation.AutomationElement]::RootElement
    $wins = $rootEl.FindAll([Windows.Automation.TreeScope]::Children, [Windows.Automation.Condition]::TrueCondition)
    foreach ($w in $wins) {
        $p2 = Get-Process -Id $w.Current.ProcessId -ErrorAction SilentlyContinue
        if (-not $p2) { continue }
        if ($p2.Name -notmatch 'msiexec|unins|setup|Un_A|consent') { continue }
        Log "  native win: pid=$($p2.Name) title='$($w.Current.Name)'"
        $btns = $w.FindAll([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Button)))
        foreach ($b in $btns) {
            $n = ($b.Current.Name -replace '&','')
            if ($b.Current.IsEnabled -and $n -match '^(OK|Finish|Close|Yes|Uninstall|Next)$') {
                Log "clicking native dialog btn '$($b.Current.Name)' (pid=$($p2.Name))"
                $b.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
                return $true
            }
        }
        # Fallback: press Enter on the dialog (accepts its default action, as a real user would).
        # Throttled to once per 60s so a repeat-press cannot confirm an unexpected second prompt.
        if ($script:lastEnter -and ((Get-Date) - $script:lastEnter).TotalSeconds -lt 60) { continue }
        $script:lastEnter = Get-Date
        $null = [W2]::SetForegroundWindow($w.Current.NativeWindowHandle); Start-Sleep -Milliseconds 600
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        Log "pressed ENTER on native window '$($w.Current.Name)'"
        return $true
    }
    return $false
}

$p = Start-Process $AppPath -PassThru
$sw = [Diagnostics.Stopwatch]::StartNew()
do { Start-Sleep -Milliseconds 400; $p.Refresh() } while ($p.MainWindowHandle -eq [IntPtr]::Zero -and $sw.Elapsed.TotalSeconds -lt 60)
if ($p.MainWindowHandle -eq [IntPtr]::Zero) { Log 'NO WINDOW'; exit 1 }
Start-Sleep -Seconds 6
$win = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
Log "window title: $($win.Current.Name)"
Shot '00-dashboard.png'

# navigate to Uninstaller (NavigationViewItem exposes SelectionItemPattern)
$nav = Find $win 'NavUninstaller'
if ($nav) {
    try { $nav.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
    catch { try { $nav.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch { Log "nav pattern error: $($_.Exception.Message)" } }
    Log 'nav -> Uninstaller'
} else { Log 'NavUninstaller MISSING' }
$list = $null
for ($i = 0; $i -lt 18 -and -not $list; $i++) { Start-Sleep 5; $list = Find $win 'UnApplications' }
Shot '10-uninstaller.png'
$status = Find $win 'UnStatus'
Log "status: $($status.Current.Name)"
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
    Start-Sleep -Seconds 3
    Shot '20-app-selected.png'
    $uninstall = Find $win 'UnUninstall'
    if (-not $uninstall) { Log 'UnUninstall MISSING'; exit 3 }
    $uninstall.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 4
    Shot '30-confirm-dialog.png'
    AllButtons $win
    # ContentDialog primary button carries AutomationId 'PrimaryButton'
    $primary = Find $win 'PrimaryButton'
    if ($primary) { $primary.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Log 'confirmed uninstall' }
    else { Log 'dialog primary MISSING' }
    # wait for uninstall + automatic leftover scan (inventory refresh + scan)
    $status = Find $win 'UnStatus'
    for ($i = 0; $i -lt 60; $i++) { Start-Sleep 5; $null = ClickNativeInstallerDialog; $status = Find $win 'UnStatus'; $sname = $status.Current.Name; if ($i % 6 -eq 0) { Log "status t=$((5*$i))s: $sname"; ShotScreen "45-wait-$((5*$i))s.png" }; if ($sname -match 'leftover|Leftover|removed|Removed|scan|Scan') { Log "status settled: $sname"; break } }
    Shot '40-after-uninstall.png'
    Log "status after: $($status.Current.Name)"
    $summary = Find $win 'UnLeftoverSummary'
    if ($summary) { Log "leftover summary: $($summary.Current.Name)" }
    Shot '50-leftovers.png'
    $summary = Find $win 'UnLeftoverSummary'
    $list2 = Find $win 'UnApplications'
    if ($summary -and $summary.Current.Name -and $list2) {
        $items2 = $list2.FindAll([Windows.Automation.TreeScope]::Children, [Windows.Automation.Condition]::TrueCondition)
        Log "leftover items: $($items2.Count)"
        foreach ($i in $items2) { if ($i.Current.Name) { Log "  leftover: $($i.Current.Name)" } }
        foreach ($i in $items2) { try { $i.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch { } }
        Log 'selected all visible leftover items'
    } else { Log "leftover list absent; summary='$($summary.Current.Name)'" }
    $allow = Find $win 'UnAllowPersonalData'
    if ($allow -and $allow.Current.IsEnabled -and $allow.Current.Visibility -ne 'Offscreen') {
        $allow.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle(); Log 'toggled AllowPersonalData'
    }
    Shot '55-leftovers-selected.png'
    $delete = Find $win 'UnDeleteLeftovers'
    if ($delete -and $delete.Current.IsEnabled) {
        $delete.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Seconds 4
        Shot '60-delete-dialog.png'
        AllButtons $win
        $deletePrimary = Find $win 'PrimaryButton'
        if ($deletePrimary) { $deletePrimary.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Log 'confirmed delete leftovers' }
        else { Log 'delete dialog primary MISSING' }
        Start-Sleep -Seconds 8
        ShotScreen '70-uac-or-result.png'
        Shot '70-after-delete.png'
        $status = Find $win 'UnStatus'
        Log "status after delete: $($status.Current.Name)"
        Start-Sleep -Seconds 20
        ShotScreen '80-final-screen.png'
    } else { Log "UnDeleteLeftovers missing or disabled: $([bool]$delete)" }
}
Log 'UI ROUND DONE'
