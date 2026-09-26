param(
    [Parameter(Mandatory=$true)][string]$AppPath,
    [Parameter(Mandatory=$true)][string]$EvidencePath
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
if (!('ProbeWindowNative' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ProbeWindowNative {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
}
'@
}
$AppPath = (Resolve-Path -LiteralPath $AppPath).Path
$EvidencePath = [IO.Path]::GetFullPath($EvidencePath)
New-Item -ItemType Directory -Force $EvidencePath | Out-Null
$script:checks = [Collections.Generic.List[object]]::new()
$script:process = $null
$script:window = $null
function Record([string]$test, [bool]$passed, $details) {
    $script:checks.Add([pscustomobject]@{Test=$test;Result=$(if($passed){'PASS'}else{'FAIL'});Details=$details})
    if (!$passed) { throw "Failed: $test" }
}
function Find-Id([string]$id) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $script:window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Get-Text {
    $all = $script:window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    return (($all | ForEach-Object { $_.Current.Name }) -join "`n")
}
function Start-Probe([int]$run) {
    $script:process = Start-Process -FilePath $AppPath -PassThru -RedirectStandardError (Join-Path $EvidencePath "stderr-$run.txt")
    $ready = [Threading.SpinWait]::SpinUntil([Func[bool]]{
        $script:process.Refresh()
        if ($script:process.HasExited) { return $true }
        if ($script:process.MainWindowHandle -eq [IntPtr]::Zero) { return $false }
        $script:window = [System.Windows.Automation.AutomationElement]::FromHandle($script:process.MainWindowHandle)
        return $null -ne (Find-Id 'ProbeLanguage')
    }, 20000)
    Record "Launch $run creates controls" ($ready -and !$script:process.HasExited -and $null -ne $script:window) $script:process.Id
}
function Select-Combo([string]$id, [string]$name) {
    $combo = Find-Id $id
    $pattern = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $pattern.Expand()
    $script:item = $null
    $found = [Threading.SpinWait]::SpinUntil([Func[bool]]{
        $condition = [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name),
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::IsSelectionItemPatternAvailableProperty, $true))
        $script:item = $script:window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        return $null -ne $script:item
    }, 3000)
    Record "Find $id item $name" $found $name
    $script:item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $pattern.Collapse()
}
function Assert-Text([string]$test, [string]$expected) {
    $observed = [Threading.SpinWait]::SpinUntil([Func[bool]]{ (Get-Text).Contains($expected) }, 3000)
    Record $test $observed $expected
}
function Close-Probe([int]$run) {
    $sent = $script:process.CloseMainWindow()
    $ended = $script:process.WaitForExit(10000)
    Record "Close $run exits" ($sent -and $ended) @{Requested=$sent;Exited=$ended}
    $script:process.Dispose()
    $script:process = $null
}
$exitCode = 0
try {
    Start-Probe 1
    $dpi = [ProbeWindowNative]::GetDpiForWindow($script:process.MainWindowHandle)
    $before = Get-Text
    Assert-Text 'English resources' 'Technical eligibility'
    foreach($theme in @('Light','Dark')) {
        Select-Combo 'ProbeTheme' $theme
        Assert-Text "Actual WinUI theme $theme" "Theme: $theme"
    }
    $tab = Find-Id 'DeploymentTab'
    $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Record 'Deployment tab is selected' $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected $tab.Current.Name
    Assert-Text 'Deployment tab navigation' 'Self-contained x64 publication'
    Select-Combo 'ProbeLanguage' 'العربية'
    Assert-Text 'Arabic deployment resources' 'حزمة x64'
    $tab = Find-Id 'CompatibilityTab'
    $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Assert-Text 'Arabic compatibility resources' 'الأهلية التقنية'
    Assert-Text 'RTL property' 'FlowDirection: RightToLeft'
    Record 'Arabic tab accessible name' ((Find-Id 'CompatibilityTab').Current.Name -eq 'التوافق') (Find-Id 'CompatibilityTab').Current.Name
    Select-Combo 'ProbeTheme' 'فاتح'
    Assert-Text 'Arabic Light theme' 'Theme: Light'
    Select-Combo 'ProbeTheme' 'داكن'
    Assert-Text 'Arabic Dark theme' 'Theme: Dark'
    Select-Combo 'ProbeLanguage' 'English'
    Assert-Text 'LTR restored' 'FlowDirection: LeftToRight'
    Select-Combo 'ProbeTheme' 'System'
    Assert-Text 'System selection keeps usable content' 'Technical eligibility'
    $after = Get-Text
    $focus = @()
    $hwnd = $script:process.MainWindowHandle
    if ([ProbeWindowNative]::SetForegroundWindow($hwnd) -and [ProbeWindowNative]::GetForegroundWindow() -eq $hwnd) {
        (Find-Id 'ProbeLanguage').SetFocus()
        for ($i=0; $i -lt 8; $i++) {
            if ([ProbeWindowNative]::GetForegroundWindow() -ne $hwnd) { throw 'Lost foreground; no further keyboard input sent.' }
            [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
            $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
            Record "Tab $i stays in probe" ($focused.Current.ProcessId -eq $script:process.Id) $focused.Current.Name
            $focus += [pscustomobject]@{Name=$focused.Current.Name;Id=$focused.Current.AutomationId;Type=$focused.Current.ControlType.ProgrammaticName}
        }
        $expected = 'ProbeTheme,CompatibilityTab,ProbeRefresh,ProbeLanguage,ProbeTheme,CompatibilityTab,ProbeRefresh,ProbeLanguage'
        Record 'Tab order repeats logical sequence' (($focus.Id -join ',') -eq $expected) ($focus.Id -join ',')
        (Find-Id 'CompatibilityTab').SetFocus()
        [System.Windows.Forms.SendKeys]::SendWait('{RIGHT}')
        $tabFocused = [System.Windows.Automation.AutomationElement]::FocusedElement
        Record 'Keyboard arrow focuses Deployment tab' ($tabFocused.Current.AutomationId -eq 'DeploymentTab') $tabFocused.Current.AutomationId
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        $selected = [Threading.SpinWait]::SpinUntil([Func[bool]]{
            (Find-Id 'DeploymentTab').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected
        }, 3000)
        Record 'Keyboard Enter selects Deployment tab' $selected (Find-Id 'DeploymentTab').Current.Name
        [System.Windows.Forms.SendKeys]::SendWait('{LEFT}{ENTER}')
        Assert-Text 'Keyboard restores Compatibility content' 'Technical eligibility'
        (Find-Id 'ProbeLanguage').SetFocus()
        [System.Windows.Forms.SendKeys]::SendWait('%{DOWN}{HOME}{DOWN}{ENTER}')
        Assert-Text 'Keyboard-only Arabic selection' 'الأهلية التقنية'
    } else {
        $script:checks.Add([pscustomobject]@{Test='Keyboard-only traversal';Result='NOT TESTED';Details='Could not securely target probe foreground.'})
    }
    Close-Probe 1
    Start-Probe 2
    Assert-Text 'Relaunch default language' 'Technical eligibility'
    Close-Probe 2
} catch {
    $exitCode = 1
    $script:checks.Add([pscustomobject]@{Test='Harness exception';Result='FAIL';Details=$_.Exception.Message})
} finally {
    if ($null -ne $script:process) {
        if (!$script:process.HasExited) { $null=$script:process.CloseMainWindow(); $null=$script:process.WaitForExit(5000) }
        $script:process.Dispose()
    }
    [ordered]@{
        Scope='Actual current desktop UI Automation. Does not certify visual rendering, Narrator, clean machines, restart, installation or UAC.'
        Utc=[DateTime]::UtcNow.ToString('o');OS=[Environment]::OSVersion.Version.ToString()
        AppSHA256=(Get-FileHash $AppPath -Algorithm SHA256).Hash
        AppAssemblySHA256=(Get-FileHash ([IO.Path]::ChangeExtension($AppPath,'.dll')) -Algorithm SHA256).Hash
        ResourceIndexSHA256=(Get-FileHash (Join-Path ([IO.Path]::GetDirectoryName($AppPath)) 'SystemCore.App.pri') -Algorithm SHA256).Hash
        Dpi=$dpi;InitialText=$before;FinalText=$after;TabFocus=$focus;Tests=@($script:checks.ToArray())
    } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $EvidencePath 'ui-results.json') -Encoding UTF8
}
if ($exitCode -ne 0) { throw "UI probe failed; see $EvidencePath/ui-results.json" }
