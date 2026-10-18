# Elevated: clean re-apply of the audit-apps-installed checkpoint and a patient boot wait.
$log = 'G:\F1\artifacts\audit-baseline\vm-reapply.log'
"reapply $(Get-Date -Format o)" | Set-Content $log
$vm = Get-VM -Name 'F1-Clean-Win11'
"state: $($vm.State)" | Add-Content $log
if ($vm.State -ne 'Off') { Stop-VM -Name 'F1-Clean-Win11' -Force -ErrorAction Continue | Out-Null; Start-Sleep 6; "stopped" | Add-Content $log }
Revert-VM -Name 'F1-Clean-Win11' -SnapshotName 'audit-apps-installed' -ErrorAction Continue 2>&1 | Out-String | Add-Content $log
"reverted" | Add-Content $log
Start-VM -Name 'F1-Clean-Win11' 2>&1 | Out-String | Add-Content $log
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep 5
    $hb = (Get-VM -Name 'F1-Clean-Win11').Heartbeat
    if ($hb -eq 'OkApplicationsHealthy') { "healthy after ~$((5*($i+1)))s" | Add-Content $log; break }
    if ($i % 12 -eq 11) { "t=$((5*($i+1)))s heartbeat=$hb" | Add-Content $log }
}
"final heartbeat: $((Get-VM -Name 'F1-Clean-Win11').Heartbeat)" | Add-Content $log
(Get-VM -Name 'F1-Clean-Win11').NetworkAdapters | ForEach-Object { "IPs=$($_.IPAddresses -join ',')" } | Add-Content $log
