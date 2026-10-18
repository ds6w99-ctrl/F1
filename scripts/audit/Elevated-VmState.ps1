# Elevated: report VM and checkpoint state (read-only).
$ErrorActionPreference = 'Continue'
$out = 'G:\F1\artifacts\audit-baseline\vm-state.txt'
"recorded $(Get-Date -Format o)" | Set-Content $out
"" | Add-Content $out
Get-VM | Select-Object Name,State,Generation,@{n='CPU';e={$_.ProcessorCount}},@{n='RAM_MB';e={$_.MemoryAssigned/1MB}},@{n='Uptime';e={$_.Uptime}} | Format-Table -AutoSize | Out-String -Width 200 | Add-Content $out
foreach ($vm in Get-VM) {
    "---- VM $($vm.Name) network ----" | Add-Content $out
    $vm.NetworkAdapters | ForEach-Object { "Switch=$($_.SwitchName) IPs=$($_.IPAddresses -join ',')" } | Add-Content $out
    "---- VM $($vm.Name) checkpoints ----" | Add-Content $out
    Get-VMCheckpoint -VMName $vm.Name -ErrorAction SilentlyContinue | Select-Object Name,CheckpointType,CreationTime | Format-Table -AutoSize | Out-String -Width 200 | Add-Content $out
}
"---- hyper-v services ----" | Add-Content $out
Get-Service vmms, vmcompute | Select-Object Name,Status | Format-Table -AutoSize | Out-String -Width 200 | Add-Content $out
