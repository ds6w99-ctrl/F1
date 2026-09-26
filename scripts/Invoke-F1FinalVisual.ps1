$ErrorActionPreference='Stop'
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Invoke-F1ProductUx.ps1)))) -EvidencePath artifacts/f1-ui/test-results/ui-06
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-F1Visual.ps1))))
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-F1PopulatedScreens.ps1))))
