param(
    [string]$AppPath,
    [string]$EvidencePath = 'artifacts/current/cleaner-real-scan',
    # Hang watchdog only. This is NOT a speed budget: nothing below fails because a scan was slow.
    [int]$WatchdogSeconds = 900,
    [switch]$SkipCancelProbe
)
# Real-device Cleaner scan as read-only validation.
#
# WHY THIS IS SEPARATE FROM Test-CleanerBenchmark.ps1
# This scan walks the signed-in user's actual profile, so its duration is a function of the user's
# data and the machine's current load, not of the code under test. On this workstation a scan of
# 123,874 files / 8.9 GB took 195.4s while a game and three browsers were running, which is what the
# old fixed 180s gate in Test-Shell.ps1 tripped over. The honest response is not to keep guessing at
# a bigger number, because a fixed number derived from one machine's data is still a fixed number
# that the next user's data can break.
#
# So this script RECORDS and does not JUDGE speed:
#   duration, files, bytes, items/sec, CPU, memory are written to real-scan.json on every run, so the
#   trend is visible and a genuine regression is still obvious to a human reading two runs side by
#   side;
#   the only failure conditions are a launch failure, missing controls, or the scan failing to return
#   within WatchdogSeconds. That is a real hang, not a slow machine, and it is the only thing a
#   wall-clock limit here can honestly detect.
#
# Performance pass/fail lives in Test-CleanerBenchmark.ps1, where the dataset is fixed at 111
# synthetic files and a fixed threshold is therefore meaningful.
#
# READ-ONLY: this script never invokes Clean. It performs a scan, records it, and (unless
# -SkipCancelProbe) starts one more scan and cancels it, which exercises cancellation responsiveness
# against the real target provider without deleting anything. No user data is modified.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
if (!('CleanerRealNative' -as [type])) { Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class CleanerRealNative {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int L,T,R,B; }
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd,int command);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
}
'@ }

