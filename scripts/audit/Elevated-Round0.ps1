# Elevated: reapply the pristine audit-round0 checkpoint and boot.
$log = 'G:\F1\artifacts\audit-baseline\vm-round0.log'
"round0 $(Get-Date -Format o)" | Set-Content $log
$vm = Get-VM -Name 'F1-Clean-Win11'
"state: $($vm.State)" | Add-Content $log
if ($vm.State -ne 'Off') { Stop-VM -Name 'F1-Clean-Win11' -Force -ErrorAction Continue | Out-Null; Start-Sleep 6 }
Revert-VM -Name 'F1-Clean-Win11' -SnapshotName 'audit-round0' -ErrorAction Continue 2>&1 | Out-String | Add-Content $log
"reverted" | Add-Content $log
Start-VM -Name 'F1-Clean-Win11' 2>&1 | Out-String | Add-Content $log
for ($i = 0; $i -lt 48; $i++) {
    Start-Sleep 5
    $hb = (Get-VM -Name 'F1-Clean-Win11').Heartbeat
    if ($hb -eq 'OkApplicationsHealthy') { "healthy after ~$((5*($i+1)))s" | Add-Content $log; break }
}
"final heartbeat: $((Get-VM -Name 'F1-Clean-Win11').Heartbeat)" | Add-Content $log
(Get-VM -Name 'F1-Clean-Win11').NetworkAdapters | ForEach-Object { "IPs=$($_.IPAddresses -join ',')" } | Add-Content $log
