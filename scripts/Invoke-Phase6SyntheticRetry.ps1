$ErrorActionPreference='Stop'
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-StartupSyntheticPage.ps1)))) -EvidencePath artifacts/phase6/synthetic-ui-verified
