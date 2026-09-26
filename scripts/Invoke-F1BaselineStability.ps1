$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-ProductStability.ps1))
# Same six-cycle workload for before/after, with two full minutes minimized.
$source=$source.Replace("for(`$i=0;`$i -lt 6;`$i++){Start-Sleep -Seconds 5;Record 'minimized'}","for(`$i=0;`$i -lt 24;`$i++){Start-Sleep -Seconds 5;Record 'minimized'}")
& ([scriptblock]::Create($source)) -AppPath artifacts/phase9/app/SystemCore.App.exe -EvidencePath artifacts/f1-ui/baseline/stability -Cycles 6
