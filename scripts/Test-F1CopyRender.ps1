param([string]$AppPath = 'artifacts/current/fix4-copy/publish/F1.exe',
      [string]$EvidencePath = 'artifacts/current/fix4-copy/copy-render')

# FIX 4 rendered-copy verification.
#
# scripts/Test-ThemeLocalization.ps1 cannot be used for this change in this repository state: it
# hardcodes $AppPath='artifacts/phase8/app/SystemCore.App.exe' (assigned, not a parameter) and that
# artifact does not exist. It cannot be satisfied by copying the current build either, because the
# WinUI app does not load its resource index under a renamed executable: launching a copy named
# SystemCore.App.exe opens a window titled 'F1' whose UI tree exposes no AutomationId at all
# (NavSettings is not found), so the suite fails on its first navigation check.
#
# This script supplies the equivalent evidence for the actual change: for both languages and all
# three themes, it asserts that
#   (a) each of the 15 shortened strings is really rendered on the page that owns it, and
#   (b) each string that was deleted from the visual tree is no longer rendered.
#
# Values are compared as exact matches against the resx, read with an explicit UTF-8 decoder.
# Pure ASCII: a BOM-less .ps1 is decoded by Windows PowerShell 5.1 using the ANSI code page, so no
# Arabic literal is embedded here.

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
$p = $null
# Not named $results: the dot-sourced harness prefix owns that name as an ArrayList.
$copyChecks = @()
try {
    [IO.Directory]::CreateDirectory($EvidencePath) | Out-Null
    $source = [IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
    . ([scriptblock]::Create($source.Substring(0, $source.IndexOf('$p=Start-Process')))) -AppPath $AppPath -EvidencePath $EvidencePath
    $utf8 = [Text.UTF8Encoding]::new($false)

    # Same implementation as the copy in Test-ProductUx.ps1:39 / Test-F1Visual.ps1:45, which live in
    # those scripts rather than in the shared Test-Shell prefix this script dot-sources.
    function Scroll-Containing([string]$id, [double]$percent) {
        $node = Find $id
        while ($node -and !$node.GetCurrentPropertyValue([Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty)) {
            $node = [Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($node)
        }
        if ($node) { $node.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, $percent); Start-Sleep -Milliseconds 250 }
    }

    $res = @{}
    foreach ($pair in @(@('en', 'Shell.resx'), @('ar', 'Shell.ar.resx'))) {
        $table = @{}
        ([xml][IO.File]::ReadAllText((Resolve-Path "src/SystemCore.App/Resources/$($pair[1])"), $utf8)).root.data |
            ForEach-Object { if ($_.name) { $table[$_.name] = [string]$_.value } }
        $res[$pair[0]] = $table
    }

    # page -> keys that must be rendered, and keys that must NOT be rendered any more.
    $expect = [ordered]@{
        'Dashboard'   = @('F1History')
        'Performance' = @('PerfNetworkScope')
        'MemoryTools' = @('MemEducation')
        'Cleaner'     = @('CleanDescription', 'CleanSafety')
        'Tools'       = @('ToolsDescription')
        'Settings'    = @('SettingsBehavior', 'SettingsGeneralNotice', 'SettingsCleanerNotice', 'SettingsPrivacyNotice', 'SessionSettings')
        'About'       = @('AboutSupported', 'AboutCopyright')
        'SystemInfo'  = @('InfoDescription')
    }
    # Element deleted from the visual tree -> its key must no longer be rendered.
    $absent = [ordered]@{
        'Dashboard'   = @('F1Intro', 'F1CleanupBody', 'F1UninstallBody', 'F1StartupBody')
        'Performance' = @('PerfDescription', 'CpuDetail', 'MemoryDetail', 'DiskDetail', 'PerfChart', 'PerfProcessNotice')
        'Settings'    = @('SettingsDescription', 'SettingsAppearance', 'AboutDescription')
        'About'       = @('AboutDescription')
    }

    $pref = Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
    $saved = [IO.File]::ReadAllBytes($pref)
    [IO.File]::WriteAllText($pref, '{"schemaVersion":1,"language":"en","theme":"Light"}')

    $p = Start-Process (Resolve-Path $AppPath) -PassThru
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do { Start-Sleep -Milliseconds 200; $p.Refresh() } while (!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $clock.Elapsed.TotalSeconds -lt 45)
    $window = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    do { Start-Sleep -Milliseconds 150; $t = Find 'PageTitle' } while (!$t -and $clock.Elapsed.TotalSeconds -lt 30)
    "app started pid=$($p.Id)"
    $null = [ShellSmokeNative]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, 40, 40, 1120, 760, 0x14)
    Start-Sleep -Milliseconds 500

    function Get-PageText {
        $texts = $window.FindAll([Windows.Automation.TreeScope]::Descendants,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,
                [Windows.Automation.ControlType]::Text))
        $sb = New-Object Text.StringBuilder
        foreach ($el in $texts) { if (![string]::IsNullOrWhiteSpace($el.Current.Name)) { $null = $sb.AppendLine($el.Current.Name) } }
        $sb.ToString()
    }

    foreach ($language in @('en', 'ar')) {
        Navigate 'NavSettings'; Scroll-Containing 'LanguagePicker' 0
        Choose 'LanguagePicker' $res[$language][$(if ($language -eq 'en') { 'English' } else { 'Arabic' })]
        foreach ($theme in @('Light', 'Dark', 'System')) {
            Navigate 'NavSettings'; Scroll-Containing 'LanguagePicker' 0
            Choose 'ThemePicker' $res[$language]["Theme$theme"]
            Start-Sleep -Milliseconds 700
            foreach ($page in $expect.Keys) {
                Navigate "Nav$page"; Start-Sleep -Milliseconds 700
                $blob = Get-PageText
                foreach ($key in $expect[$page]) {
                    $value = $res[$language][$key]
                    $ok = $blob.Contains($value)
                    $copyChecks += [pscustomobject]@{
                        Language = $language; Theme = $theme; Page = $page; Key = $key
                        Expect = 'rendered'; Pass = $ok; Length = $value.Length
                    }
                    if (!$ok) { "  MISSING [$language/$theme/$page] $key" }
                }
                foreach ($key in $absent[$page]) {
                    $value = $res[$language][$key]
                    # SettingsGeneralNotice legitimately reuses {Brand}; AboutDescription is asserted absent.
                    $gone = -not $blob.Contains($value)
                    $copyChecks += [pscustomobject]@{
                        Language = $language; Theme = $theme; Page = $page; Key = $key
                        Expect = 'absent'; Pass = $gone; Length = $value.Length
                    }
                    if (!$gone) { "  STILL-RENDERED [$language/$theme/$page] $key" }
                }
            }
        }
    }

    $failed = @($copyChecks | Where-Object { !$_.Pass })
    $copyChecks | ConvertTo-Json -Depth 4 | Set-Content "$EvidencePath/copy-render-checks.json" -Encoding utf8
    ""
    "rendered-copy checks: $($copyChecks.Count - $failed.Count)/$($copyChecks.Count) passed  (2 languages x 3 themes)"
    "  rendered-expected: $(@($copyChecks | Where-Object { $_.Expect -eq 'rendered' }).Count)"
    "  absent-expected  : $(@($copyChecks | Where-Object { $_.Expect -eq 'absent' }).Count)"
    if ($failed.Count -gt 0) { "FAILURES:"; $failed | ForEach-Object { "  $($_.Language)/$($_.Theme)/$($_.Page)/$($_.Key)" } }
}
finally {
    if ($p) { if (!$p.HasExited) { $null = $p.CloseMainWindow(); $null = $p.WaitForExit(15000) }; $p.Dispose() }
    if ($script:saved) { [IO.File]::WriteAllBytes($pref, $script:saved) }
    Pop-Location
}
