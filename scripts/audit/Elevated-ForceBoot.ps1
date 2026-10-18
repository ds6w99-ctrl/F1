# Elevated: force-restart the audit VM (clean boot after revert) and wait for full readiness.
$log = 'G:\F1\artifacts\audit-baseline\vm-forceboot.log'
"forceboot $(Get-Date -Format o)" | Set-Content $log
$vm = Get-VM -Name 'F1-Clean-Win11'
"state: $($vm.State)" | Add-Content $log
if ($vm.State -ne 'Off') { Stop-VM -Name 'F1-Clean-Win11' -Force -ErrorAction Continue | Out-Null; Start-Sleep 5; "stopped" | Add-Content $log }
Start-VM -Name 'F1-Clean-Win11' 2>&1 | Out-String | Add-Content $log
$ok = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep 5
    $vm2 = Get-VM -Name 'F1-Clean-Win11'
    if ($vm2.Heartbeat -eq 'OkApplicationsHealthy') { "healthy after ~$((5*($i+1)))s" | Add-Content $log; $ok = $true; break }
}
if (-not $ok) { "not fully healthy within 300s; heartbeat=$((Get-VM -Name 'F1-Clean-Win11').Heartbeat)" | Add-Content $log }
(Get-VM -Name 'F1-Clean-Win11').NetworkAdapters | ForEach-Object { "Switch=$($_.SwitchName) IPs=$($_.IPAddresses -join ',')" } | Add-Content $log
