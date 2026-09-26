param(
    [string]$Dotnet = 'dotnet',
    [switch]$UpdateLocks
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    New-Item -ItemType Directory -Force 'artifacts/logs' | Out-Null
    $sdk = & $Dotnet --version
    if ($LASTEXITCODE -ne 0 -or $sdk.Trim() -ne '10.0.401') { throw 'Pinned SDK 10.0.401 is required.' }
    $restoreArgs = @('restore', 'SystemCore.sln')
    if (!$UpdateLocks) { $restoreArgs += '--locked-mode' }
    & $Dotnet @restoreArgs 2>&1 | Tee-Object 'artifacts/logs/restore.txt'
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed; build stopped.' }
    & $Dotnet build SystemCore.sln -c Release --no-restore 2>&1 | Tee-Object 'artifacts/logs/build.txt'
    if ($LASTEXITCODE -ne 0) { throw 'Build failed; tests/publish stopped.' }
    & $Dotnet run --project tests/SystemCore.UnitTests -c Release --no-build 2>&1 | Tee-Object 'artifacts/logs/unit-tests.txt'
    if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed; publish stopped.' }
    foreach ($project in @('App', 'ElevatedHost')) {
        $destination = if ($project -eq 'App') { 'artifacts/publish/App' } else { 'artifacts/publish/App/ElevatedHost' }
        & $Dotnet publish "src/SystemCore.$project" -c Release --no-restore -p:CleanerValidation=false -o $destination 2>&1 |
            Tee-Object "artifacts/logs/publish-$project.txt"
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
    }
    if (!(Test-Path 'artifacts/publish/App/F1.pri')) {
        throw 'Published WinUI resource index is missing; publication is not runnable.'
    }
    & $Dotnet run --project tests/SystemCore.CompatibilityTests -c Release --no-build -- $root 2>&1 |
        Tee-Object 'artifacts/logs/compatibility-tests.txt'
    if ($LASTEXITCODE -ne 0) { throw 'Current-host compatibility tests failed.' }
    & $Dotnet run --project tests/SystemCore.SystemInformationTests -c Release --no-build 2>&1 |
        Tee-Object 'artifacts/logs/system-information-tests.txt'
    if ($LASTEXITCODE -ne 0) { throw 'Current-host system information tests failed.' }
    & $Dotnet run --project tests/SystemCore.PerformanceTests -c Release --no-build 2>&1 |
        Tee-Object 'artifacts/logs/performance-tests.txt'
    if ($LASTEXITCODE -ne 0) { throw 'Current-host performance tests failed.' }
    foreach ($mode in @('', '--executor', '--completion')) {
        $arguments = @('run', '--project', 'tests/SystemCore.CleanerTests', '-c', 'Release', '--no-build')
        if ($mode) { $arguments += @('--', $mode) }
        & $Dotnet @arguments 2>&1 | Tee-Object "artifacts/logs/cleaner$mode.txt"
        if ($LASTEXITCODE -ne 0) { throw "Cleaner validation failed: $mode" }
    }
    Get-ChildItem 'artifacts/publish' -File -Recurse | ForEach-Object {
        [pscustomobject]@{
            Path = $_.FullName.Substring((Join-Path $root 'artifacts/publish').Length + 1)
            Length = $_.Length
            SHA256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
        }
    } | ConvertTo-Json -Depth 4 | Set-Content 'artifacts/publish-inventory.json' -Encoding UTF8
} finally { Pop-Location }
