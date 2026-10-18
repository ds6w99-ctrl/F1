# Elevated: revert the VM to audit-apps-installed and boot it (re-arms all 8 apps for a round).
$log = 'G:\F1\artifacts\audit-baseline\vm-revert.log'
"revert run $(Get-Date -Format o)" | Set-Content $log
$vm = Get-VM -Name 'F1-Clean-Win11'
if ($vm.State -ne 'Off') { Stop-VM -Name 'F1-Clean-Win11' -Force -ErrorAction Continue | Out-Null; Start-Sleep 5 }
Revert-VM -Name 'F1-Clean-Win11' -SnapshotName 'audit-apps-installed' -ErrorAction Continue 2>&1 | Out-String | Add-Content $log
Start-VM -Name 'F1-Clean-Win11' 2>&1 | Out-String | Add-Content $log
$ok = $false
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep 5
    if ((Get-VM -Name 'F1-Clean-Win11').Heartbeat -eq 'Ok') { $ok = $true; "heartbeat Ok after ~$((5*($i+1)))s" | Add-Content $log; break }
}
if (-not $ok) { "heartbeat NOT Ok within 200s" | Add-Content $log }
Start-Sleep 15
"state after: $((Get-VM -Name 'F1-Clean-Win11').State)" | Add-Content $log
