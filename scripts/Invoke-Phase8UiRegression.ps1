$ErrorActionPreference='Stop'
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Invoke-Phase8UiValidation.ps1)))) -RegressionOnly
