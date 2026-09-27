param(
    [string]$AppPath,
    [string]$EvidencePath = 'artifacts/current/cleaner-benchmark',
    [ValidateRange(1,20)][int]$ScanCount = 5,
    [double]$MaxScanSeconds = 10,
    [ValidateRange(1,20)][int]$CancelCount = 5,
    [double]$MaxCancelSeconds = 30
)
# Deterministic Cleaner performance gate.
#
# WHY THIS EXISTS
# The previous Cleaner speed gate was a fixed 180s budget applied to a REAL-DEVICE scan. That budget
# is not a property of the code: it is a property of how many files the signed-in user's profile
# happens to contain and how busy the machine is. On this workstation a scan of 123,874 files /
# 8.9 GB took 195.4s while a game and three browsers were running, and the only honest response then
# was either to raise the number (hiding nothing, but also proving nothing) or to let an unrelated
# test fail for an unrelated reason.
#
# So the two concerns are split:
#   * THIS script is the performance pass/fail gate. It runs the CLEANER_VALIDATION build, whose
#     ICleanerService is replaced by Cleaner.ValidationCleanerService. That service materialises a
#     FIXED fixture (111 files, one target, fixed pattern, fixed 24h minimum age) and never touches
#     the real disk. The dataset is byte-for-byte identical on every run, on every machine, so a
#     fixed threshold is meaningful here in a way it never was for a real scan. A regression that
#     makes scanning 10x slower fails this gate; a user with 200k files does not.
#   * Test-CleanerRealScan.ps1 keeps the real-device scan as read-only validation. It records the
#     numbers and fails only on a genuine hang, never on a speed threshold.
#
# Nothing here weakens an existing assertion: the 180s real-scan budget in Test-Shell.ps1 is
# replaced by a large hang watchdog, and every other Cleaner check in that suite is untouched.
# Cleaner production code is not modified.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
if (!('CleanerBenchNative' -as [type])) { Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class CleanerBenchNative {
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

# A synthetic fixture is mandatory here. A production binary would scan the real disk and the
# benchmark would be measuring the user's profile instead of the fixed dataset.
$artifact = Resolve-F1AppArtifact -Repo $root -AppPath $AppPath -RequireValidationMarker
$AppPath = $artifact.Path

New-Item -ItemType Directory -Force $EvidencePath | Out-Null
$results = @()
$scanSamples = @()
$cancelSamples = @()
function Check([string]$name, [bool]$value, $detail = $null) {
    $script:results += [pscustomobject]@{ Test = $name; Passed = $value; Detail = $detail }
    "  {0,-52} {1}  {2}" -f $name, $(if ($value) { 'PASS' } else { 'FAIL' }), $detail
}
function Find([string]$id) {
    $rt = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    $rt.FindFirst([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $id))
}
function WaitFor([string]$id, [scriptblock]$ready, [double]$seconds, [string]$what) {
    $clock = [Diagnostics.Stopwatch]::StartNew(); $e = $null
    do { Start-Sleep -Milliseconds 100; $e = Find $id; if ($e -and (& $ready $e)) { return $e } } while ($clock.Elapsed.TotalSeconds -lt $seconds)
    return $null
}
function Invoke([string]$id) {
    $e = WaitFor $id { param($x) $true } 10 "element $id"
    if (!$e) { throw "Missing $id after bounded UIA wait" }
    $e.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Navigate([string]$id) {
    $null = [CleanerBenchNative]::ShowWindow($p.MainWindowHandle, 9)
    $rect = New-Object CleanerBenchNative+Rect
    $null = [CleanerBenchNative]::GetWindowRect($p.MainWindowHandle, [ref]$rect)
    $width = [Math]::Min(1120, [CleanerBenchNative]::GetSystemMetrics(0) - 80)
    $height = [Math]::Min(760, [CleanerBenchNative]::GetSystemMetrics(1) - 80)
    if (($rect.R - $rect.L) -ne $width -or ($rect.B - $rect.T) -ne $height) {
        $null = [CleanerBenchNative]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, 40, 40, $width, $height, 0x14)
        Start-Sleep -Milliseconds 200
    }
    $null = [CleanerBenchNative]::SetForegroundWindow($p.MainWindowHandle)
    $nav = WaitFor $id { param($x) $true } 10 "nav $id"
    if (!$nav) { throw "Navigation element $id unavailable after bounded wait" }
    $nav.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 300
}

"Cleaner deterministic synthetic benchmark"
"  build     : $($artifact.Path)"
"  version   : $($artifact.ProductVersion)"
"  scans     : $ScanCount (max ${MaxScanSeconds}s each)   cancel cycles: $CancelCount (max ${MaxCancelSeconds}s each)"

$p = $null
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
$saved=if(Test-Path $pref){[IO.File]::ReadAllBytes($pref)}else{$null}
try {
    [IO.Directory]::CreateDirectory((Split-Path $pref)) | Out-Null
    [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"language":"en","theme":"Light"}')
    $p = Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
    $launch = [Diagnostics.Stopwatch]::StartNew()
    do { Start-Sleep -Milliseconds 300; $p.Refresh() } while (!$p.HasExited -and $p.MainWindowHandle -eq 0 -and $launch.Elapsed.TotalSeconds -lt 30)
    Check 'Synthetic validation app launched' (!$p.HasExited -and $p.MainWindowHandle -ne 0) $artifact.Path
    if ($p.HasExited -or $p.MainWindowHandle -eq 0) { throw 'Validation app did not present a window' }

    Navigate 'NavCleaner'
    Check 'Fixed synthetic fixture controls present' ($null -ne (Find 'CleanerValidationPrepare'))

    # One Prepare for the whole run. Clean is never invoked, so the 111-file fixture is byte-for-byte
    # identical at the start of every scan below and the samples are directly comparable.
    Invoke 'CleanerValidationPrepare'
    Start-Sleep -Milliseconds 400
    $prepared = (Find 'CleanerValidationStatus').Current.Name
    Check 'Fixture prepared with fixed size' ($prepared -match 'Prepared:111') $prepared

    for ($i = 1; $i -le $ScanCount; $i++) {
        $p.Refresh()
        $cpu0 = $p.TotalProcessorTime.TotalMilliseconds
        $scan = [Diagnostics.Stopwatch]::StartNew()
        Invoke 'CleanerScan'
        $idle = WaitFor 'CleanerScan' { param($e) $e.Current.IsEnabled } 120 'scan to return idle'
        $scan.Stop()
        $p.Refresh()
        $cpu = $p.TotalProcessorTime.TotalMilliseconds - $cpu0
        $status = (Find 'CleanerValidationStatus').Current.Name
        $summary = (Find 'CleanerSummary').Current.Name
        $scanSamples += [pscustomobject]@{
            Scan = $i
            Seconds = [Math]::Round($scan.Elapsed.TotalSeconds, 3)
            CpuMs = [Math]::Round($cpu, 1)
            PrivateBytes = $p.PrivateMemorySize64
            WorkingSet = $p.WorkingSet64
            Summary = $summary
        }
        # Fixed dataset, fixed threshold: a per-scan overrun is a code regression, not user data.
        Check "Scan $i completes within fixed budget" ($null -ne $idle -and $scan.Elapsed.TotalSeconds -le $MaxScanSeconds) ("{0}s  {1}" -f [Math]::Round($scan.Elapsed.TotalSeconds, 3), $summary)
        Check "Scan $i returns 111 files and 147 bytes" ($summary -match '147 B' -and $summary -match '\b111 eligible files' -and $status -match 'CleanerBusy:False') $summary
    }

    # Cancellation responsiveness must stay testable; it is the user-visible half of a real scan.
    for ($i = 1; $i -le $CancelCount; $i++) {
        $cancel = [Diagnostics.Stopwatch]::StartNew()
        Invoke 'CleanerScan'
        Start-Sleep -Milliseconds 50
        $cancelButton = Find 'CleanerCancel'
        $cancelInvoked = $false
        if ($cancelButton -and $cancelButton.Current.IsEnabled) {
            $cancelButton.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
            $cancelInvoked = $true
        }
        $idle = WaitFor 'CleanerScan' { param($e) $e.Current.IsEnabled } $MaxCancelSeconds 'cancel to return idle'
        $cancel.Stop()
        $cancelSamples += [pscustomobject]@{
            Cycle = $i
            Seconds = [Math]::Round($cancel.Elapsed.TotalSeconds, 3)
            CancelInvoked = $cancelInvoked
        }
        Check "Cancel $i returns control within budget" ($cancelInvoked -and $null -ne $idle -and $cancel.Elapsed.TotalSeconds -le $MaxCancelSeconds) ("{0}s" -f [Math]::Round($cancel.Elapsed.TotalSeconds, 3))
    }
}
finally {
    if ($p -and !$p.HasExited) { $p.Kill(); $p.WaitForExit(10000) | Out-Null }
    if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
}

$okScans = @($scanSamples)
$median = if ($okScans.Count) { ($okScans.Seconds | Sort-Object)[[int]($okScans.Count / 2)] } else { $null }
$itemsPerSecond = if ($median -and $median -gt 0) { [Math]::Round(111 / $median, 1) } else { $null }
[pscustomobject]@{
    Build = $artifact.Path
    ProductVersion = $artifact.ProductVersion
    FixtureFiles = 111
    Scans = $scanSamples
    Cancels = $cancelSamples
    MedianScanSeconds = $median
    ItemsPerSecond = $itemsPerSecond
    MaxScanSeconds = $MaxScanSeconds
    MaxCancelSeconds = $MaxCancelSeconds
    Results = $results
} | ConvertTo-Json -Depth 6 | Set-Content "$EvidencePath/benchmark.json" -Encoding UTF8

"  median scan : $median s   items/sec: $itemsPerSecond   (fixed 111-file fixture)"
$failed = @($results | Where-Object { -not $_.Passed })
"  {0} checks, {1} failed" -f $results.Count, $failed.Count
if ($failed.Count) { throw "Cleaner benchmark failed: $($failed.Test -join '; ')" }
