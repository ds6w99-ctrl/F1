param([switch]$DedicatedTestMachine)
$ErrorActionPreference = 'Stop'
if (!$DedicatedTestMachine) { throw 'Run only in a disposable VM or dedicated test environment; pass -DedicatedTestMachine.' }
# The probe accepts no input and performs no writes. Install/copy its complete publish
# folder to this fixed protected location from an administrator session in the VM first.
$exe = Join-Path $env:ProgramFiles 'SystemCore-Phase0\ElevatedHost\SystemCore.ElevatedHost.exe'
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Missing probe: $exe" }
$process = $null
try {
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.UseShellExecute = $true
    $start.Verb = 'runas'
    $start.WorkingDirectory = Split-Path $exe -Parent
    $process = [Diagnostics.Process]::Start($start)
    if ($null -eq $process) { throw 'No process handle returned; outcome unknown.' }
    $null = $process.Handle
    if (!$process.WaitForExit(30000)) {
        Write-Output 'TIMEOUT: probe outcome unknown. No retry and no forced termination.'
        exit 2
    }
    if ($null -eq $process.ExitCode) { throw 'Exit code unavailable; do not interpret as success.' }
    Write-Output "Probe exit code: $($process.ExitCode) (0=elevated token observed, 5=not elevated, 64=invalid input)."
    exit $process.ExitCode
} catch {
    $errorObject = $_.Exception
    while ($null -ne $errorObject) {
        if ($errorObject -is [System.ComponentModel.Win32Exception] -and $errorObject.NativeErrorCode -eq 1223) {
            Write-Output 'CANCELLED: user declined UAC. No retry.'
            exit 3
        }
        $errorObject = $errorObject.InnerException
    }
    throw
} finally {
    if ($null -ne $process) { $process.Dispose() }
}
