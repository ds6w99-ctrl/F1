param(
    [string]$ExePath,
    [string]$ResultFile = 'C:\F1Evidence\launch-result.json',
    [int]$TimeoutSeconds = 60
)
# Runs in the interactive console session (via a logon-triggered task). Launches F1, waits for a
# real window, records the result, and closes the app. The window title proves a genuine UI launch.
$result = [ordered]@{ Ok = $false; Title = ''; Reason = '' }
try {
    $p = Start-Process $ExePath -PassThru
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do { Start-Sleep -Milliseconds 300; $p.Refresh() } while (!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $clock.Elapsed.TotalSeconds -lt $TimeoutSeconds)
    if ($p.HasExited) {
        $result.Reason = "exited code $($p.ExitCode)"
    }
    elseif ($p.MainWindowHandle -eq [IntPtr]::Zero) {
        $p.Kill(); $null = $p.WaitForExit(5000)
        $result.Reason = 'no window within timeout'
    }
    else {
        $result.Ok = $true
        $result.Title = $p.MainWindowTitle
        $null = $p.CloseMainWindow(); $null = $p.WaitForExit(15000)
        if (!$p.HasExited) { $p.Kill(); $null = $p.WaitForExit(5000) }
    }
}
catch {
    $result.Reason = $_.Exception.Message
}
$result | ConvertTo-Json | Set-Content $ResultFile -Encoding utf8
