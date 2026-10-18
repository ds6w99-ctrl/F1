# Elevated: checkpoint the VM with the full audit app set installed (rerun point for rounds).
$log = 'G:\F1\artifacts\audit-baseline\vm-checkpoint2.log'
"checkpoint audit-apps-installed $(Get-Date -Format o)" | Set-Content $log
$vm = Get-VM -Name 'F1-Clean-Win11'
if ($vm.State -ne 'Running') { "VM not running: $($vm.State)" | Add-Content $log; exit 1 }
Checkpoint-VM -Name 'F1-Clean-Win11' -SnapshotName 'audit-apps-installed' 2>&1 | Out-String | Add-Content $log
Get-VMCheckpoint -VMName 'F1-Clean-Win11' | Select-Object Name,CreationTime | Format-Table -AutoSize | Out-String -Width 200 | Add-Content $log
