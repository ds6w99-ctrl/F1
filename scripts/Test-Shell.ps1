param([string]$AppPath, [string]$EvidencePath, [switch]$CleanerExecutor,
      # Hang watchdog for the real-device Cleaner scan, NOT a speed budget. See the note at the scan.
      [int]$CleanerScanWatchdogSeconds = 900)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
if (!('ShellSmokeNative' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ShellSmokeNative {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle, out Rect rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr handle, IntPtr dc, uint flags);
}
'@
}
$results=[Collections.Generic.List[object]]::new()
New-Item -ItemType Directory -Force $EvidencePath | Out-Null
function Find([string]$id) {
    $window.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$id))
}
function Save-WindowPicture([string]$name) {
    $null=[ShellSmokeNative]::ShowWindow($p.MainWindowHandle,9)
    $null=[ShellSmokeNative]::SetForegroundWindow($p.MainWindowHandle)
    Start-Sleep -Milliseconds 400
    $rect=New-Object ShellSmokeNative+Rect
    $null=[ShellSmokeNative]::GetWindowRect($p.MainWindowHandle,[ref]$rect)
    $image=New-Object Drawing.Bitmap(($rect.Right-$rect.Left),($rect.Bottom-$rect.Top))
    $graphics=[Drawing.Graphics]::FromImage($image)
    $dc=$graphics.GetHdc()
    try { $captured=[ShellSmokeNative]::PrintWindow($p.MainWindowHandle,$dc,2) } finally { $graphics.ReleaseHdc($dc) }
    # WinUI's composition surface can be blank in PrintWindow. Capture only this
    # app's foreground rectangle for visual evidence when the desktop is available.
    if([ShellSmokeNative]::GetForegroundWindow() -eq $p.MainWindowHandle){
        $graphics.CopyFromScreen($rect.Left,$rect.Top,0,0,$image.Size,[Drawing.CopyPixelOperation]::SourceCopy)
        $captured=$true
    }
    try { if($captured){$image.Save([IO.Path]::GetFullPath("$EvidencePath/$name.png"),[Drawing.Imaging.ImageFormat]::Png)} }
    finally { $graphics.Dispose();$image.Dispose() }
}
function Check([string]$name,[bool]$ok,$detail) {
    $results.Add([pscustomobject]@{Test=$name;Passed=$ok;Detail=$detail})
    if (!$ok) { throw $name }
}
function Navigate([string]$id) {
    $null=[ShellSmokeNative]::ShowWindow($p.MainWindowHandle,9)
    $area=[Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $null=[ShellSmokeNative]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,$area.Left+40,$area.Top+40,[Math]::Min(1120,$area.Width-80),[Math]::Min(760,$area.Height-80),0x14)
    $null=[ShellSmokeNative]::SetForegroundWindow($p.MainWindowHandle)
    Start-Sleep -Milliseconds 200
    $item=Find $id
    if(!$item){
        $toggle=Find 'TogglePaneButton'
        if($toggle){$toggle.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();Start-Sleep -Milliseconds 300}
        $item=Find $id
    }
    Check "Navigation target $id available" ($null -ne $item) $null
    $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 400
}
function Choose([string]$id,[string]$name) {
    $combo=Find $id
    $null=[ShellSmokeNative]::SetForegroundWindow($p.MainWindowHandle)
    $combo.SetFocus()
    Start-Sleep -Milliseconds 200
    $expand=$combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    $condition=[Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$name),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::IsSelectionItemPatternAvailableProperty,$true))
    $choiceWait=[Diagnostics.Stopwatch]::StartNew()
    do {
        Start-Sleep -Milliseconds 100
        $item=$window.FindFirst([Windows.Automation.TreeScope]::Descendants,$condition)
    }while(!$item -and $choiceWait.Elapsed.TotalSeconds -lt 5)
    Check "Popup choice $id $name becomes available" ($null -ne $item) $choiceWait.Elapsed.TotalSeconds
    $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $expand.Collapse()
    Start-Sleep -Milliseconds 300
    Check "Select $id $name" ($null -ne $item) $name
}
function Wait-Info([string]$overview) {
    $clock=[Diagnostics.Stopwatch]::StartNew()
    $found=$false
    $deviceLabels=@('Shell.resx','Shell.ar.resx'|ForEach-Object { (([xml][IO.File]::ReadAllText((Resolve-Path "src/SystemCore.App/Resources/$_"))).root.data|Where-Object name -eq 'InfoDevice').value })
    do {
        $match=$window.FindFirst([Windows.Automation.TreeScope]::Descendants,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$overview))
        # ItemsControl has no UIA control-view peer. Inspect its actual rendered row instead.
        $device=$window.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.OrCondition]::new(
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$deviceLabels[0]),
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$deviceLabels[1])))
        if($null -ne $match -and $null -ne $device -and (Find 'InfoRefresh').Current.IsEnabled) { $found=$true; break }
        Start-Sleep -Milliseconds 200
    } while($clock.Elapsed.TotalSeconds -lt 30)
    Check 'System information populated' $found @{ElapsedMs=$clock.Elapsed.TotalMilliseconds}
}
function Wait-InventoryButton([string]$id,[int]$seconds=120) {
    $clock=[Diagnostics.Stopwatch]::StartNew()
    do {
        $control=Find $id
        if($control -and $control.Current.IsEnabled){return}
        Start-Sleep -Milliseconds 100
    }while($clock.Elapsed.TotalSeconds -lt $seconds)
    throw "Inventory did not return control: $id"
}
function Wait-AppForeground {
    $clock=[Diagnostics.Stopwatch]::StartNew()
    $activator=New-Object -ComObject WScript.Shell
    try {
        do {
            if($p.HasExited){throw 'Capture process exited'}
            $null=[ShellSmokeNative]::ShowWindow($p.MainWindowHandle,9)
            $null=[ShellSmokeNative]::SetForegroundWindow($p.MainWindowHandle)
            if([ShellSmokeNative]::GetForegroundWindow() -eq $p.MainWindowHandle){return}
            # Target only the owned process; Windows can reject SetForegroundWindow
            # when the controller itself no longer owns the last input event.
            $null=$activator.AppActivate($p.Id)
            Start-Sleep -Milliseconds 100
        }while($clock.Elapsed.TotalSeconds -lt 5)
    }finally{$null=[Runtime.InteropServices.Marshal]::FinalReleaseComObject($activator)}
    throw 'Foreground F1 desktop capture required'
}
function Prepare-ShellLaunch {
    $path=(Resolve-Path $AppPath).Path
    $script:shellPreferences=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
    $script:shellSavedPreferences=if(Test-Path $script:shellPreferences){[IO.File]::ReadAllBytes($script:shellPreferences)}else{$null}
    if($null -ne $script:shellSavedPreferences){[IO.File]::WriteAllBytes([IO.Path]::GetFullPath("$EvidencePath/original-preferences.bin"),$script:shellSavedPreferences)}
    [IO.Directory]::CreateDirectory((Split-Path $script:shellPreferences))|Out-Null
    [IO.File]::WriteAllText($script:shellPreferences,'{"schemaVersion":1,"theme":"Light","language":"en"}')
    return $path
}
function Invoke-ForegroundCapture([scriptblock]$captureAction,[string]$name) {
    for($attempt=1;$attempt -le 3;$attempt++){
        Wait-AppForeground
        try { & $captureAction $name; return }
        catch {
            # The original guard runs after its compositor delay. A theme popup can
            # take focus during that delay; retry only this exact pre-capture failure.
            if($_.Exception.Message -ne 'Foreground F1 desktop capture required' -or $attempt -eq 3){throw}
            "Foreground retry $attempt for $name"|Add-Content "$EvidencePath/foreground-retries.log" -Encoding utf8
        }
    }
}
$p=Start-Process (Prepare-ShellLaunch) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
try {
    $launch=[Diagnostics.Stopwatch]::StartNew()
    do { Start-Sleep -Milliseconds 300; $p.Refresh() } while(!$p.HasExited -and $p.MainWindowHandle -eq 0 -and $launch.Elapsed.TotalSeconds -lt 30)
    Check 'Actual launch' (!$p.HasExited -and $p.MainWindowHandle -ne 0) $p.Id
    $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    Start-Sleep -Seconds 6
    foreach($id in @('MetricCpu','MetricMemory','MetricDisk','MetricFree','MetricApps','MetricStartup','WindowsVersion','DeviceName')) {
        $e=Find $id
        Check $id ($null -ne $e -and $e.Current.Name -notmatch 'N/A|Loading|Collecting') $e.Current.Name
    }
    Check 'Junk not scanned before Cleaner activity' ((Find 'JunkStatus').Current.Name -eq 'Not scanned') (Find 'JunkStatus').Current.Name
    foreach($page in @('Cleaner','Uninstaller','Performance','SystemInfo','Startup','Tools','Settings','About','Dashboard')) {
        Navigate "Nav$page"
        Check "Navigate $page" ($null -ne (Find 'PageTitle')) (Find 'PageTitle').Current.Name
        if($page -in @('Tools')) {
            if(Find 'ToolsRefresh') {
                Wait-InventoryButton 'ToolsRefresh'
                Check 'FIX7 Tools populated without refresh' ($null -ne (Find 'ToolAvailabilityTaskManager')) (Find 'ToolsStatus').Current.Name
                Check 'Tools page has real availability status' ($null -ne (Find 'ToolsStatus')) (Find 'ToolsStatus').Current.Name
            } else {
                Check "Placeholder $page" ($null -ne (Find 'DeferredFeature')) (Find 'DeferredFeature').Current.Name
            }
        }
        if($page -eq 'Uninstaller') {
            Wait-InventoryButton 'UnRefresh'
            Check 'FIX7 applications populated without refresh' ((Find 'UnStatus').Current.Name -match '^Applications loaded') (Find 'UnCount').Current.Name
            Check 'Uninstaller inventory controls available' ($null -ne (Find 'UnRefresh') -and $null -ne (Find 'UnApplications')) 'Phase 5 inventory page'
            Check 'Uninstaller requires explicit selection' (($null -eq (Find 'UnUninstall')) -or !(Find 'UnUninstall').Current.IsEnabled) 'No uninstall performed by regression'
        }
        if($page -eq 'Startup') {
            Wait-InventoryButton 'StartRefresh'
            Check 'FIX7 startup populated without refresh' ((Find 'StartStatus').Current.Name -match '^Inventory loaded|^Some startup sources') (Find 'StartStatus').Current.Name
            Check 'Startup inventory controls available' ($null -ne (Find 'StartRefresh') -and $null -ne (Find 'StartItems')) 'Phase 6 inventory page'
            Check 'Startup requires explicit selection' ((($null -eq (Find 'StartDisable')) -or !(Find 'StartDisable').Current.IsEnabled) -and (($null -eq (Find 'StartEnable')) -or !(Find 'StartEnable').Current.IsEnabled)) $null
        }
        if($page -eq 'Cleaner') {
            Check 'Production excludes synthetic fixture controls' ($null -eq (Find 'CleanerValidationPrepare')) 'Production artifact'
            Check 'Cleaner disabled before scan' (!(Find 'CleanerClean').Current.IsEnabled) 'Preview-only scanner'
            (Find 'CleanerScan').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Milliseconds 400
            Check 'Cleaner scanning state' (!(Find 'CleanerScan').Current.IsEnabled) (Find 'CleanerSummary').Current.Name
            (Find 'CleanerCancel').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
            $clock=[Diagnostics.Stopwatch]::StartNew()
            while(!(Find 'CleanerScan').Current.IsEnabled -and $clock.Elapsed.TotalSeconds -lt 30) { Start-Sleep -Milliseconds 200 }
            Check 'Cleaner cancel returns control' ((Find 'CleanerScan').Current.IsEnabled) $clock.Elapsed.TotalSeconds
            (Find 'CleanerScan').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Milliseconds 300
            $clock.Restart()
            # This scans the signed-in user's real profile, so its duration tracks their data and the
            # machine's load rather than the code: 195.4s for 123,874 files on one occasion and
            # 279.3s for 122,259 files on another, on the same commit. A fixed speed budget derived
            # from that is not a property of the product, and the previous 180s gate failed for
            # exactly that reason. So this bound only distinguishes "returned" from "hung"; the scan
            # is not failed for being slow. Performance pass/fail is Test-CleanerBenchmark.ps1,
            # which runs a fixed 111-file synthetic fixture where a fixed threshold means something.
            while(!(Find 'CleanerScan').Current.IsEnabled -and $clock.Elapsed.TotalSeconds -lt $CleanerScanWatchdogSeconds) { Start-Sleep -Milliseconds 300 }
            $realScanSeconds=$clock.Elapsed.TotalSeconds
            Check 'Cleaner real scan returns (hang watchdog, not a speed budget)' ((Find 'CleanerScan').Current.IsEnabled) ("{0:N1}s within ${CleanerScanWatchdogSeconds}s watchdog" -f $realScanSeconds)
            # Record the numbers every run so a real regression is still visible when two runs are
            # compared, without making an arbitrary threshold the arbiter.
            [pscustomobject]@{ DurationSeconds=[Math]::Round($realScanSeconds,2); WatchdogSeconds=$CleanerScanWatchdogSeconds; Summary=(Find 'CleanerSummary').Current.Name; RecordedUtc=[DateTime]::UtcNow.ToString('o') } |
                ConvertTo-Json | Set-Content "$EvidencePath/cleaner-real-scan.json" -Encoding UTF8
            Check 'Cleaner categories rendered' ((Find 'CleanerCategories').FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::CheckBox)).Count -gt 0) 'Real category results'
            Check 'Cleaner scan summary available' ((Find 'CleanerSummary').Current.Name -match 'Potentially reclaimable') 'Real scan; no deletion invoked'
            Save-WindowPicture 'cleaner-en-scanner'
            if($CleanerExecutor) {
                $list=Find 'CleanerCategories'
                foreach($percent in @(0,25,50,75,100)) {
                    if($list.GetCurrentPropertyValue([Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty)) {
                        $list.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1,$percent)
                        Start-Sleep -Milliseconds 150
                    }
                    $selectedBoxes=$list.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::CheckBox))
                    foreach($box in $selectedBoxes) {
                        $toggle=$box.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
                        if($box.Current.IsEnabled -and $toggle.Current.ToggleState -eq [Windows.Automation.ToggleState]::On){$toggle.Toggle()}
                    }
                }
                Check 'Explicit deselection disables clean' (!(Find 'CleanerClean').Current.IsEnabled) $null
                if($list.Current.IsEnabled -and $list.GetCurrentPropertyValue([Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty)) {
                    $list.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1,100)
                    Start-Sleep -Milliseconds 300
                }
                $boxes=$list.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::CheckBox))
                $review=$boxes | Where-Object { $_.Current.IsEnabled -and $_.Current.Name -eq 'Thumbnail cache' } | Select-Object -First 1
                Check 'Review category available for confirmation-only test' ($null -ne $review) 'No real deletion is invoked'
                $review.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
                Check 'Explicit category enables clean' ((Find 'CleanerClean').Current.IsEnabled) $null
                (Find 'CleanerClean').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
                Start-Sleep -Milliseconds 300
                Check 'Real confirmation dialog displayed' ($null -ne (Find 'CleanerConfirmation')) 'Destructive confirmation is cancelled'
                Save-WindowPicture 'cleaner-confirmation'
                (Find 'CloseButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
                Start-Sleep -Milliseconds 300
                Check 'Confirmation cancelled' ($null -eq (Find 'CleanerConfirmation')) $null
                $review.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
                Check 'Zero selected disables clean' (!(Find 'CleanerClean').Current.IsEnabled) $null
                Navigate 'NavDashboard'
                Navigate 'NavCleaner'
                Check 'Navigation invalidates selection' (!(Find 'CleanerClean').Current.IsEnabled) $null
                (Find 'CleanerScan').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
                Start-Sleep -Milliseconds 200
                Navigate 'NavDashboard'
                Start-Sleep -Seconds 1
                Navigate 'NavCleaner'
                $cancelWait=[Diagnostics.Stopwatch]::StartNew()
                while(!(Find 'CleanerScan').Current.IsEnabled -and $cancelWait.Elapsed.TotalSeconds -lt 30) { Start-Sleep -Milliseconds 100 }
                Check 'Navigate during scan cancels safely' ((Find 'CleanerScan').Current.IsEnabled -and !(Find 'CleanerClean').Current.IsEnabled) $null
            }
        }
        if($page -eq 'Performance') {
            $null=[ShellSmokeNative]::ShowWindow($p.MainWindowHandle,9)
            $null=[ShellSmokeNative]::SetForegroundWindow($p.MainWindowHandle)
            Start-Sleep -Seconds 5
            foreach($metric in @('PerfCpu','PerfMemory','PerfDisk','PerfNetwork')) {
                Check "Real performance $metric" ((Find $metric).Current.Name -notmatch 'Not available') (Find $metric).Current.Name
            }
            # Keep the native window visible and foregrounded during this
            # functional history test; record the actual history count below.
            # This is not a production memory measurement.
            for($second=0;$second -lt 65;$second++) {
                $null=[ShellSmokeNative]::SetForegroundWindow($p.MainWindowHandle)
                Start-Sleep -Seconds 1
            }
            $count=[int]([regex]::Match((Find 'PerfHistory').Current.Name,'\d+').Value)
            Check '60-second performance history bounded' ($count -ge 50 -and $count -le 60) $count
            Save-WindowPicture 'performance-en-charts'
            $searchBox=Find 'PerfSearch'
            $ancestor=$searchBox
            while($ancestor -and !$ancestor.GetCurrentPropertyValue([Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty)) { $ancestor=[Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($ancestor) }
            if($ancestor){$ancestor.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1,100)}
            Start-Sleep -Milliseconds 500
            Choose 'PerfMode' 'All processes'
            # The published executable was renamed to F1; still assert this exact running app.
            $searchBox.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($p.ProcessName)
            Start-Sleep -Seconds 3
            Check 'Process name search returns current application' ((Find 'PerfProcessState').Current.Name -notmatch '^0 ') (Find 'PerfProcessState').Current.Name
            $searchBox.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('NoSuchSystemCoreProcess_928173')
            Start-Sleep -Milliseconds 300
            Check 'Process search empty state' ((Find 'PerfProcessState').Current.Name -eq '0 processes shown') (Find 'PerfProcessState').Current.Name
            $searchBox.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('')
            Choose 'PerfSort' 'Working set'
            $en=([xml][IO.File]::ReadAllText((Resolve-Path 'src/SystemCore.App/Resources/Shell.resx'))).root.data
            Choose 'PerfMode' (($en | Where-Object name -eq 'PerfTopMemory').value)
            Choose 'PerfMode' (($en | Where-Object name -eq 'PerfTopCpu').value)
            Save-WindowPicture 'performance-en-processes'
        }
        if($page -eq 'SystemInfo') {
            Wait-Info 'Device overview'
            Check 'FIX7 System Information populated without refresh' ($null -ne $window.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Device name'))) $null
            $liveWait=[Diagnostics.Stopwatch]::StartNew()
            while((Find 'InfoLive').Current.Name -notmatch 'RAM:' -and $liveWait.Elapsed.TotalSeconds -lt 5) { Start-Sleep -Milliseconds 200 }
            Check 'System Info live values' ((Find 'InfoLive').Current.Name -match 'RAM:') 'Live memory populated'
            $button=Find 'InfoRefresh'
            $null=[ShellSmokeNative]::SetForegroundWindow($p.MainWindowHandle)
            $button.SetFocus()
            $focusWait=[Diagnostics.Stopwatch]::StartNew()
            do {
                $null=[ShellSmokeNative]::SetForegroundWindow($p.MainWindowHandle)
                $button.SetFocus()
                Start-Sleep -Milliseconds 100
            }while(([ShellSmokeNative]::GetForegroundWindow() -ne $p.MainWindowHandle -or !$button.Current.HasKeyboardFocus) -and $focusWait.Elapsed.TotalSeconds -lt 5)
            Check 'Refresh accepts keyboard focus' ($button.Current.HasKeyboardFocus) @{Id=[Windows.Automation.AutomationElement]::FocusedElement.Current.AutomationId;Name=[Windows.Automation.AutomationElement]::FocusedElement.Current.Name;Focusable=$button.Current.IsKeyboardFocusable;Enabled=$button.Current.IsEnabled}
            Check 'Keyboard targets application window' ([ShellSmokeNative]::GetForegroundWindow() -eq $p.MainWindowHandle) $null
            [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
            Wait-InventoryButton 'InfoRefresh'
            Check 'Keyboard refresh completes' ((Find 'InfoRefresh').Current.IsEnabled) $null
            $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Milliseconds 100
            Navigate 'NavDashboard'
            Navigate 'NavSystemInfo'
            Wait-Info 'Device overview'
            Check 'Refresh/navigation remains responsive' (!$p.HasExited) $null
            $null=[ShellSmokeNative]::ShowWindow($p.MainWindowHandle,6)
            Start-Sleep -Seconds 2
            $null=[ShellSmokeNative]::ShowWindow($p.MainWindowHandle,9)
            Start-Sleep -Seconds 2
            Check 'Minimize/restore System Info' ($null -ne (Find 'InfoRefresh')) $null
            Start-Sleep -Seconds 5
            $p.Refresh()
            $cpu=$p.TotalProcessorTime.TotalMilliseconds
            $clock=[Diagnostics.Stopwatch]::StartNew()
            Start-Sleep -Seconds 20
            $p.Refresh()
            $clock.Stop()
            [pscustomobject]@{Seconds=$clock.Elapsed.TotalSeconds;NormalizedCpuPercent=100*($p.TotalProcessorTime.TotalMilliseconds-$cpu)/$clock.Elapsed.TotalMilliseconds/[Environment]::ProcessorCount;WorkingSetBytes=$p.WorkingSet64;Responding=$p.Responding;Scope='System Info after collection, short observation, not benchmark'} | ConvertTo-Json | Set-Content "$EvidencePath/system-info-performance.json"
        }
    }
    $junk=(Find 'JunkStatus').Current.Name
    $partial=([xml][IO.File]::ReadAllText((Resolve-Path 'src/SystemCore.App/Resources/Shell.resx'))).root.data | Where-Object name -eq 'CleanPartial'
    if($CleanerExecutor){
        Check 'Dashboard clears invalidated scan after navigation cancellation' ($junk -eq 'Not scanned') $junk
    }else{
        Check 'Dashboard reflects completed Cleaner scan' ($junk -eq $partial.value -or $junk -match '^\d[\d,.]*\s+(B|KB|MB|GB|TB)$') $junk
    }
    $before=(Find 'SampleTime').Current.Name
    Start-Sleep -Seconds 2
    Check 'Live sample updates' ((Find 'SampleTime').Current.Name -ne $before) (Find 'SampleTime').Current.Name
    Navigate 'NavSettings'
    foreach($theme in @('Light','Dark','System')) { Choose 'ThemePicker' $theme }
    $arabic=([xml][IO.File]::ReadAllText((Resolve-Path 'src/SystemCore.App/Resources/Shell.ar.resx'))).root.data
    Choose 'LanguagePicker' (($arabic | Where-Object name -eq 'Arabic').value)
    Check 'Arabic navigation' ((Find 'NavDashboard').Current.Name -eq ($arabic | Where-Object name -eq 'NavDashboard').value) (Find 'NavDashboard').Current.Name
    Navigate 'NavDashboard'
    Check 'Arabic dashboard' ((Find 'PageTitle').Current.Name -eq ($arabic | Where-Object name -eq 'F1Welcome').value) (Find 'PageTitle').Current.Name
    if($CleanerExecutor) {
        Navigate 'NavCleaner'
        Check 'Arabic Cleaner title' ((Find 'PageTitle').Current.Name -eq ($arabic | Where-Object name -eq 'NavCleaner').value) $null
        Check 'Arabic Cleaner scan label' ((Find 'CleanerScan').Current.Name -eq ($arabic | Where-Object name -eq 'CleanScan').value) $null
        Save-WindowPicture 'cleaner-ar'
    }
    Navigate 'NavSystemInfo'
    Wait-Info (($arabic | Where-Object name -eq 'InfoOverview').value)
    Check 'Arabic System Info title' ((Find 'PageTitle').Current.Name -eq ($arabic | Where-Object name -eq 'InfoTitle').value) (Find 'PageTitle').Current.Name
    Navigate 'NavPerformance'
    Start-Sleep -Seconds 4
    Check 'Arabic Performance title' ((Find 'PageTitle').Current.Name -eq ($arabic | Where-Object name -eq 'NavPerformance').value) (Find 'PageTitle').Current.Name
    Check 'Arabic Performance metrics' ((Find 'PerfMemory').Current.Name -match 'GB') 'Shared display labels remain readable'
    $selection=(Find 'PerfMode').GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    Check 'Arabic process mode selection' ($selection[0].Current.Name -eq ($arabic | Where-Object name -eq 'PerfTopCpu').value) $selection[0].Current.Name
    $selection=(Find 'PerfSort').GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    Check 'Arabic process sort selection' ($selection[0].Current.Name -eq ($arabic | Where-Object name -eq 'PerfWorkingSet').value) $selection[0].Current.Name
    Save-WindowPicture 'performance-ar-processes'
    $ancestor=Find 'PerfSearch'
    while($ancestor -and !$ancestor.GetCurrentPropertyValue([Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty)) { $ancestor=[Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($ancestor) }
    if($ancestor){$ancestor.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1,0)}
    Start-Sleep -Milliseconds 300
    Save-WindowPicture 'performance-ar-charts'
    Navigate 'NavSettings'
    Choose 'LanguagePicker' 'English'
    Navigate 'NavDashboard'
    Check 'English restored' ((Find 'PageTitle').Current.Name -eq 'Welcome back') (Find 'PageTitle').Current.Name
    Navigate 'NavSettings'
    Choose 'ThemePicker' 'Dark'
    Choose 'LanguagePicker' (($arabic | Where-Object name -eq 'Arabic').value)
    $null=$p.CloseMainWindow()
    Check 'Clean close' ($p.WaitForExit(10000)) $null
    $p.Dispose()
    $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr-relaunch.txt"
    Start-Sleep -Seconds 5
    $p.Refresh()
    Check 'Relaunch with saved preferences' (!$p.HasExited -and $p.MainWindowHandle -ne 0) $p.Id
    $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    Check 'Arabic restored after relaunch' ((Find 'PageTitle').Current.Name -eq ($arabic | Where-Object name -eq 'F1Welcome').value) (Find 'PageTitle').Current.Name
    # F1's existing WindowAppearanceController fixes the shell LTR and localizes only ContentHost.
    Check 'RTL sidebar restored' ((Find 'NavDashboard').Current.Name -eq ($arabic | Where-Object name -eq 'NavDashboard').value -and (Find 'NavDashboard').Current.BoundingRectangle.X -lt (Find 'MetricCpu').Current.BoundingRectangle.X) (Find 'NavDashboard').Current.BoundingRectangle.X
    Navigate 'NavSettings'
    $selection=(Find 'ThemePicker').GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    Check 'Dark restored after relaunch' ($selection[0].Current.Name -eq ($arabic | Where-Object name -eq 'ThemeDark').value) $selection[0].Current.Name
    Choose 'LanguagePicker' 'English'
    Choose 'ThemePicker' 'Light'
    $null=$p.CloseMainWindow()
    Check 'Second clean close' ($p.WaitForExit(10000)) $null
    $p.Dispose()
    $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr-relaunch-en.txt"
    Start-Sleep -Seconds 5
    $p.Refresh()
    Check 'English relaunch' (!$p.HasExited -and $p.MainWindowHandle -ne 0) $p.Id
    $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    Check 'English persisted' ((Find 'PageTitle').Current.Name -eq 'Welcome back') (Find 'PageTitle').Current.Name
    Check 'LTR sidebar restored' ((Find 'NavDashboard').Current.BoundingRectangle.X -lt (Find 'MetricCpu').Current.BoundingRectangle.X) (Find 'NavDashboard').Current.BoundingRectangle.X
    Navigate 'NavSettings'
    $selection=(Find 'ThemePicker').GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    Check 'Light restored after relaunch' ($selection[0].Current.Name -eq 'Light') $selection[0].Current.Name
    $null=$p.CloseMainWindow()
    Check 'Final clean close' ($p.WaitForExit(10000)) $null
} finally {
    $results | ConvertTo-Json -Depth 4 | Set-Content "$EvidencePath/ui-results.json" -Encoding UTF8
    if(!$p.HasExited) { $null=$p.CloseMainWindow(); $null=$p.WaitForExit(10000) }
    $p.Dispose()
    if($null -ne $script:shellSavedPreferences){[IO.File]::WriteAllBytes($script:shellPreferences,$script:shellSavedPreferences)}else{[IO.File]::Delete($script:shellPreferences)}
}
