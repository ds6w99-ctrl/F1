$ErrorActionPreference='Stop'
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-StartupSyntheticPage.ps1)))) -EvidencePath artifacts/phase6/completion/synthetic-ui
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Invoke-Phase6FinalChecks.ps1)))) -EvidenceRoot artifacts/phase6/completion
