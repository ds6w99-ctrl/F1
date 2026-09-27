param([string]$AppPath='artifacts/publish/App/F1.exe',
 [string]$EvidencePath='artifacts/current/navigation-transitions',[switch]$HelpersOnly)
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Test-Shell.ps1'))
. ([scriptblock]::Create($source.Substring(0,$source.IndexOf('$p=Start-Process')))) -AppPath $AppPath -EvidencePath $EvidencePath
. "$PSScriptRoot/F1-NavigationHarness.ps1"
$resources=@{}
foreach($language in @('en','ar')){
 $file=if($language -eq 'ar'){'Shell.ar.resx'}else{'Shell.resx'}
 $map=@{};([xml][IO.File]::ReadAllText((Join-Path $PSScriptRoot "../src/SystemCore.App/Resources/$file"))).root.data|ForEach-Object {$map[$_.name]=[string]$_.value}
 $resources[$language]=$map
}
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
$saved=if(Test-Path $pref){[IO.File]::ReadAllBytes($pref)}else{$null}
$samples=[Collections.Generic.List[object]]::new();$p=$null
function Start-NavApp([string]$language){
 [IO.Directory]::CreateDirectory((Split-Path $pref))|Out-Null
 [IO.File]::WriteAllText($pref,"{`"schemaVersion`":1,`"language`":`"$language`",`"theme`":`"Light`"}")
 $script:p=Start-Process (Resolve-Path $AppPath) -PassThru
 $null=Wait-F1NavigationCondition {$p.Refresh();if($p.MainWindowHandle -ne [IntPtr]::Zero){$true}} 'main window' 30
 $null=[ShellSmokeNative]::ShowWindow($p.MainWindowHandle,9)
 $null=[ShellSmokeNative]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,40,40,1120,760,0x14)
 $null=Wait-F1NavigationCondition {Find-F1NavigationElement 'NavSettings'} 'navigation'
}
function Close-NavApp {
 if($p -and !$p.HasExited){$null=$p.CloseMainWindow();if(!$p.WaitForExit(15000)){throw 'Navigation test app did not close'}}
 if($p){$p.Dispose();$script:p=$null}
}
function Select-Nav([string]$id,[string]$title){
 $item=Wait-F1NavigationCondition {Find-F1NavigationElement $id} $id
 $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
 $null=Wait-F1NavigationCondition {$t=Find-F1NavigationElement 'PageTitle';if($t -and $t.Current.Name -eq $title){$true}} "page $id"
}
function Set-NavLanguage([string]$from,[string]$to){
 Select-Nav 'NavSettings' $resources[$from].NavSettings
 $picker=Wait-F1NavigationCondition {Find-F1NavigationElement 'LanguagePicker'} 'language picker'
 $expand=$picker.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern);$expand.Expand()
 $label=$resources[$to][$(if($to -eq 'ar'){'Arabic'}else{'English'})]
 $item=Wait-F1NavigationCondition {
  $root=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
  $root.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.AndCondition]::new(
   [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$label),
   [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::IsSelectionItemPatternAvailableProperty,$true)))
 } 'language option' 5
 $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
 $expand.Collapse()
 $null=Wait-F1NavigationCondition {$title=Find-F1NavigationElement 'PageTitle';if($title -and $title.Current.Name -eq $resources[$to].NavSettings){$true}} 'localized Settings'
}
function Record-Memory([string]$kind,[string]$language,[int]$iteration){
 $nav=Ensure-F1MemoryNavigation
 $child=$nav.Child
 $ok=$child.Current.AutomationId -eq 'NavMemoryTools' -and $child.Current.Name -eq $resources[$language].NavMemoryTools -and !$child.Current.IsOffscreen
 $samples.Add([pscustomobject]@{Kind=$kind;Language=$language;Iteration=$iteration;Found=$ok;AutomationId=$child.Current.AutomationId;Name=$child.Current.Name;ControlType=$child.Current.ControlType.ProgrammaticName;Bounds=$child.Current.BoundingRectangle.ToString();IsOffscreen=$child.Current.IsOffscreen;ParentId=$nav.Parent.Current.AutomationId;ParentState=$nav.Parent.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Current.ExpandCollapseState.ToString()})
 if(!$ok){throw "Memory identity/visibility failed: $kind/$language/$iteration"}
 Select-Nav 'NavMemoryTools' $resources[$language].NavMemoryTools
 $scan=Wait-F1NavigationCondition {Find-F1NavigationElement 'MemAnalyze'} 'Memory page action'
 if(!$scan.Current.IsEnabled){throw 'Memory page action disabled'}
}
if($HelpersOnly){return}
try {
 Start-NavApp 'en'
 for($i=1;$i -le 10;$i++){
  # Exercise both valid parent states. The explicit collapsed case was red before the fix.
  if($i % 2 -eq 0){(Find-F1NavigationElement 'NavPerformance').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()}
  Set-NavLanguage 'en' 'ar';Record-Memory 'en-to-ar' 'ar' $i
  if($i % 2 -eq 0){(Find-F1NavigationElement 'NavPerformance').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()}
  Set-NavLanguage 'ar' 'en';Record-Memory 'ar-to-en' 'en' $i
 }
 Close-NavApp
 foreach($language in @('ar','en')){
  for($i=1;$i -le 5;$i++){
   Start-NavApp $language;Record-Memory 'restart' $language $i;Close-NavApp
  }
 }
 "MEMORY_FOUND=$(@($samples|Where-Object Found).Count)/$($samples.Count)"
 if($samples.Count -ne 30){throw 'Incomplete requested matrix'}
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(15000)};$p.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $samples|ConvertTo-Json -Depth 4|Set-Content "$EvidencePath/transitions.json" -Encoding UTF8
}
