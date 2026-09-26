& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Invoke-F1ProductUx.ps1)))) -AppPath artifacts/f1-ui/iterations/r3/publish/F1.exe -EvidencePath artifacts/f1-ui/iterations/r3/ui
