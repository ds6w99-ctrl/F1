$ErrorActionPreference='Stop'
# Run from an elevated diagnostics harness when required by the desktop-token
# launcher. Each native UI / stability controller is a separate limited process.
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Build-Phase8.ps1))))
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Invoke-DesktopTokenValidation.ps1)))) -ScriptPath scripts/Test-ThemeLocalization.ps1 -LogPath artifacts/phase8/completion/theme-controller.txt
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Invoke-DesktopTokenValidation.ps1)))) -ScriptPath scripts/Invoke-Phase8UiValidation.ps1 -LogPath artifacts/phase8/completion/ui-controller.txt
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Invoke-DesktopTokenValidation.ps1)))) -ScriptPath scripts/Invoke-Phase8Stability.ps1 -LogPath artifacts/phase8/completion/stability-controller.txt
& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Write-Phase8Summary.ps1))))
