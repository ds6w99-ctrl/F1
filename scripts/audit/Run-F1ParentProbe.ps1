# Host-side: run the F1ParentTests parent-identity security probe (elevated).
# accept-parent layout: probe renamed to F1.exe inside App\, next to the real ElevatedHost.
# reject-parent layout: probe keeps its own name; the helper must refuse the connection (exit 5).
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$probeBin = Join-Path $root 'artifacts\audit-tools\F1ParentProbe'
$helperSrc = Join-Path $root 'artifacts\publish\App\ElevatedHost'
if (!(Test-Path (Join-Path $probeBin 'F1ParentProbe.exe'))) { throw 'Publish the self-contained probe first: dotnet publish tests/SystemCore.F1ParentTests -c Release -r win-x64 --self-contained -o artifacts/audit-tools/F1ParentProbe (then revert the F1ParentTests packages.lock.json).' }
if (!(Test-Path (Join-Path $helperSrc 'SystemCore.ElevatedHost.exe'))) { throw 'Publish the app first (helper missing).' }

$stage = Join-Path $root 'artifacts\audit-tools\F1ParentStage'
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
$accept = Join-Path $stage 'accept\F1\App'
$reject = Join-Path $stage 'reject'
New-Item -ItemType Directory -Force $accept, (Join-Path $accept 'ElevatedHost'), $reject, (Join-Path $reject 'ElevatedHost') | Out-Null
Copy-Item (Join-Path $helperSrc '*') -Destination (Join-Path $accept 'ElevatedHost') -Recurse -Force
Copy-Item (Join-Path $helperSrc '*') -Destination (Join-Path $reject 'ElevatedHost') -Recurse -Force
Copy-Item (Join-Path $probeBin '*') -Destination $accept -Recurse -Force
Move-Item (Join-Path $accept 'F1ParentProbe.exe') (Join-Path $accept 'F1.exe') -Force
Move-Item (Join-Path $accept 'F1ParentProbe.runtimeconfig.json') (Join-Path $accept 'F1.runtimeconfig.json') -Force
Move-Item (Join-Path $accept 'F1ParentProbe.deps.json') (Join-Path $accept 'F1.deps.json') -Force
Copy-Item (Join-Path $probeBin '*') -Destination $reject -Recurse -Force

$out = Join-Path $stage 'results.txt'
$acceptHelper = Join-Path $accept 'ElevatedHost\SystemCore.ElevatedHost.exe'
$rejectHelper = Join-Path $reject 'ElevatedHost\SystemCore.ElevatedHost.exe'
$cases = @(
    @{ Name = 'cleaner accept-parent'; Probe = (Join-Path $accept 'F1.exe'); Helper = $acceptHelper; Endpoint = 'cleaner'; Mode = 'accept-parent' },
    @{ Name = 'startup accept-parent'; Probe = (Join-Path $accept 'F1.exe'); Helper = $acceptHelper; Endpoint = 'startup'; Mode = 'accept-parent' },
    @{ Name = 'cleaner reject-parent'; Probe = (Join-Path $reject 'F1ParentProbe.exe'); Helper = $rejectHelper; Endpoint = 'cleaner'; Mode = 'reject-parent' },
    @{ Name = 'startup reject-parent'; Probe = (Join-Path $reject 'F1ParentProbe.exe'); Helper = $rejectHelper; Endpoint = 'startup'; Mode = 'reject-parent' }
)
"F1ParentTests @ $(Get-Date -Format o)" | Set-Content $out
$allPass = $true
foreach ($case in $cases) {
    $line = & $case.Probe $case.Helper $case.Endpoint $case.Mode 2>&1 | Out-String
    $line = $line.Trim()
    $casePass = ($line -match '"Passed":true') -and ($case.Probe | ForEach-Object { $LASTEXITCODE -eq 0 } | Select-Object -First 1)
    # The probe exits 0 on pass; capture LASTEXITCODE before it is overwritten by Out-String pipeline.
    $code = $LASTEXITCODE
    "$($case.Name): exit=$code passed=$($line -match '"Passed":true') :: $line" | Add-Content $out
    if ($code -ne 0 -or $line -notmatch '"Passed":true') { $allPass = $false }
}
"ALL: $allPass" | Add-Content $out
Get-Content $out
if (-not $allPass) { exit 1 }
