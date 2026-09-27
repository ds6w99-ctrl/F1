param([string]$AppPath,[string]$EvidencePath='artifacts/current/ui-review',[string]$Language='ar',[string]$Theme='Dark')
# Collects the post-FIX-4 visual review package: one screenshot per page, in a single folder, with a
# manifest. This script only OBSERVES the running app. It changes no UI, no resources and no code.
#
# Scope is deliberately narrow compared with Test-F1Visual.ps1, which captures every page in every
# language and theme (2 x 3 x N). A review package wants a fixed, obvious set: the ten product pages
# in one language and one theme, named so they sort in navigation order. Arabic Dark is the default
# because it is the layout most sensitive to text density: it is RTL, and FIX 4 shortened and removed
# text across the shell, so the Arabic pages are where a bad truncation or a mis-measured column would
# show up first.
#
# Readiness is condition-based. Each page is captured only after its title has stopped changing across
# consecutive polls, so a screenshot is never taken mid-transition. A fixed sleep cannot make that
# guarantee.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. ([scriptblock]::Create([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Resolve-F1Artifact.ps1'))))
$resolved=Resolve-F1AppArtifact -Repo $root -AppPath $AppPath
$AppPath=$resolved.Path
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
. ([scriptblock]::Create($source.Substring(0,$source.IndexOf('$p=Start-Process')))) -AppPath $AppPath -EvidencePath $EvidencePath

# The ten product pages, in navigation order. Memory is a child of Performance in the nav tree, so it
# is reached by expanding the parent first; System Information's id is NavSystemInfo.
$pages=[ordered]@{
 '01-dashboard'='NavDashboard'; '02-cleaner'='NavCleaner'; '03-uninstaller'='NavUninstaller'
 '04-startup'='NavStartup'; '05-performance'='NavPerformance'; '06-memory'='NavMemoryTools'
 '07-system-information'='NavSystemInfo'; '08-tools'='NavTools'; '09-settings'='NavSettings'
 '10-about'='NavAbout'
}
function Scroll-Containing([string]$id,[double]$percent){
 $node=Find $id
 while($node -and !$node.GetCurrentPropertyValue([Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty)){$node=[Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($node)}
 if($node){$scroll=$node.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern);if($scroll.Current.VerticallyScrollable){$scroll.SetScrollPercent(-1,$percent)}}
}
# Re-derive the UIA root from the HWND on every lookup. The shared prefix captures the root provider
# once, which goes stale across the ShowWindow/SetForegroundWindow cycle that every screenshot does;
# Test-CleanerLifecycle.ps1 already carries this same override for the same reason.
function Find([string]$id){
 $rt=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 $rt.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$id))
}
# Wait until PageTitle reports the same non-empty value twice in a row: that is the UI having settled,
# rather than an arbitrary duration having elapsed.
function Wait-PageSettled([double]$seconds){
 $clock=[Diagnostics.Stopwatch]::StartNew();$last=$null;$stable=0
 do{
  $node=Find 'PageTitle';$value=if($node){$node.Current.Name}else{$null}
  if($value -and $value -eq $last){$stable++}else{$stable=0}
  $last=$value
  if($stable -lt 1){Start-Sleep -Milliseconds 100}
 }while($stable -lt 1 -and $clock.Elapsed.TotalSeconds -lt $seconds)
 return [pscustomobject]@{Title=$value;Settled=($stable -ge 1);Waited=[Math]::Round($clock.Elapsed.TotalSeconds,2)}
}
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
$saved=if(Test-Path $pref){[IO.File]::ReadAllBytes($pref)}else{$null}
$manifest=[Collections.Generic.List[object]]::new();$p=$null
try{
 New-Item -ItemType Directory -Force $EvidencePath|Out-Null
 [IO.File]::WriteAllText($pref,"{`"schemaVersion`":1,`"theme`":`"$Theme`",`"language`":`"$Language`"}")
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 $launch=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq 0 -and $launch.Elapsed.TotalSeconds -lt 30)
 Check 'Review build launched' (!$p.HasExited -and $p.MainWindowHandle -ne 0) $AppPath
 $null=[ShellSmokeNative]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,40,40,1280,860,0x14)
 # Fixed capture size so the ten shots are directly comparable side by side.
 $sized=[Diagnostics.Stopwatch]::StartNew();$r=New-Object ShellSmokeNative+Rect
 do{$null=[ShellSmokeNative]::GetWindowRect($p.MainWindowHandle,[ref]$r);if(($r.Right-$r.Left) -ne 1280){Start-Sleep -Milliseconds 100}}
 while(($r.Right-$r.Left) -ne 1280 -and $sized.Elapsed.TotalSeconds -lt 15)
 $nav=Find 'NavDashboard'
 if(!$nav){$ready=[Diagnostics.Stopwatch]::StartNew();do{$nav=Find 'NavDashboard';if(!$nav){Start-Sleep -Milliseconds 150}}while(!$nav -and $ready.Elapsed.TotalSeconds -lt 30)}
 Check 'Navigation tree ready' ($null -ne $nav)
 foreach($name in $pages.Keys){
  $id=$pages[$name]
  $item=Find $id
  # Memory lives under the expanded Performance header.
  if(!$item -and $id -eq 'NavMemoryTools'){
   $parent=Find 'NavPerformance'
   if($parent -and $parent.GetCurrentPropertyValue([Windows.Automation.AutomationElement]::IsExpandCollapsePatternAvailableProperty)){
    $parent.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $w=[Diagnostics.Stopwatch]::StartNew();do{$item=Find $id;if(!$item){Start-Sleep -Milliseconds 150}}while(!$item -and $w.Elapsed.TotalSeconds -lt 10)
   }
  }
  if(!$item){Check "Route $name" $false $id;continue}
  $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
  $settle=Wait-PageSettled 20
  Scroll-Containing 'PageTitle' 0
  Save-WindowPicture $name
  $file="$name.png"
  $bytes=if(Test-Path "$EvidencePath/$file"){(Get-Item "$EvidencePath/$file").Length}else{0}
  Check "Captured $name" ($settle.Settled -and $bytes -gt 0) ("{0}  {1:N0} bytes  settled {2}s" -f $settle.Title,$bytes,$settle.Waited)
  $manifest.Add([pscustomobject]@{
   Order=$name;File=$file;NavId=$id;Title=$settle.Title;Bytes=$bytes;Settled=$settle.Settled
   Language=$Language;Theme=$Theme;ClientWidth=($r.Right-$r.Left);ClientHeight=($r.Bottom-$r.Top)
  })
 }
 [pscustomobject]@{
  GeneratedUtc=[DateTime]::UtcNow.ToString('o');Build=$AppPath;ProductVersion=$resolved.ProductVersion
  Language=$Language;Theme=$Theme;PageCount=$manifest.Count
  AssemblySha256=(Get-FileHash (Join-Path (Split-Path $AppPath) 'F1.dll')).Hash
  Note='Observation only; no UI, resource or code change was made to produce this package.'
  Pages=$manifest
 }|ConvertTo-Json -Depth 5|Set-Content "$EvidencePath/index.json" -Encoding UTF8
 $null=$p.CloseMainWindow();Check 'Review app closed normally' ($p.WaitForExit(15000)) $null
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(15000)};$p.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $results|ConvertTo-Json -Depth 4|Set-Content "$EvidencePath/checks.json" -Encoding utf8
}
