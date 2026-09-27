param([string]$AppPath = 'artifacts/current/fix4-copy/publish/F1.exe',
      [string]$EvidencePath = 'artifacts/current/fix4-copy/cleaner-diag')

# Read-only diagnostic: reproduce the 'Cleaner real scan completes' step from Test-Shell.ps1 on its
# own and report the live state, so a failure can be attributed rather than guessed at.
# Pure ASCII by design; the Arabic is not needed here.

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
$p = $null
try {
    [IO.Directory]::CreateDirectory($EvidencePath) | Out-Null
    $source = [IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
    . ([scriptblock]::Create($source.Substring(0, $source.IndexOf('$p=Start-Process')))) -AppPath $AppPath -EvidencePath $EvidencePath

    $pref = Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
    [IO.Directory]::CreateDirectory((Split-Path $pref)) | Out-Null
    [IO.File]::WriteAllText($pref, '{"schemaVersion":1,"theme":"Light","language":"en"}')

    $p = Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do { Start-Sleep -Milliseconds 200; $p.Refresh() } while (!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $clock.Elapsed.TotalSeconds -lt 30)
    $window = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    do { Start-Sleep -Milliseconds 150; $t = Find 'PageTitle' } while (!$t -and $clock.Elapsed.TotalSeconds -lt 30)
    "app started pid=$($p.Id)"

    Navigate 'NavCleaner'
    Start-Sleep -Milliseconds 500
    "initial summary = $((Find 'CleanerSummary').Current.Name)"
    "initial scanEnabled = $((Find 'CleanerScan').Current.IsEnabled)"

    # Mirror Test-Shell.ps1's Cleaner block exactly: scan -> cancel -> rescan. The cancel step is
    # what Test-Shell does before the failing check, and it is not exercised by a single scan.
    "`n--- step 1: invoke scan, then cancel (Test-Shell sequence) ---"
    (Find 'CleanerScan').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 400
    "after invoke: scanEnabled=$((Find 'CleanerScan').Current.IsEnabled) summary='$((Find 'CleanerSummary').Current.Name)'"

    (Find 'CleanerCancel').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    $c1 = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Find 'CleanerScan').Current.IsEnabled -and $c1.Elapsed.TotalSeconds -lt 30) { Start-Sleep -Milliseconds 200 }
    "cancel returned control after $([Math]::Round($c1.Elapsed.TotalSeconds,1))s : scanEnabled=$((Find 'CleanerScan').Current.IsEnabled) summary='$((Find 'CleanerSummary').Current.Name)'"

    "`n--- step 2: rescan (this is the step Test-Shell allows 180s for) ---"
    (Find 'CleanerScan').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $last = ''
    while ($sw.Elapsed.TotalSeconds -lt 240) {
        Start-Sleep -Seconds 2
        $scanEnabled = (Find 'CleanerScan').Current.IsEnabled
        $summary = ''
        try { $summary = (Find 'CleanerSummary').Current.Name } catch { $summary = '<unreadable>' }
        if ($summary -ne $last) {
            "  t={0,6:N0}s scanEnabled={1,-5} summary='{2}'" -f $sw.Elapsed.TotalSeconds, $scanEnabled, $summary
            $last = $summary
        }
        if ($scanEnabled) {
            ""
            "RESCAN COMPLETED after $([Math]::Round($sw.Elapsed.TotalSeconds,1))s"
            "final summary = $summary"
            $cats = (Find 'CleanerCategories').FindAll([Windows.Automation.TreeScope]::Descendants,
                [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [Windows.Automation.ControlType]::CheckBox))
            "category checkboxes = $($cats.Count)"
            "reclaimable check  = $(($summary -match 'Potentially reclaimable'))"
            "Test-Shell verdict: completes=$true categories=$($cats.Count -gt 0) reclaimable=$(($summary -match 'Potentially reclaimable'))"
            break
        }
    }
    if ($sw.Elapsed.TotalSeconds -ge 240) {
        ""
        "RESCAN DID NOT COMPLETE within 240s (Test-Shell allows 180s) -> Test-Shell 'Cleaner real scan completes' FAILS"
        "last summary = $last"
    }
}
finally {
    if ($p) { if (!$p.HasExited) { $null = $p.CloseMainWindow(); $null = $p.WaitForExit(15000) }; $p.Dispose() }
    Pop-Location
}
