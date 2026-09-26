$ErrorActionPreference='Stop'
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-MemoryToolsSyntheticPage.ps1))))
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-MemoryToolsPage.ps1)))) -EvidencePath artifacts/phase7/completion/ui
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-MemoryToolsStability.ps1))))
[pscustomobject]@{Completed=$true;Change='RTL numeric direction isolation';Checked='Synthetic UI, production Memory UI, non-UIA native-input stability';CompletedAt=[DateTimeOffset]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content artifacts/phase7/completion/final-ui-result.json -Encoding utf8
