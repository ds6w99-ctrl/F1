$ErrorActionPreference='Stop'
$failures=[Collections.Generic.List[string]]::new()
try{& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Invoke-F1ProductUx.ps1)))) -EvidencePath artifacts/f1-ui/test-results/ui-03}catch{$failures.Add("Product UI: $_");Write-Output $_}
try{& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-F1Visual.ps1))))}catch{$failures.Add("Visual runtime: $_");Write-Output $_}
try{& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Invoke-F1PageRegression.ps1))))}catch{$failures.Add("Page regression: $_");Write-Output $_}
$failures|ConvertTo-Json|Set-Content artifacts/f1-ui/test-results/remaining-failures.json -Encoding utf8
if($failures.Count){throw ($failures -join '; ')}
