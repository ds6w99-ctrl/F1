param([string]$AppPath, [string]$EvidencePath='artifacts/current/theme-localization')
# Theme + localization round-trip on a real published build.
#
# WHAT WAS WRONG HERE BEFORE
# This script hardcoded its own build:
#     $AppPath='artifacts/phase8/app/SystemCore.App.exe'
#     $EvidencePath='artifacts/phase8/completion/theme-localization'
# and two callers patched that literal by string substitution rather than passing anything
# (Invoke-F1PageRegression.ps1 replaced the exe path with .Replace(),
# Invoke-Phase9CandidateUi.ps1 replaced 'artifacts/phase8/app' with 'artifacts/phase9/app').
# Three separate problems came out of that:
#   1. it named a phase, so the script was wrong the moment a later phase moved on;
#   2. artifacts/ is gitignored, so the target is simply absent on a clean checkout;
#   3. the target was named SystemCore.App.exe, and WinUI does not resolve its resource index under a
#      renamed executable. The app launched and reported a window titled 'F1', but its UI tree exposed
#      no AutomationId at all, so Navigate 'NavSettings' could never find NavSettings. That was
#      previously misread as a localization/theme failure rather than a renamed-binary failure.
# It is now a parameter, and when omitted it is discovered by Resolve-F1Artifact.ps1, which rejects
# renamed copies outright and validates ProductName and the repository <Version>. The two callers now
# pass -AppPath/-EvidencePath instead of rewriting this file's source text.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. ([scriptblock]::Create([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Resolve-F1Artifact.ps1'))))
$resolved=Resolve-F1AppArtifact -Repo $root -AppPath $AppPath
$AppPath=$resolved.Path
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-Shell.ps1))
. ([scriptblock]::Create($source.Substring(0,$source.IndexOf('$p=Start-Process')))) -AppPath $AppPath -EvidencePath $EvidencePath
$pref=Join-Path $env:LOCALAPPDATA 'SystemCore/preferences.json'
# Guarded: on a machine that has never run the app there is no preferences file yet, and the
# unguarded ReadAllBytes threw before the test could report anything useful.
$saved=if(Test-Path $pref){[IO.File]::ReadAllBytes($pref)}else{$null}
$p=$null
try{
 [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"language":"en","theme":"Light"}')
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 # Condition-based readiness instead of a fixed 5s sleep: wait for the window, then for the Settings
 # navigation element to actually exist in the UI tree. A sleep cannot prove the UI is ready; it can
 # only be long enough on a fast day and too short on a slow one.
 $launch=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 300;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq 0 -and $launch.Elapsed.TotalSeconds -lt 30)
 Check 'Localized build launched' (!$p.HasExited -and $p.MainWindowHandle -ne 0) $AppPath
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 $ready=[Diagnostics.Stopwatch]::StartNew();$navSettings=$null
 do{ $navSettings=Find 'NavSettings'; if(!$navSettings){Start-Sleep -Milliseconds 200} }
 while(!$navSettings -and $ready.Elapsed.TotalSeconds -lt 30)
 # This is the assertion that a renamed executable would fail: WinUI exposes no AutomationId at all
 # when the resource index cannot be resolved, so NavSettings is simply absent.
 Check 'UI tree exposes automation ids' ($null -ne $navSettings) ("{0:N1}s" -f $ready.Elapsed.TotalSeconds)
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
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $results|ConvertTo-Json -Depth 4|Set-Content "$EvidencePath/checks.json" -Encoding utf8
}
