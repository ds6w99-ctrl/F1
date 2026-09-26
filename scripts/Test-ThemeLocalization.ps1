$ErrorActionPreference='Stop'
$AppPath='artifacts/phase8/app/SystemCore.App.exe';$EvidencePath='artifacts/phase8/completion/theme-localization'
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
. ([scriptblock]::Create($source.Substring(0,$source.IndexOf('$p=Start-Process')))) -AppPath $AppPath -EvidencePath $EvidencePath
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json';$saved=[IO.File]::ReadAllBytes($pref);$p=$null
try{
 [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"language":"en","theme":"Light"}')
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 Start-Sleep -Seconds 5;$p.Refresh();$window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 Navigate 'NavSettings'
 foreach($culture in @('ar','en')){
  $file=if($culture -eq 'ar'){'Shell.ar.resx'}else{'Shell.resx'}
  $r=@{};([xml][IO.File]::ReadAllText((Resolve-Path "src/SystemCore.App/Resources/$file"))).root.data|ForEach-Object{$r[$_.name]=$_.value}
  Choose 'LanguagePicker' $r[$(if($culture -eq 'ar'){'Arabic'}else{'English'})]
  $actual=(Find 'ThemePicker').GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()[0].Current.Name
  Save-WindowPicture "$culture-selected-theme"
  Check "Collapsed selected theme localizes to $culture" ($actual -eq $r.ThemeLight) $actual
  Check 'Presentation refresh preserves theme preference' ((Get-Content $pref -Raw|ConvertFrom-Json).theme -eq 'Light') $null
 }
 $null=$p.CloseMainWindow();Check 'Theme test closes normally' ($p.WaitForExit(15000)) $null
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(15000)};$p.Dispose()}
 [IO.File]::WriteAllBytes($pref,$saved)
 $results|ConvertTo-Json -Depth 4|Set-Content "$EvidencePath/checks.json" -Encoding utf8
}
