# Elevated: checkpoint the audit VM at round 0 (audit starting state).
$ErrorActionPreference = 'Continue'
$log = 'G:\F1\artifacts\audit-baseline\vm-checkpoint.log'
"checkpoint audit-round0 attempted $(Get-Date -Format o)" | Set-Content $log
$vm = Get-VM -Name 'F1-Clean-Win11'
if (-not $vm) { "VM not found" | Add-Content $log; exit 1 }
Checkpoint-VM -Name 'F1-Clean-Win11' -SnapshotName 'audit-round0' -ErrorAction Continue 2>&1 | Out-String | Add-Content $log
Get-VMCheckpoint -VMName 'F1-Clean-Win11' | Select-Object Name,CreationTime | Format-Table -AutoSize | Out-String -Width 200 | Add-Content $log
