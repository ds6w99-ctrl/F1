param([string]$EvidencePath='artifacts/f1-ui/test-results/stability',[string]$Calibration='artifacts/f1-ui/test-results/calibration/native-coordinates.json')
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-ProductStability.ps1))
$source=$source.Replace('artifacts/phase8/completion/ui/native-coordinates.json',$Calibration)
$source=$source.Replace('Start-Sleep -Seconds 3;Foreground','Start-Sleep -Seconds 3;$null=[MemorySoakNative]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,40,40,1120,760,0x14);Start-Sleep -Milliseconds 300;Foreground')
$source=$source.Replace("for(`$i=0;`$i -lt 6;`$i++){Start-Sleep -Seconds 5;Record 'minimized'}","for(`$i=0;`$i -lt 24;`$i++){Start-Sleep -Seconds 5;Record 'minimized'}")
& ([scriptblock]::Create($source)) -AppPath artifacts/f1-ui/publish/F1.exe -EvidencePath $EvidencePath -Cycles 6
