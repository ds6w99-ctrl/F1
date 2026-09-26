$ErrorActionPreference='Stop'
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-ProductUx.ps1)))) -AppPath artifacts/phase9/9a/verified-app/SystemCore.App.exe -EvidencePath artifacts/phase9/9a/ui
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-MemoryToolsPage.ps1)))) -AppPath artifacts/phase9/9a/verified-app/SystemCore.App.exe -EvidencePath artifacts/phase9/9a/memory-ui
