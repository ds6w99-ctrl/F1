# Elevated: enable checkpoints on the audit VM, boot, and take the round-0 checkpoint.
$ErrorActionPreference = 'Continue'
$log = 'G:\F1\artifacts\audit-baseline\vm-checkpoint.log'
"enable-checkpoint run $(Get-Date -Format o)" | Set-Content $log
$vm = Get-VM -Name 'F1-Clean-Win11'
if (-not $vm) { "VM not found" | Add-Content $log; exit 1 }
"state before: $($vm.State)" | Add-Content $log
if ($vm.State -ne 'Off') { Stop-VM -Name 'F1-Clean-Win11' -Force -ErrorAction Continue | Out-Null; Start-Sleep 3 }
Set-VM -Name 'F1-Clean-Win11' -CheckpointType Standard 2>&1 | Out-String | Add-Content $log
"checkpoint type now: $((Get-VM -Name 'F1-Clean-Win11').CheckpointType)" | Add-Content $log
Start-VM -Name 'F1-Clean-Win11' 2>&1 | Out-String | Add-Content $log
# wait for boot: heartbeat Ok
$ok = $false
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep 5
    $hb = (Get-VM -Name 'F1-Clean-Win11').Heartbeat
    if ($hb -eq 'Ok') { $ok = $true; "heartbeat Ok after ~$((5*($i+1)))s" | Add-Content $log; break }
}
if (-not $ok) { "heartbeat NOT Ok within 200s" | Add-Content $log }
Start-Sleep 20  # let autologon settle
Checkpoint-VM -Name 'F1-Clean-Win11' -SnapshotName 'audit-round0' 2>&1 | Out-String | Add-Content $log
Get-VMCheckpoint -VMName 'F1-Clean-Win11' | Select-Object Name,CreationTime | Format-Table -AutoSize | Out-String -Width 200 | Add-Content $log
"state after: $((Get-VM -Name 'F1-Clean-Win11').State)" | Add-Content $log
