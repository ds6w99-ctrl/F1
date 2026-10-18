# Baseline console-runner sweep for the audit. Each suite logs separately.
$ErrorActionPreference = 'Continue'
$dotnet = 'C:\Users\Falcone\.dotnet\dotnet.exe'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$out = Join-Path $root 'artifacts\audit-baseline\tests'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$summary = Join-Path $out 'summary.txt'
"" | Set-Content $summary

function Invoke-Runner {
    param([string]$Name, [string]$Project, [string[]]$Extra = @())
    $log = Join-Path $out "$Name.log"
    Write-Host "===== $Name ====="
    $p = Join-Path $root $Project
    $argv = @('run','--project',"$p",'-c','Release')
    if ($Extra.Count -gt 0) { $argv += '--'; $argv += $Extra }
    & $dotnet @argv *>&1 | Tee-Object -FilePath $log | Out-Null
    $code = $LASTEXITCODE
    "{0} : exit {1}" -f $Name, $code | Tee-Object -FilePath $summary -Append
    # expose a pass/fail tail line
    Get-Content $log | Select-Object -Last 3 | ForEach-Object { Write-Host "   $_" }
}

Invoke-Runner -Name 'unit'           -Project 'tests\SystemCore.UnitTests'
Invoke-Runner -Name 'compatibility'  -Project 'tests\SystemCore.CompatibilityTests'
Invoke-Runner -Name 'security'       -Project 'tests\SystemCore.SecurityTests'
Invoke-Runner -Name 'reliability'    -Project 'tests\SystemCore.ReliabilityTests'
Invoke-Runner -Name 'product'        -Project 'tests\SystemCore.ProductTests'
Invoke-Runner -Name 'f1parent'       -Project 'tests\SystemCore.F1ParentTests'
Invoke-Runner -Name 'systeminfo'     -Project 'tests\SystemCore.SystemInformationTests'
Invoke-Runner -Name 'performance'    -Project 'tests\SystemCore.PerformanceTests'
Invoke-Runner -Name 'uninstaller'    -Project 'tests\SystemCore.UninstallerTests' -Extra @('--synthetic','--leftover-delete')
Invoke-Runner -Name 'cleaner-default'      -Project 'tests\SystemCore.CleanerTests'
Invoke-Runner -Name 'cleaner-executor'     -Project 'tests\SystemCore.CleanerTests' -Extra @('--executor')
Invoke-Runner -Name 'cleaner-completion'   -Project 'tests\SystemCore.CleanerTests' -Extra @('--completion')
Invoke-Runner -Name 'cleaner-scanperf'     -Project 'tests\SystemCore.CleanerTests' -Extra @('--scan-performance')
Invoke-Runner -Name 'startup-default'      -Project 'tests\SystemCore.StartupTests'
Invoke-Runner -Name 'startup-mutation'     -Project 'tests\SystemCore.StartupTests' -Extra @('--mutation')
Invoke-Runner -Name 'memory-default'       -Project 'tests\SystemCore.MemoryToolsTests'
Invoke-Runner -Name 'memory-integration'   -Project 'tests\SystemCore.MemoryToolsTests' -Extra @('--integration')

Write-Host '===== SUMMARY ====='
Get-Content $summary
