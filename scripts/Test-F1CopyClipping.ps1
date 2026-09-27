param([string]$AppPath = 'artifacts/current/fix4-copy/publish/F1.exe',
      [string]$EvidencePath = 'artifacts/current/fix4-copy/clipping')

# FIX 4 clipping probe.
#
# Purpose: the screenshots exist as evidence, but this model has no image input, so visual review is
# not available here. This probe supplies the machine-checkable half of the "no text clipping after
# shortening" requirement using UI Automation geometry:
#
#   1. ellipsis  - a Text element whose rendered name contains U+2026 is being trimmed by the
#                  layout engine. That is real truncation.
#   2. overflow  - a Text element whose rect is not contained in its parent's rect is being drawn
#                  outside its container (clipped or overlapping).
#   3. offscreen - a Text element scrolled out of its ScrollViewer. Legitimate for long lists, so it
#                  is reported as a count, not a defect.
#
# Deliberately pure ASCII: Windows PowerShell 5.1 decodes a BOM-less .ps1 with the ANSI code page,
# so any Arabic literal in this file would be corrupted. Arabic is read from the resx with an
# explicit UTF-8 decoder instead.

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
$p = $null
try {
    [IO.Directory]::CreateDirectory($EvidencePath) | Out-Null
    $source = [IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
    . ([scriptblock]::Create($source.Substring(0, $source.IndexOf('$p=Start-Process')))) -AppPath $AppPath -EvidencePath $EvidencePath

    $utf8 = [Text.UTF8Encoding]::new($false)
    $table = @{}
    ([xml][IO.File]::ReadAllText((Resolve-Path 'src/SystemCore.App/Resources/Shell.ar.resx'), $utf8)).root.data |
        ForEach-Object { $table[$_.name] = [string]$_.value }

    $pref = Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
    [IO.Directory]::CreateDirectory((Split-Path $pref)) | Out-Null
    # Arabic + Dark: the densest combination, and the direction that is easiest to break.
    [IO.File]::WriteAllText($pref, '{"schemaVersion":1,"theme":"Dark","language":"ar"}')

    $p = Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do { Start-Sleep -Milliseconds 200; $p.Refresh() } while (!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $clock.Elapsed.TotalSeconds -lt 30)
    $window = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    do { Start-Sleep -Milliseconds 150; $title = Find 'PageTitle' } while (!$title -and $clock.Elapsed.TotalSeconds -lt 30)
    $clock.Restart()
    do { Start-Sleep -Milliseconds 150; $t = (Find 'PageTitle').Current.Name } while ([string]::IsNullOrWhiteSpace($t) -and $clock.Elapsed.TotalSeconds -lt 20)
    "app started: title='$t'"

    $walker = [Windows.Automation.TreeWalker]::ControlViewWalker
    $pages = @('Dashboard', 'Cleaner', 'Uninstaller', 'Performance', 'SystemInfo', 'MemoryTools', 'Startup', 'Tools', 'Settings', 'About')
    $report = @()

    foreach ($page in $pages) {
        Navigate "Nav$page"
        Start-Sleep -Milliseconds 600
        $wr = $window.Current.BoundingRectangle
        $texts = $window.FindAll([Windows.Automation.TreeScope]::Descendants,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,
                [Windows.Automation.ControlType]::Text))

        $ellipsis = @(); $overflow = @(); $offscreen = 0; $chars = 0; $empty = 0
        foreach ($el in $texts) {
            $name = [string]$el.Current.Name
            if ([string]::IsNullOrWhiteSpace($name)) { $empty++; continue }
            $chars += $name.Length
            if ($el.Current.IsOffscreen) { $offscreen++; continue }
            $r = $el.Current.BoundingRectangle
            if ($r.Width -le 0 -or $r.Height -le 0) { $empty++; continue }
            if ($name.Contains([char]0x2026) -or $name.Contains('...')) { $ellipsis += $name }

            # Containment inside the window.
            if ($r.Left -lt ($wr.Left - 1) -or $r.Top -lt ($wr.Top - 1) -or
                $r.Right -gt ($wr.Right + 1) -or $r.Bottom -gt ($wr.Bottom + 1)) {
                $overflow += "OUTSIDE-WINDOW: '$name'"
            }
            # Containment inside the immediate parent.
            $parent = $walker.GetParent($el)
            if ($parent) {
                try {
                    $pr = $parent.Current.BoundingRectangle
                    if ($pr.Width -gt 0 -and $pr.Height -gt 0) {
                        if ($r.Left -lt ($pr.Left - 1) -or $r.Top -lt ($pr.Top - 1) -or
                            $r.Right -gt ($pr.Right + 1) -or $r.Bottom -gt ($pr.Bottom + 1)) {
                            $overflow += "OUTSIDE-PARENT($($parent.Current.ControlType.ProgrammaticName -replace 'ControlType\.','')): '$name'"
                        }
                    }
                } catch { }
            }
        }
        $report += [pscustomobject]@{
            Page = $page
            TextElements = $texts.Count
            VisibleChars = $chars
            EllipsisTrimmed = $ellipsis
            Overflow = $overflow
            Offscreen = $offscreen
            EmptyOrZeroSized = $empty
        }
        "  {0,-14} texts={1,-4} chars={2,-6} ellipsis={3,-3} overflow={4,-3} offscreen={5}" -f `
            $page, $texts.Count, $chars, $ellipsis.Count, $overflow.Count, $offscreen
        foreach ($o in $overflow) { "      $o" }
        foreach ($e in $ellipsis) { "      TRIMMED: $e" }
    }

    $report | ConvertTo-Json -Depth 5 | Set-Content "$EvidencePath/clipping-report.json" -Encoding utf8
    # Measure-Object cannot take a script-block property, so total the arrays directly.
    $totalEllipsis = 0; $totalOverflow = 0
    foreach ($row in $report) { $totalEllipsis += $row.EllipsisTrimmed.Count; $totalOverflow += $row.Overflow.Count }
    ""
    "TOTAL ellipsis-trimmed=$totalEllipsis  overflow=$totalOverflow"
    if ($totalEllipsis -eq 0 -and $totalOverflow -eq 0) { "CLIPPING PROBE: CLEAN" } else { "CLIPPING PROBE: FINDINGS ABOVE" }
}
finally {
    if ($p) { if (!$p.HasExited) { $null = $p.CloseMainWindow(); $null = $p.WaitForExit(15000) }; $p.Dispose() }
    Pop-Location
}
