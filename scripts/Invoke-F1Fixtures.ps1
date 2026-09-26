$ErrorActionPreference='Stop'
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-UninstallerPage.ps1)))) -AppPath artifacts/f1-ui/publish/F1.exe -EvidencePath artifacts/f1-ui/test-results/fixtures/uninstaller
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-StartupSyntheticPage.ps1)).Replace('Disabled by SystemCore','Disabled by F1')
& ([scriptblock]::Create($source)) -AppPath artifacts/f1-ui/fixture-app/F1.exe -EvidencePath artifacts/f1-ui/test-results/fixtures/startup
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Test-MemoryToolsSyntheticPage.ps1)).Replace('Released from SystemCore caches:','Released from F1 caches:')
& ([scriptblock]::Create($source)) -AppPath artifacts/f1-ui/fixture-app/F1.exe -EvidencePath artifacts/f1-ui/test-results/fixtures/memory
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-CleanerLifecycle.ps1)))) -AppPath artifacts/f1-ui/fixture-app/F1.exe -EvidencePath artifacts/f1-ui/test-results/fixtures/cleaner -MinimumCycles 1 -SnapshotCycles @(0,1)
