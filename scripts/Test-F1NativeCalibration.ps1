param([string]$AppPath,[string]$EvidencePath='artifacts/current/calibration',[switch]$LightTrace,[switch]$FailureOnlyTrace,[switch]$ProbeCollapsedState)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
# Same discovery rule as the rest of the harness: prefer an explicit -AppPath, otherwise resolve the
# current build and refuse renamed copies. This script used to hardcode artifacts/f1-ui/publish/F1.exe,
# which is build output from a particular earlier run rather than a property of the source tree.
. ([scriptblock]::Create([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Resolve-F1Artifact.ps1'))))
$resolved=Resolve-F1AppArtifact -Repo $root -AppPath $AppPath
$AppPath=$resolved.Path
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
. ([scriptblock]::Create($source.Substring(0,$source.IndexOf('$p=Start-Process')))) -AppPath $AppPath -EvidencePath $EvidencePath
. "$PSScriptRoot/F1-NavigationHarness.ps1"
# Condition-based UI synchronization. A fixed sleep cannot establish that the UI is ready: it can only
# be long enough on an unloaded machine and too short on a loaded one, which is exactly how this test
# came to fail intermittently. Each helper below waits for an observable state with a bounded timeout
# and fails loudly on timeout, which is a real signal, rather than hoping a duration was sufficient.
function Wait-Element([string]$id,[double]$seconds){
 $clock=[Diagnostics.Stopwatch]::StartNew();$e=$null
 do{ if($p.MainWindowHandle -ne 0){$e=Find $id}; if(!$e){Start-Sleep -Milliseconds 100} }while(!$e -and $clock.Elapsed.TotalSeconds -lt $seconds)
 if(!$e){throw "Control $id did not appear within ${seconds}s"}
 return $e
}
function Wait-WindowSized([int]$width,[int]$height,[double]$seconds){
 $clock=[Diagnostics.Stopwatch]::StartNew();$r=New-Object ShellSmokeNative+Rect
 do{ $null=[ShellSmokeNative]::GetWindowRect($p.MainWindowHandle,[ref]$r)
  if(($r.Right-$r.Left) -ne $width -or ($r.Bottom-$r.Top) -ne $height){Start-Sleep -Milliseconds 100} }
 while((($r.Right-$r.Left) -ne $width -or ($r.Bottom-$r.Top) -ne $height) -and $clock.Elapsed.TotalSeconds -lt $seconds)
 if(($r.Right-$r.Left) -ne $width -or ($r.Bottom-$r.Top) -ne $height){throw 'Calibration viewport did not reach requested size'}
 return $r
}
function Wait-PageTitle([string]$expected,[double]$seconds){
 $clock=[Diagnostics.Stopwatch]::StartNew();$actual=$null
 do{ $node=Find 'PageTitle'; if($node){$actual=$node.Current.Name}; if($actual -ne $expected){Start-Sleep -Milliseconds 100} }
 while($actual -ne $expected -and $clock.Elapsed.TotalSeconds -lt $seconds)
 return $actual
}
function Save-NavigationEvidence([string]$label,[switch]$Force){
 if($FailureOnlyTrace -and !$Force){return}
 $fresh=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 $condition=if($LightTrace -and !$Force){[Windows.Automation.OrCondition]::new([Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'NavPerformance'),[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'NavMemoryTools'))}else{[Windows.Automation.Condition]::TrueCondition}
 $nodes=$fresh.FindAll([Windows.Automation.TreeScope]::Descendants,$condition)
 $tree=@(foreach($element in $nodes){
  try {
  $parent=[Windows.Automation.TreeWalker]::RawViewWalker.GetParent($element)
  $pattern=$null
  $expanded=if($element.TryGetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern,[ref]$pattern)){$pattern.Current.ExpandCollapseState.ToString()}else{$null}
  [pscustomobject]@{AutomationId=$element.Current.AutomationId;Name=$element.Current.Name;ControlType=$element.Current.ControlType.ProgrammaticName;IsOffscreen=$element.Current.IsOffscreen;Bounds=$element.Current.BoundingRectangle.ToString();RuntimeId=($element.GetRuntimeId() -join '.');ParentId=$(if($parent){$parent.Current.AutomationId});ExpandCollapse=$expanded}
  } catch {
   [pscustomobject]@{SnapshotError=$_.Exception.Message}
  }
 })
 $tree | ConvertTo-Json -Depth 4 | Set-Content "$EvidencePath/$label-tree.json" -Encoding UTF8
 $oldChild=Find 'NavMemoryTools'
 [pscustomobject]@{Label=$label;FreshMemoryCount=@($tree|Where-Object AutomationId -eq 'NavMemoryTools').Count;CachedRootFind=($null -ne $oldChild);Nav=@($tree|Where-Object {$_.AutomationId -in @('NavPerformance','NavMemoryTools')})} |
  ConvertTo-Json -Depth 5 | Set-Content "$EvidencePath/$label-nav.json" -Encoding UTF8
 if($LightTrace -and !$Force){return}
 $rect=New-Object ShellSmokeNative+Rect
 $null=[ShellSmokeNative]::GetWindowRect($p.MainWindowHandle,[ref]$rect)
 $image=New-Object Drawing.Bitmap(($rect.Right-$rect.Left),($rect.Bottom-$rect.Top))
 $graphics=[Drawing.Graphics]::FromImage($image);$dc=$graphics.GetHdc()
 try{$captured=[ShellSmokeNative]::PrintWindow($p.MainWindowHandle,$dc,2)}finally{$graphics.ReleaseHdc($dc)}
 try{if(!$captured){throw 'Diagnostic screenshot failed'};$image.Save([IO.Path]::GetFullPath("$EvidencePath/$label.png"))}finally{$graphics.Dispose();$image.Dispose()}
}
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json';$saved=if(Test-Path $pref){[IO.File]::ReadAllBytes($pref)}else{$null};$p=$null
try{
 [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"language":"en","theme":"Light"}')
 $p=Start-Process (Resolve-Path $AppPath) -PassThru
 # Replaces the fixed 6s sleep. Readiness is: the process has a window, and the UI tree actually
 # exposes the controls this test calibrates. A renamed executable never gets here, which is the point.
 $launch=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq 0 -and $launch.Elapsed.TotalSeconds -lt 30)
 Check 'Calibration build presented a window' (!$p.HasExited -and $p.MainWindowHandle -ne 0) $AppPath
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 $null=[ShellSmokeNative]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,40,40,1120,760,0x14)
 # Replaces a fixed 500ms after the resize: wait for the window to actually report the new size and
 # for the first-page control to be realized, so the coordinates below are read from a settled layout.
 $null=Wait-WindowSized 1120 760 15
 $null=Wait-Element 'PageTitle' 20
 foreach($language in @('en','ar')){
  $resx=if($language -eq 'en'){'Shell.resx'}else{'Shell.ar.resx'};$resources=@{}
  ([xml][IO.File]::ReadAllText((Resolve-Path "src/SystemCore.App/Resources/$resx"))).root.data|ForEach-Object{$resources[$_.name]=$_.value}
  Navigate 'NavSettings'
  if($ProbeCollapsedState -and $language -eq 'ar'){
   (Find 'NavPerformance').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
  }
  Save-NavigationEvidence "$language-before-language"
  Choose 'LanguagePicker' $resources[$(if($language -eq 'en'){'English'}else{'Arabic'})]
  Save-NavigationEvidence "$language-after-language"
  Navigate 'NavDashboard'
  Save-NavigationEvidence "$language-after-dashboard"
  # A collapsed group does not realize its child. Waiting alone cannot expand it.
  try {$navigation=Ensure-F1MemoryNavigation}
  catch {Save-NavigationEvidence "$language-missing-child" -Force;throw}
  $node=$navigation.Parent;$child=$navigation.Child
  [pscustomobject]@{Language=$language;WaitSeconds=$navigation.ElapsedSeconds;InitialParentState=$navigation.InitialState;ParentPresent=($null -ne $node);ChildPresent=($null -ne $child);ChildOffscreen=$(if($child){$child.Current.IsOffscreen}else{$null})} |
   ConvertTo-Json | Set-Content "$EvidencePath/$language-child-readiness.json" -Encoding UTF8
  if(!$child -or $child.Current.IsOffscreen){Save-NavigationEvidence "$language-missing-child" -Force}
  Check 'Expanded child available for parent header calibration' ($child -and !$child.Current.IsOffscreen) $language
  $bounds=$node.Current.BoundingRectangle;$childBounds=$child.Current.BoundingRectangle
  Check 'Parent header band precedes child' ($childBounds.Top -gt $bounds.Top) @{Parent=$bounds.ToString();Child=$childBounds.ToString()}
  $rect=New-Object ShellSmokeNative+Rect;$null=[ShellSmokeNative]::GetWindowRect($p.MainWindowHandle,[ref]$rect)
  # Native input targets the foreground desktop, unlike UIA selection. Establish that
  # prerequisite explicitly; never retry a failed click or substitute UIA navigation.
  $focusReady=[Diagnostics.Stopwatch]::StartNew()
  do {
   $focused=[ShellSmokeNative]::SetForegroundWindow($p.MainWindowHandle)
   if([ShellSmokeNative]::GetForegroundWindow() -eq $p.MainWindowHandle){break}
   Start-Sleep -Milliseconds 100
  } while($focusReady.Elapsed.TotalSeconds -lt 5)
  $foregroundBefore=[ShellSmokeNative]::GetForegroundWindow()
  Check 'Calibration owns foreground before native input' ($foregroundBefore -eq $p.MainWindowHandle) $language
  # This provider exposes neither a reliable clickable point nor a child hit-test.
  # Use native keyboard activation of the exact UIA-identified item, not guessed
  # mouse coordinates. This verifies native activation, not mouse hit testing.
  $node.SetFocus()
  $null=Wait-F1NavigationCondition {$focusedItem=Find-F1NavigationElement 'NavPerformance';if($focusedItem -and $focusedItem.Current.HasKeyboardFocus){$true}} 'Performance keyboard focus'
  [Windows.Forms.SendKeys]::SendWait('{ENTER}')
  # Wait for the actual activation result, not a fixed post-input delay.
  $settled=Wait-PageTitle $resources.NavPerformance 20
  Save-NavigationEvidence "$language-after-native-activation"
  [pscustomobject]@{Language=$language;Expected=$resources.NavPerformance;Actual=$settled;AppHwnd=$p.MainWindowHandle.ToInt64();ForegroundBefore=$foregroundBefore.ToInt64();ForegroundAfter=([ShellSmokeNative]::GetForegroundWindow()).ToInt64();FocusRequest=$focused;Input='Enter on UIA-focused NavPerformance'} |
   ConvertTo-Json | Set-Content "$EvidencePath/$language-click-diagnostic.json" -Encoding UTF8
  Check 'Native keyboard activation selects real Performance page' ($settled -eq $resources.NavPerformance) $language
  Save-WindowPicture "$language-performance-native"
 }
 $null=$p.CloseMainWindow();Check 'Calibration normal close' ($p.WaitForExit(15000)) $null
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(15000)};$p.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $results|ConvertTo-Json -Depth 5|Set-Content "$EvidencePath/checks.json" -Encoding utf8
}
