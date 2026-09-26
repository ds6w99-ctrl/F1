$ErrorActionPreference='Stop'
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Invoke-F1Core.ps1))))
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Invoke-F1ProductUx.ps1)))) -EvidencePath artifacts/f1-ui/test-results/ui-01
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-F1Visual.ps1))))
