$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
. ([scriptblock]::Create($source.Substring(0,$source.IndexOf('$p=Start-Process')))) -AppPath artifacts/f1-ui/publish/F1.exe -EvidencePath artifacts/f1-ui/test-results/calibration
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class F1CalibrationMouse {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
}
'@
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json';$saved=[IO.File]::ReadAllBytes($pref);$p=$null
$coordinates=Get-Content artifacts/f1-ui/test-results/ui-04/native-coordinates.json -Raw|ConvertFrom-Json
try{
 [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"language":"en","theme":"Light"}')
 $p=Start-Process (Resolve-Path $AppPath) -PassThru;Start-Sleep -Seconds 6;$p.Refresh()
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 $null=[ShellSmokeNative]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,40,40,1120,760,0x14);Start-Sleep -Milliseconds 500
 foreach($language in @('en','ar')){
  $resx=if($language -eq 'en'){'Shell.resx'}else{'Shell.ar.resx'};$resources=@{}
  ([xml][IO.File]::ReadAllText((Resolve-Path "src/SystemCore.App/Resources/$resx"))).root.data|ForEach-Object{$resources[$_.name]=$_.value}
  Navigate 'NavSettings';Choose 'LanguagePicker' $resources[$(if($language -eq 'en'){'English'}else{'Arabic'})]
  Navigate 'NavDashboard'
  $node=Find 'NavPerformance'
  $child=Find 'NavMemoryTools'
  Check 'Expanded child available for parent header calibration' ($child -and !$child.Current.IsOffscreen) $language
  $bounds=$node.Current.BoundingRectangle;$childBounds=$child.Current.BoundingRectangle
  Check 'Parent header band precedes child' ($childBounds.Top -gt $bounds.Top) @{Parent=$bounds.ToString();Child=$childBounds.ToString()}
  $rect=New-Object ShellSmokeNative+Rect;$null=[ShellSmokeNative]::GetWindowRect($p.MainWindowHandle,[ref]$rect)
  $coordinates.$language.NavPerformance.X=[int]($bounds.X+$bounds.Width/2-$rect.Left)
  $coordinates.$language.NavPerformance.Y=[int](($bounds.Top+$childBounds.Top)/2-$rect.Top)
  $null=[ShellSmokeNative]::SetForegroundWindow($p.MainWindowHandle)
  $null=[F1CalibrationMouse]::SetCursorPos(($coordinates.$language.NavPerformance.X+$rect.Left),($coordinates.$language.NavPerformance.Y+$rect.Top))
  [F1CalibrationMouse]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[F1CalibrationMouse]::mouse_event(4,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 500
  Check 'Native click selects real Performance page' ((Find 'PageTitle').Current.Name -eq $resources.NavPerformance) $language
  Save-WindowPicture "$language-performance-native"
 }
 $coordinates|ConvertTo-Json -Depth 6|Set-Content "$EvidencePath/native-coordinates.json" -Encoding utf8
 $null=$p.CloseMainWindow();Check 'Calibration normal close' ($p.WaitForExit(15000)) $null
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(15000)};$p.Dispose()}
 [IO.File]::WriteAllBytes($pref,$saved)
 $results|ConvertTo-Json -Depth 5|Set-Content "$EvidencePath/checks.json" -Encoding utf8
}
