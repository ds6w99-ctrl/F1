& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-F1R2.ps1)))) -AppPath artifacts/f1-ui/iterations/r2/audit/F1.exe -EvidencePath artifacts/f1-ui/iterations/r2/audit-runtime
