# Native UIA helpers. The caller owns $p. No cached provider survives a layout change.
function Find-F1NavigationElement([string]$id) {
 $root=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 $root.FindFirst([Windows.Automation.TreeScope]::Descendants,
  [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$id))
}
function Wait-F1NavigationCondition([scriptblock]$Condition,[string]$Description,[double]$Seconds=20) {
 $clock=[Diagnostics.Stopwatch]::StartNew()
 do {
  if($p.HasExited){throw "App exited while waiting for $Description"}
  try {$value=& $Condition;if($value){return $value}}
  catch [Windows.Automation.ElementNotAvailableException] { }
  if($clock.Elapsed.TotalSeconds -lt $Seconds){Start-Sleep -Milliseconds 100}
 } while($clock.Elapsed.TotalSeconds -lt $Seconds)
 throw "UI condition not reached: $Description (${Seconds}s)"
}
function Ensure-F1MemoryNavigation {
 $clock=[Diagnostics.Stopwatch]::StartNew()
 $observation=[pscustomobject]@{InitialState=$null}
 $result=Wait-F1NavigationCondition {
  $parent=Find-F1NavigationElement 'NavPerformance'
  if(!$parent -or $parent.Current.ControlType -ne [Windows.Automation.ControlType]::ListItem){return}
  $expand=$parent.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
  if($null -eq $observation.InitialState){$observation.InitialState=$expand.Current.ExpandCollapseState.ToString()}
  if($expand.Current.ExpandCollapseState -eq [Windows.Automation.ExpandCollapseState]::Collapsed){$expand.Expand();return}
  if($expand.Current.ExpandCollapseState -ne [Windows.Automation.ExpandCollapseState]::Expanded){return}
  $child=Find-F1NavigationElement 'NavMemoryTools'
  if(!$child -or $child.Current.IsOffscreen -or $child.Current.ControlType -ne [Windows.Automation.ControlType]::ListItem){return}
  $bounds=$child.Current.BoundingRectangle
  if($bounds.IsEmpty -or $bounds.Width -le 0 -or $bounds.Height -le 0){return}
  $ancestor=[Windows.Automation.TreeWalker]::RawViewWalker.GetParent($child)
  while($ancestor -and $ancestor.Current.AutomationId -ne 'NavPerformance'){
   $ancestor=[Windows.Automation.TreeWalker]::RawViewWalker.GetParent($ancestor)
  }
  if(!$ancestor){throw 'NavMemoryTools is not a child of NavPerformance'}
  [pscustomobject]@{Parent=$parent;Child=$child;InitialState=$observation.InitialState;ElapsedSeconds=$clock.Elapsed.TotalSeconds}
 } 'expanded Performance and visible Memory child'
 return $result
}
