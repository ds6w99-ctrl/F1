# Elevated: report VM + checkpoint state only.
$out = 'G:\F1\artifacts\audit-baseline\vm-state2.txt'
"recorded $(Get-Date -Format o)" | Set-Content $out
Get-VM | Select-Object Name,State,@{n='Uptime';e={$_.Uptime}} | Format-Table -AutoSize | Out-String -Width 200 | Add-Content $out
Get-VMCheckpoint -VMName 'F1-Clean-Win11' | Select-Object Name,CreationTime,ParentSnapshotName | Format-Table -AutoSize | Out-String -Width 200 | Add-Content $out