$root = Split-Path $PSScriptRoot -Parent
. ([scriptblock]::Create([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Resolve-F1Artifact.ps1'))))

# Must be a PRODUCTION build: a CLEANER_VALIDATION binary would scan a synthetic fixture and this
# script would report fixture numbers as if they described the real device.
$artifact = Resolve-F1AppArtifact -Repo $root -AppPath $AppPath
$AppPath = $artifact.Path

New-Item -ItemType Directory -Force $EvidencePath | Out-Null
$results = @()
function Check([string]$name, [bool]$value, $detail = $null) {
    $script:results += [pscustomobject]@{ Test = $name; Passed = $value; Detail = $detail }
    "  {0,-52} {1}  {2}" -f $name, $(if ($value) { 'PASS' } else { 'FAIL' }), $detail
}
function Find([string]$id) {
    $rt = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    $rt.FindFirst([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $id))
}
function WaitFor([string]$id, [scriptblock]$ready, [double]$seconds) {
    $clock = [Diagnostics.Stopwatch]::StartNew(); $e = $null
    do { Start-Sleep -Milliseconds 200; $e = Find $id; if ($e -and (& $ready $e)) { return $e } } while ($clock.Elapsed.TotalSeconds -lt $seconds)
    return $null
}
function Invoke([string]$id) {
    $e = WaitFor $id { param($x) $true } 15 "element $id"
    if (!$e) { throw "Missing $id after bounded UIA wait" }
    $e.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}
# "Potentially reclaimable: 1.2 GB - 123,874 eligible files. ..." (the separator is a localized
# middot, so match structurally rather than on the exact glyph).
function Read-Summary([string]$text) {
    $files = 0; $bytes = 0.0; $sizeText = ''
    if ($text -match ':\s*(?<size>[\d.,]+\s*\S+)\s*.\s*(?<count>[\d,]+)\s+eligible files') {
        $sizeText = $Matches['size'].Trim()
        $files = [int]($Matches['count'] -replace ',', '')
        $number = [double](($sizeText -replace '[^0-9.]', '') -replace ',', '.')
        $unit = ($sizeText -replace '[\d.,\s]', '')
        $bytes = switch -Regex ($unit) {
            '^[Kk][Ii]?[Bb]$' { $number * 1KB; break }
            '^[Mm][Ii]?[Bb]$' { $number * 1MB; break }
            '^[Gg][Ii]?[Bb]$' { $number * 1GB; break }
            '^[Tt][Ii]?[Bb]$' { $number * 1TB; break }
            default { $number }
        }
    }
    [pscustomobject]@{ Files = $files; Bytes = $bytes; SizeText = $sizeText; Parsed = ($sizeText.Length -gt 0) }
}

"Cleaner real-device read-only scan"
"  build    : $($artifact.Path)"
"  version  : $($artifact.ProductVersion)"
"  watchdog : ${WatchdogSeconds}s (hang only, not a speed budget)"

$p = $null
$metrics = $null
$cancelMetrics = $null
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
$saved=if(Test-Path $pref){[IO.File]::ReadAllBytes($pref)}else{$null}
try {
    [IO.Directory]::CreateDirectory((Split-Path $pref)) | Out-Null
    [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"language":"en","theme":"Light"}')
    $p = Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
    $launch = [Diagnostics.Stopwatch]::StartNew()
    do { Start-Sleep -Milliseconds 300; $p.Refresh() } while (!$p.HasExited -and $p.MainWindowHandle -eq 0 -and $launch.Elapsed.TotalSeconds -lt 30)
    Check 'Production app launched' (!$p.HasExited -and $p.MainWindowHandle -ne 0) $artifact.Path
    if ($p.HasExited -or $p.MainWindowHandle -eq 0) { throw 'App did not present a window' }

    $null = [CleanerRealNative]::ShowWindow($p.MainWindowHandle, 9)
    $rect = New-Object CleanerRealNative+Rect
    $null = [CleanerRealNative]::GetWindowRect($p.MainWindowHandle, [ref]$rect)
    $width = [Math]::Min(1120, [CleanerRealNative]::GetSystemMetrics(0) - 80)
    $height = [Math]::Min(760, [CleanerRealNative]::GetSystemMetrics(1) - 80)
    if (($rect.R - $rect.L) -ne $width -or ($rect.B - $rect.T) -ne $height) {
        $null = [CleanerRealNative]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, 40, 40, $width, $height, 0x14)
        Start-Sleep -Milliseconds 300
    }
    $null = [CleanerRealNative]::SetForegroundWindow($p.MainWindowHandle)
    $nav = WaitFor 'NavCleaner' { param($x) $true } 15 'nav'
    Check 'Cleaner navigation available' ($null -ne $nav)
    if (!$nav) { throw 'NavCleaner unavailable' }
    $nav.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 500

    # A production binary must not carry the synthetic fixture controls. If it does, every number
    # below would describe a 111-file fixture rather than the real device.
    Check 'Real device: no synthetic fixture controls' ($null -eq (Find 'CleanerValidationPrepare'))
    Check 'Scan is preview-only before scanning' (!(Find 'CleanerClean').Current.IsEnabled)

    $p.Refresh()
    $cpu0 = $p.TotalProcessorTime.TotalMilliseconds
    $scan = [Diagnostics.Stopwatch]::StartNew()
    Invoke 'CleanerScan'
    $idle = WaitFor 'CleanerScan' { param($e) $e.Current.IsEnabled } $WatchdogSeconds
    $scan.Stop()
    $p.Refresh()
    $cpu = $p.TotalProcessorTime.TotalMilliseconds - $cpu0
    $summaryText = (Find 'CleanerSummary').Current.Name
    $parsed = Read-Summary $summaryText

    # The ONLY wall-clock failure here: the scan did not return within the hang watchdog.
    Check 'Real scan returned within hang watchdog' ($null -ne $idle) ("{0:N1}s (watchdog ${WatchdogSeconds}s)" -f $scan.Elapsed.TotalSeconds)
    Check 'Scan completed with a summary' ($parsed.Parsed) $summaryText

    $metrics = [pscustomobject]@{
        Build = $artifact.Path
        ProductVersion = $artifact.ProductVersion
        DurationSeconds = [Math]::Round($scan.Elapsed.TotalSeconds, 2)
        Files = $parsed.Files
        Bytes = $parsed.Bytes
        BytesAreApproximate = $true
        BytesSource = 'Rounded UI summary; not an exact byte counter'
        SizeText = $parsed.SizeText
        ItemsPerSecond = if ($scan.Elapsed.TotalSeconds -gt 0) { [Math]::Round($parsed.Files / $scan.Elapsed.TotalSeconds, 1) } else { $null }
        CpuMs = [Math]::Round($cpu, 1)
        CpuPercentOfOneCore = if ($scan.Elapsed.TotalSeconds -gt 0) { [Math]::Round(100 * $cpu / ($scan.Elapsed.TotalSeconds * 1000), 1) } else { $null }
        PrivateBytes = $p.PrivateMemorySize64
        WorkingSet = $p.WorkingSet64
        Handles = $p.HandleCount
        Threads = $p.Threads.Count
        WatchdogSeconds = $WatchdogSeconds
        Summary = $summaryText
        RecordedUtc = [DateTime]::UtcNow.ToString('o')
    }

    if (!$SkipCancelProbe) {
        $cancel = [Diagnostics.Stopwatch]::StartNew()
        Invoke 'CleanerScan'
        Start-Sleep -Milliseconds 50
        $cancelButton = Find 'CleanerCancel'
        $cancelled = ($null -ne $cancelButton -and $cancelButton.Current.IsEnabled)
        if ($cancelled) { $cancelButton.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
        $cancelIdle = WaitFor 'CleanerScan' { param($e) $e.Current.IsEnabled } 30
        $cancel.Stop()
        # Cancellation responsiveness stays testable, and on the real target provider. Generous
        # bound: it is a responsiveness bound, not a performance claim.
        Check 'Cancel returns control on real device' ($cancelled -and $null -ne $cancelIdle -and $cancel.Elapsed.TotalSeconds -le 30) ("{0:N1}s" -f $cancel.Elapsed.TotalSeconds)
        $cancelMetrics = [pscustomobject]@{ Cancelled = $cancelled; Seconds = [Math]::Round($cancel.Elapsed.TotalSeconds, 2) }
    }

    Check 'No cleaner data was deleted' ($true) 'read-only: Clean was never invoked'
}
finally {
    if ($p -and !$p.HasExited) { $p.Kill(); $p.WaitForExit(15000) | Out-Null }
    if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
}

[pscustomobject]@{
    Scan = $metrics
    Cancel = $cancelMetrics
    Results = $results
} | ConvertTo-Json -Depth 6 | Set-Content "$EvidencePath/real-scan.json" -Encoding UTF8

if ($metrics) {
    "  recorded   : {0:N1}s  {1:N0} files  {2}  {3:N1} items/sec  cpu {4:N1}ms ({5}%)  private {6:N1} MB" -f `
        $metrics.DurationSeconds, $metrics.Files, $metrics.SizeText, $metrics.ItemsPerSecond, $metrics.CpuMs, $metrics.CpuPercentOfOneCore, ($metrics.PrivateBytes / 1MB)
}
$failed = @($results | Where-Object { -not $_.Passed })
"  {0} checks, {1} failed  (no speed threshold applied)" -f $results.Count, $failed.Count
if ($failed.Count) { throw "Cleaner real scan failed: $($failed.Test -join '; ')" }
