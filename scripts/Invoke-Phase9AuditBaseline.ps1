$ErrorActionPreference='Stop'
$started=[DateTime]::UtcNow
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-ProductStability.ps1)))) -AppPath artifacts/phase9/9a/baseline-app/SystemCore.App.exe -EvidencePath artifacts/phase9/9a/baseline-runtime -Cycles 6
$destination='artifacts/phase9/9a/baseline-runtime'
Get-ChildItem (Join-Path $env:LOCALAPPDATA 'SystemCore/ReliabilityAudit') -Filter 'audit-*.json'|Where-Object LastWriteTimeUtc -ge $started|ForEach-Object{Copy-Item $_.FullName $destination}
