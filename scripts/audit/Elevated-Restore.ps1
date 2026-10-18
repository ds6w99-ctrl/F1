# Elevated: properly restore the audit-apps-installed checkpoint (Restore-VMSnapshot).
Import-Module Hyper-V -ErrorAction Stop
$log = 'G:\F1\artifacts\audit-baseline\vm-restore.log'
"restore $(Get-Date -Format o)" | Set-Content $log
$vm = Get-VM -Name 'F1-Clean-Win11'
"state: $($vm.State)" | Add-Content $log
if ($vm.State -ne 'Off') { Stop-VM -Name 'F1-Clean-Win11' -Force -ErrorAction Continue | Out-Null; Start-Sleep 6; "stopped" | Add-Content $log }
Restore-VMSnapshot -VMName 'F1-Clean-Win11' -Name 'audit-apps-installed' -Confirm:$false 2>&1 | Out-String | Add-Content $log
"restore reported OK" | Add-Content $log
Start-VM -Name 'F1-Clean-Win11' 2>&1 | Out-String | Add-Content $log
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep 5
    $hb = (Get-VM -Name 'F1-Clean-Win11').Heartbeat
    if ($hb -eq 'OkApplicationsHealthy') { "healthy after ~$((5*($i+1)))s" | Add-Content $log; break }
}
"final heartbeat: $((Get-VM -Name 'F1-Clean-Win11').Heartbeat)" | Add-Content $log
(Get-VM -Name 'F1-Clean-Win11').NetworkAdapters | ForEach-Object { "IPs=$($_.IPAddresses -join ',')" } | Add-Content $log
