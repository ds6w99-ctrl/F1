# Elevated: report both VMs' state and IPs.
Import-Module Hyper-V -ErrorAction Continue
$out = 'G:\F1\artifacts\audit-baseline\vm-state5.txt'
"recorded $(Get-Date -Format o)" | Set-Content $out
Get-VM | Select-Object Name,State,Uptime | Format-Table -AutoSize | Out-String -Width 200 | Add-Content $out
foreach ($vm in Get-VM) {
    "---- $($vm.Name) ----" | Add-Content $out
    $vm.NetworkAdapters | ForEach-Object { "IPs=$($_.IPAddresses -join ',')" } | Add-Content $out
    "Heartbeat: $($vm.Heartbeat)" | Add-Content $out
}
