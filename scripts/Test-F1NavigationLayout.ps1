param([string]$AppPath='artifacts/publish/App/F1.exe',[string]$EvidencePath='artifacts/current/navigation-layout')
. "$PSScriptRoot/Test-F1NavigationTransitions.ps1" -AppPath $AppPath -EvidencePath $EvidencePath -HelpersOnly
$layout=[Collections.Generic.List[object]]::new()
try {
 foreach($language in @('en','ar')){
  Start-NavApp $language
  $nav=Ensure-F1MemoryNavigation
  Select-Nav 'NavMemoryTools' $resources[$language].NavMemoryTools
  $title=Find-F1NavigationElement 'PageTitle'
  $text=$title.GetCurrentPattern([Windows.Automation.TextPattern]::Pattern)
  $flow=$text.DocumentRange.GetAttributeValue([Windows.Automation.TextPattern]::TextFlowDirectionsAttribute)
  $alignment=$text.DocumentRange.GetAttributeValue([Windows.Automation.TextPattern]::HorizontalTextAlignmentAttribute)
  $expected=if($language -eq 'ar'){[Windows.Automation.Text.FlowDirections]::RightToLeft}else{[Windows.Automation.Text.FlowDirections]::Default}
  $flowAvailable=$flow -is [int] -or $flow -is [Windows.Automation.Text.FlowDirections]
  $flowStatus=if(!$flowAvailable){'NOT AVAILABLE'}elseif([int]$flow -eq [int]$expected){'PASS'}else{'FAIL'}
  $passed=$nav.Child.Current.BoundingRectangle.Right -lt $title.Current.BoundingRectangle.Left -and $title.Current.Name -eq $resources[$language].NavMemoryTools
  $layout.Add([pscustomobject]@{Language=$language;Flow="$flow";FlowAttributeStatus=$flowStatus;Expected="$expected";Alignment="$alignment";SidebarAndLocalizationPassed=$passed;VisualDirectionReview='PENDING';MemoryId=$nav.Child.Current.AutomationId;MemoryBounds=$nav.Child.Current.BoundingRectangle.ToString();TitleBounds=$title.Current.BoundingRectangle.ToString()})
  # Keep the sidebar on the left in both languages; only page content changes direction.
  if(!$passed -or $flowStatus -eq 'FAIL'){throw "Layout direction/sidebar assertion failed for $language (flow=$flow)"}
  $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
  Save-WindowPicture "$language-memory-layout"
  Close-NavApp
 }
 "LAYOUT_CAPTURE=$($layout.Count)/2; direction attribute may be NOT AVAILABLE; inspect layout.json and screenshots"
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(15000)};$p.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $layout|ConvertTo-Json -Depth 4|Set-Content "$EvidencePath/layout.json" -Encoding UTF8
}
