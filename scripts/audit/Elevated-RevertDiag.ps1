# Elevated: investigate the VM's disk topology and run an explicit revert with full output.
Import-Module Hyper-V -ErrorAction Continue
$log = 'G:\F1\artifacts\audit-baseline\vm-revertdiag.log'
"diag $(Get-Date -Format o)" | Set-Content $log
"--- disks ---" | Add-Content $log
Get-VMHardDiskDrive -VMName 'F1-Clean-Win11' | Select-Object Path,ControllerType,ControllerNumber,ControllerLocation | Format-List | Out-String -Width 300 | Add-Content $log
"--- checkpoints ---" | Add-Content $log
Get-VMCheckpoint -VMName 'F1-Clean-Win11' | Select-Object Name,Id,ParentSnapshotName | Format-List | Out-String -Width 300 | Add-Content $log
"--- vm state ---" | Add-Content $log
(Get-VM -Name 'F1-Clean-Win11').State | Add-Content $log
"--- explicit revert attempt ---" | Add-Content $log
try {
    $err = $null
    Revert-VM -Name 'F1-Clean-Win11' -SnapshotName 'audit-apps-installed' -ErrorAction Stop -ErrorVariable err 2>&1 | Out-String | Add-Content $log
    if ($err) { "error variable: $err" | Add-Content $log }
    "revert reported OK" | Add-Content $log
} catch {
    "revert THREW: $($_.Exception.Message)" | Add-Content $log
}
