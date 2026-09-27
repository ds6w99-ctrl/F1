param([string]$AppPath = 'artifacts/current/fix4-copy/publish/F1.exe',
      [string]$EvidencePath = 'artifacts/current/fix4-copy/settings-dump')

# Read-only diagnostic for the two copy-render findings on the Settings page.
#   SettingsGeneralNotice - the resx value contains the {Brand} placeholder, which
#                          LocalizationService substitutes at render time, so a literal
#                          match against the raw resx value can never succeed.
#   SessionSettings       - this is the INITIAL value of the save-status line
#                          (SettingsViewModel.SessionNotice => L[status]). Choosing a language
#                          or theme changes that status, so the line is expected to have moved on
#                          by the time the page is inspected.
# This dump prints what the page really renders, in both languages, so the two can be confirmed
# rather than assumed. Pure ASCII: Arabic is read from the resx with an explicit UTF-8 decoder.

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
$p = $null
try {
    [IO.Directory]::CreateDirectory($EvidencePath) | Out-Null
    $source = [IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
    . ([scriptblock]::Create($source.Substring(0, $source.IndexOf('$p=Start-Process')))) -AppPath $AppPath -EvidencePath $EvidencePath
    $utf8 = [Text.UTF8Encoding]::new($false)

    function Scroll-Containing([string]$id, [double]$percent) {
        $node = Find $id
        while ($node -and !$node.GetCurrentPropertyValue([Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty)) {
            $node = [Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($node)
        }
        if ($node) { $node.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, $percent); Start-Sleep -Milliseconds 250 }
    }

    $res = @{}
    foreach ($pair in @(@('en', 'Shell.resx'), @('ar', 'Shell.ar.resx'))) {
        $t = @{}
        ([xml][IO.File]::ReadAllText((Resolve-Path "src/SystemCore.App/Resources/$($pair[1])"), $utf8)).root.data |
            ForEach-Object { if ($_.name) { $t[$_.name] = [string]$_.value } }
        $res[$pair[0]] = $t
    }

    $pref = Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
    $saved = [IO.File]::ReadAllBytes($pref)
    [IO.File]::WriteAllText($pref, '{"schemaVersion":1,"language":"en","theme":"Light"}')

    $p = Start-Process (Resolve-Path $AppPath) -PassThru
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do { Start-Sleep -Milliseconds 200; $p.Refresh() } while (!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $clock.Elapsed.TotalSeconds -lt 45)
    $window = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    do { Start-Sleep -Milliseconds 150; $t = Find 'PageTitle' } while (!$t -and $clock.Elapsed.TotalSeconds -lt 30)
    $null = [ShellSmokeNative]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, 40, 40, 1120, 760, 0x14)
    Start-Sleep -Milliseconds 600

    function Dump-Settings([string]$label) {
        Navigate 'NavSettings'; Scroll-Containing 'LanguagePicker' 0; Start-Sleep -Milliseconds 500
        $texts = $window.FindAll([Windows.Automation.TreeScope]::Descendants,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,
                [Windows.Automation.ControlType]::Text))
        "--- $label ---"
        foreach ($el in $texts) {
            $n = $el.Current.Name
            if (![string]::IsNullOrWhiteSpace($n) -and $n -notmatch '^(Open |Back )') { "    $n" }
        }
    }

    "### en / Light, immediately after a fresh launch (nothing touched yet)"
    Dump-Settings 'initial en/Light'
    ""
    "### after choosing theme + language, which is the state the copy-render suite inspects"
    Navigate 'NavSettings'; Scroll-Containing 'LanguagePicker' 0
    Choose 'ThemePicker' $res.en.ThemeDark
    Start-Sleep -Milliseconds 600
    Dump-Settings 'after theme change en/Dark'
    ""
    "### Arabic"
    Navigate 'NavSettings'; Scroll-Containing 'LanguagePicker' 0
    Choose 'LanguagePicker' $res.ar.Arabic
    Start-Sleep -Milliseconds 800
    Dump-Settings 'after language change ar'
    ""
    "### expected raw resx values, for comparison"
    "  en SessionSettings        = '$($res.en.SessionSettings)'"
    "  en SettingsGeneralNotice  = '$($res.en.SettingsGeneralNotice)'"
    "  en SettingsSaved          = '$($res.en.SettingsSaved)'"
    "  ar SessionSettings        = '$($res.ar.SessionSettings)'"
    "  ar SettingsGeneralNotice  = '$($res.ar.SettingsGeneralNotice)'"
    "  ar SettingsSaved          = '$($res.ar.SettingsSaved)'"
}
finally {
    if ($p) { if (!$p.HasExited) { $null = $p.CloseMainWindow(); $null = $p.WaitForExit(15000) }; $p.Dispose() }
    if ($script:saved) { [IO.File]::WriteAllBytes($pref, $script:saved) }
    Pop-Location
}
