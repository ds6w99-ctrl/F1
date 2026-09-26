$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Invoke-Phase7Completion.ps1))
$start=$source.IndexOf('& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-MemoryToolsSyntheticPage.ps1))))')
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
$sdk="$env:DOTNET_ROOT\dotnet.exe";$evidence='artifacts/phase7/completion'
& ([scriptblock]::Create($source.Substring($start)))
