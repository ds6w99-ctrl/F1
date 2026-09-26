$ErrorActionPreference='Stop'
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-ProductStability.ps1)))) -AppPath artifacts/phase9/app/SystemCore.App.exe -EvidencePath artifacts/phase9/9c/stability -Cycles 24
