param([string]$AppPath = 'artifacts/f1-ui/publish/F1.exe',
      [string]$EvidencePath = 'artifacts/current/fix4-copy/clickwalk')

# FIX 4 native click-walk.
#
# This reproduces the assertion made by scripts/Test-F1NativeCalibration.ps1 line 34
# ('Native click selects real Performance page') for both languages, because that script could not run
# in this environment: it waits a fixed Start-Sleep -Seconds 6 and then reads $p.MainWindowHandle
# with no polling, so it throws "hwnd cannot be IntPtr.Zero" whenever the machine is loaded (a game
# plus browsers were running). Every other script in this repo polls for the handle instead.
#
# The only difference here is that the launch uses a bounded poll rather than a fixed sleep. No
# assertion from the original script is removed, relaxed or reordered: the click is still a real
# SetCursorPos + mouse_event at a coordinate derived from live UIA bounds, and the result is still
# compared against the localized resx value.
#
# Pure ASCII by design: Windows PowerShell 5.1 decodes a BOM-less .ps1 with the ANSI code page, so
# Arabic is read from the resx with an explicit UTF-8 decoder instead of being embedded as a literal.

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
$p = $null
# NB: deliberately NOT named $results. The dot-sourced harness prefix owns $results as an ArrayList
# and its Check function calls $results.Add(); rebinding that name to a fixed-size array here would
# break every later Check call in this script.
$clickResults = @()
try {
    [IO.Directory]::CreateDirectory($EvidencePath) | Out-Null
    $source = [IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
    . ([scriptblock]::Create($source.Substring(0, $source.IndexOf('$p=Start-Process')))) -AppPath $AppPath -EvidencePath $EvidencePath

    Add-Type @'
using System;using System.Runtime.InteropServices;
public static class F1ClickWalkMouse {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
}
'@

    $utf8 = [Text.UTF8Encoding]::new($false)
    $pref = Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
    $saved = [IO.File]::ReadAllBytes($pref)
    [IO.File]::WriteAllText($pref, '{"schemaVersion":1,"language":"en","theme":"Light"}')

    $p = Start-Process (Resolve-Path $AppPath) -PassThru
    # Poll for the main window instead of a fixed sleep: strictly more robust, no assertion change.
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do { Start-Sleep -Milliseconds 200; $p.Refresh() } while (!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $clock.Elapsed.TotalSeconds -lt 45)
    if ($p.MainWindowHandle -eq [IntPtr]::Zero) { throw 'app never produced a main window handle' }
    $window = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    do { Start-Sleep -Milliseconds 150; $t = Find 'PageTitle' } while (!$t -and $clock.Elapsed.TotalSeconds -lt 30)
    "app started pid=$($p.Id) after $([Math]::Round($clock.Elapsed.TotalSeconds,1))s"

    $null = [ShellSmokeNative]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, 40, 40, 1120, 760, 0x14)
    Start-Sleep -Milliseconds 500

    foreach ($language in @('en', 'ar')) {
        $resx = if ($language -eq 'en') { 'Shell.resx' } else { 'Shell.ar.resx' }
        $resources = @{}
        ([xml][IO.File]::ReadAllText((Resolve-Path "src/SystemCore.App/Resources/$resx"), $utf8)).root.data |
            ForEach-Object { if ($_.name) { $resources[$_.name] = [string]$_.value } }

        Navigate 'NavSettings'
        Choose 'LanguagePicker' $resources[$(if ($language -eq 'en') { 'English' } else { 'Arabic' })]
        Navigate 'NavDashboard'

        $node = Find 'NavPerformance'
        $child = Find 'NavMemoryTools'
        $bounds = $node.Current.BoundingRectangle
        $childBounds = $child.Current.BoundingRectangle

        # Same coordinate derivation as the harness: the band between a parent nav header and its
        # first expanded child, which is where the clickable parent row actually sits.
        $rect = New-Object ShellSmokeNative+Rect
        $null = [ShellSmokeNative]::GetWindowRect($p.MainWindowHandle, [ref]$rect)
        $x = [int]($bounds.X + $bounds.Width / 2 - $rect.Left)
        $y = [int](($bounds.Top + $childBounds.Top) / 2 - $rect.Top)

        $null = [ShellSmokeNative]::SetForegroundWindow($p.MainWindowHandle)
        $null = [F1ClickWalkMouse]::SetCursorPos(($x + $rect.Left), ($y + $rect.Top))
        [F1ClickWalkMouse]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
        [F1ClickWalkMouse]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 800

        $actual = (Find 'PageTitle').Current.Name
        $expected = $resources.NavPerformance
        $ok = ($actual -eq $expected)
        $clickResults += [pscustomobject]@{ Language = $language; Check = 'Native click selects real Performance page'; Pass = $ok; Detail = @{ X = $x; Y = $y; Expected = $expected; Actual = $actual } }
        "  {0}  click at ({1},{2})  expected='{3}'  actual='{4}'" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $x, $y, $expected, $actual
        Save-WindowPicture "$language-performance-native"
    }

    $failed = @($clickResults | Where-Object { !$_.Pass }).Count
    $clickResults | ConvertTo-Json -Depth 5 | Set-Content "$EvidencePath/click-checks.json" -Encoding utf8
    ""
    "click-walk: $($clickResults.Count - $failed)/$($clickResults.Count) passed"
}
finally {
    if ($p) { if (!$p.HasExited) { $null = $p.CloseMainWindow(); $null = $p.WaitForExit(15000) }; $p.Dispose() }
    if ($script:saved) { [IO.File]::WriteAllBytes($pref, $script:saved) }
    Pop-Location
}
