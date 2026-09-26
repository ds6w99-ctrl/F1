$ErrorActionPreference='Stop'
# Targeted image-only correction after a completed populated-state matrix.
$visual=[IO.File]::ReadAllText((Resolve-Path scripts/Test-F1Visual.ps1))
$entry=[regex]::Match($visual,'(?m)^try\{\r?$').Index
if($entry -le 0){throw 'Capture helper boundary missing'}
. ([scriptblock]::Create($visual.Substring(0,$entry))) -AppPath artifacts/f1-ui/publish/F1.exe -EvidencePath artifacts/f1-ui/screenshots-populated
$previous=Get-Content "$EvidencePath/metadata.json" -Raw|ConvertFrom-Json
$proof='artifacts/f1-ui/test-results/memory-framing'
[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($proof))|Out-Null
try{
 [IO.File]::WriteAllText($pref,'{"schemaVersion":1,"theme":"Dark","language":"ar"}')
 $p=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$proof/stderr.txt"
 $startup=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 200;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $startup.Elapsed.TotalSeconds -lt 30)
 Check 'Memory framing startup ready' (!$p.HasExited -and $p.MainWindowHandle -ne [IntPtr]::Zero) $p.Id
 $window=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 Start-Sleep -Seconds 4
 $null=[ShellSmokeNative]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,10,10,1360,1000,0x14)
 foreach($language in @('ar','en')){
  Route 'Settings';Choose 'LanguagePicker' $resources[$language][$(if($language -eq 'ar'){'Arabic'}else{'English'})]
  foreach($theme in @('Dark','Light')){
   Route 'Settings';Choose 'ThemePicker' $resources[$language]["Theme$theme"]
   Route 'MemoryTools';Scroll-Containing 'MemPhysical' 0;Start-Sleep -Seconds 12
   Check 'Physical memory visible in history frame' (!(Find 'MemPhysical').Current.IsOffscreen) "$language-$theme"
   Capture 'MemoryTools' '-history'
   Scroll-Containing 'MemPhysical' 100;Capture 'MemoryTools' '-consumers'
   Scroll-Containing 'MemPhysical' 0
  }
 }
 $null=$p.CloseMainWindow();Check 'Memory framing normal close' ($p.WaitForExit(15000)) $null
 $replace=@($shots.File)
 $merged=@($previous|Where-Object {$_.File -notin $replace})+@($shots)
 if($merged.Count -ne 36){throw 'Populated screenshot metadata count changed'}
 $merged|Sort-Object File|ConvertTo-Json -Depth 5|Set-Content "$EvidencePath/metadata.json" -Encoding utf8
 $shots|ConvertTo-Json -Depth 5|Set-Content "$proof/metadata.json" -Encoding utf8
}finally{
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(15000)};$p.Dispose()}
 if($null -ne $saved){[IO.File]::WriteAllBytes($pref,$saved)}else{[IO.File]::Delete($pref)}
 $results|ConvertTo-Json -Depth 5|Set-Content "$proof/checks.json" -Encoding utf8
}
