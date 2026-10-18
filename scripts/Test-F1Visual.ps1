param([string]$AppPath='artifacts/f1-ui/publish/F1.exe',[string]$EvidencePath='artifacts/f1-ui/screenshots')
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
. ([scriptblock]::Create($source.Substring(0,$source.IndexOf('$p=Start-Process')))) -AppPath $AppPath -EvidencePath $EvidencePath
function Save-WindowPicture([string]$name) {
 $null=[ShellSmokeNative]::SetForegroundWindow($p.MainWindowHandle)
 Start-Sleep -Milliseconds 400
 if([ShellSmokeNative]::GetForegroundWindow() -ne $p.MainWindowHandle){throw 'Foreground F1 desktop capture required'}
 $rect=New-Object ShellSmokeNative+Rect
 $null=[ShellSmokeNative]::GetWindowRect($p.MainWindowHandle,[ref]$rect)
 $image=New-Object Drawing.Bitmap(($rect.Right-$rect.Left),($rect.Bottom-$rect.Top))
 $graphics=[Drawing.Graphics]::FromImage($image)
 try{
  $graphics.CopyFromScreen($rect.Left,$rect.Top,0,0,$image.Size,[Drawing.CopyPixelOperation]::SourceCopy)
  $image.Save([IO.Path]::GetFullPath("$EvidencePath/$name.png"),[Drawing.Imaging.ImageFormat]::Png)
 }finally{$graphics.Dispose();$image.Dispose()}
}
if(!('F1CaptureNative' -as [type])) { Add-Type @'
using System;using System.Runtime.InteropServices;
public static class F1CaptureNative {
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd,out Rect rect);
}
'@
}
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
$saved=if(Test-Path $pref){[IO.File]::ReadAllBytes($pref)}else{$null}
if($null -ne $saved){[IO.File]::WriteAllBytes([IO.Path]::GetFullPath("$EvidencePath/original-preferences.bin"),$saved)}
$shots=[Collections.Generic.List[object]]::new();$p=$null
$resources=@{}
foreach($language in @('ar','en')){
 $file=if($language -eq 'ar'){'Shell.ar.resx'}else{'Shell.resx'};$map=@{}
 ([xml][IO.File]::ReadAllText((Resolve-Path "src/SystemCore.App/Resources/$file"))).root.data|ForEach-Object{$map[$_.name]=$_.value}
 $resources[$language]=$map
}
function Route([string]$page){
 $item=Find "Nav$page"
 if(!$item -and $page -eq 'MemoryTools'){(Find 'NavPerformance').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand();$item=Find 'NavMemoryTools'}
 Check "Route $page available" ($null -ne $item) $null
 $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();Start-Sleep -Milliseconds 500
 Scroll-Containing 'PageTitle' 0
}
function Invoke-Control([string]$id){(Find $id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();Start-Sleep -Milliseconds 300}
function Scroll-Containing([string]$id,[double]$percent){
 $node=Find $id
 while($node -and !$node.GetCurrentPropertyValue([Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty)){$node=[Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($node)}
 if($node){$scroll=$node.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern);if($scroll.Current.VerticallyScrollable){$scroll.SetScrollPercent(-1,$percent);Start-Sleep -Milliseconds 250}}
}
function Capture([string]$page,[string]$suffix=''){
 $name="$language-$theme-$page$suffix";Save-WindowPicture $name
 $r=New-Object F1CaptureNative+Rect;$null=[F1CaptureNative]::GetClientRect($p.MainWindowHandle,[ref]$r)
 $shots.Add([pscustomobject]@{File="$name.png";Page=$page;Language=$language;Theme=$theme;ClientPixels=@($r.Right,$r.Bottom);Dpi=[F1CaptureNative]::GetDpiForWindow($p.MainWindowHandle);Version='1.0.0';Utc=[DateTimeOffset]::UtcNow;Source='Published F1.exe desktop capture';AssemblySha256=(Get-FileHash (Join-Path (Split-Path $AppPath) F1.dll)).Hash})
}
try{
 [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"theme":"Dark","language":"ar","f1CompatibilitySentinel":{"preserve":true}}')
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 $watch=[Diagnostics.Stopwatch]::StartNew();do{Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $watch.Elapsed.TotalSeconds -lt 30)
 Check 'Published F1 launch' (!$p.HasExited -and $p.MainWindowHandle -ne [IntPtr]::Zero) $p.Id
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 $null=[ShellSmokeNative]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,10,10,1360,1000,0x14)
 Start-Sleep -Seconds 8
 # The app applies its default size in Loaded; resize only after that startup work.
 $null=[ShellSmokeNative]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,10,10,1360,1000,0x14)
 Start-Sleep -Milliseconds 500
 foreach($language in @('ar','en')){
  Route 'Settings';Choose 'LanguagePicker' $resources[$language][$(if($language -eq 'ar'){'Arabic'}else{'English'})]
  foreach($theme in @('Dark','Light','System')){
   Route 'Settings';Scroll-Containing 'LanguagePicker' 0;Choose 'ThemePicker' $resources[$language]["Theme$theme"]
   foreach($page in @('Dashboard','SystemInfo','Performance','MemoryTools','Cleaner','Uninstaller','Startup','Tools','Settings','About')){
    Route $page
    if($page -eq 'Dashboard'){Start-Sleep -Seconds 6;Check 'Left sidebar for both languages' ((Find 'NavDashboard').Current.BoundingRectangle.X -lt (Find 'MetricCpu').Current.BoundingRectangle.X) $language}
    if($page -eq 'SystemInfo'){Start-Sleep -Seconds 3}
    if($page -eq 'About'){
     Scroll-Containing 'AboutCopyVersion' 0;Check 'Exact F1 attribution' ((Find 'F1Attribution').Current.Name -eq 'Made By Falcone') $null
     Check 'Version remains a separate technical value in both languages' ((Find 'F1AboutVersion').Current.Name -eq '1.0.0') $language
    }
    Capture $page
    if($theme -eq 'Dark' -and $page -in @('Cleaner','MemoryTools','Uninstaller','Startup','Settings','About')){Scroll-Containing $(switch($page){'Cleaner'{'CleanerScan'} 'MemoryTools'{'MemPhysical'} 'Uninstaller'{'UnSearch'} 'Startup'{'StartSearch'} 'Settings'{'LanguagePicker'} 'About'{'AboutCopyVersion'}}) 100;Capture $page '-details';Scroll-Containing $(switch($page){'Cleaner'{'CleanerScan'} 'MemoryTools'{'MemPhysical'} 'Uninstaller'{'UnSearch'} 'Startup'{'StartSearch'} 'Settings'{'LanguagePicker'} 'About'{'AboutCopyVersion'}}) 0}
   }
   Route 'Settings';Scroll-Containing 'LanguagePicker' 0
   (Find 'ThemePicker').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand();Capture 'ThemePopup';[Windows.Forms.SendKeys]::SendWait('{ESC}')
   Route 'About';Scroll-Containing 'AboutCopyVersion' 100;Invoke-Control 'DiagnosticsClear';Capture 'Confirmation';[Windows.Forms.SendKeys]::SendWait('{ESC}')
   $closeWait=[Diagnostics.Stopwatch]::StartNew()
   do{Start-Sleep -Milliseconds 150;$dialog=Find 'DiagnosticsClearConfirmation'}while($dialog -and $closeWait.Elapsed.TotalSeconds -lt 5)
   Check 'Dialog cancel without deletion' ($null -eq $dialog) $closeWait.Elapsed.TotalSeconds
  }
 }
 $language='ar';$theme='Dark';Route 'Settings';Scroll-Containing 'LanguagePicker' 0;Choose 'LanguagePicker' $resources.ar.Arabic;Choose 'ThemePicker' $resources.ar.ThemeDark
 Route 'Dashboard';Start-Sleep -Seconds 25;Capture 'Dashboard' '-reference'
 Invoke-Control 'F1HeroScan';Check 'Hero opens real Cleaner' ((Find 'PageTitle').Current.Name -eq $resources.ar.NavCleaner) $null
 if((Find 'CleanerCancel').Current.IsEnabled){Invoke-Control 'CleanerCancel'}
 Route 'Dashboard';Invoke-Control 'F1ActionPrograms';Check 'Programs action routes' ((Find 'PageTitle').Current.Name -eq $resources.ar.NavUninstaller) $null
 Route 'Dashboard';Invoke-Control 'F1ActionStartup';Check 'Startup action routes' ((Find 'PageTitle').Current.Name -eq $resources.ar.NavStartup) $null
 Invoke-Control 'F1Settings';Check 'Title settings routes' ((Find 'PageTitle').Current.Name -eq $resources.ar.NavSettings) $null
 Invoke-Control 'F1Search';Capture 'SearchFlyout';[Windows.Forms.SendKeys]::SendWait('{ESC}')
 foreach($page in @('Dashboard','SystemInfo','Performance','MemoryTools','Cleaner','Uninstaller','Startup','Tools','Settings','About')){
  $null=[ShellSmokeNative]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,10,10,900,600,0x14);Route $page
  Capture $page '-minimum'
 }
 $stored=Get-Content $pref -Raw|ConvertFrom-Json;Check 'Unknown preference field preserved across F1 changes' $stored.f1CompatibilitySentinel.preserve $null
 $null=$p.CloseMainWindow();Check 'Normal close' ($p.WaitForExit(15000)) $null
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(15000)};$p.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $shots|ConvertTo-Json -Depth 5|Set-Content "$EvidencePath/metadata.json" -Encoding utf8
 $results|ConvertTo-Json -Depth 5|Set-Content "$EvidencePath/checks.json" -Encoding utf8
}
