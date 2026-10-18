# Elevated: inspect Default Switch / host networking for the VM.
$out = 'G:\F1\artifacts\audit-baseline\vswitch-state.txt'
"recorded $(Get-Date -Format o)" | Set-Content $out
Get-Service vmms, vmcompute, SharedAccess | Select-Object Name,Status,StartType | Format-Table -AutoSize | Out-String -Width 200 | Add-Content $out
Get-VMSwitch | Select-Object Name,SwitchType,NetAdapterInterfaceDescription | Format-Table -AutoSize | Out-String -Width 200 | Add-Content $out
Get-NetIPAddress -InterfaceAlias 'vEthernet (Default Switch)' -ErrorAction SilentlyContinue | Select-Object IPAddress | Out-String | Add-Content $out
Get-NetNat -ErrorAction SilentlyContinue | Select-Object Name,InternalIPInterfaceAddressPrefix | Format-Table -AutoSize | Out-String -Width 200 | Add-Content $out
(Get-VM -Name 'F1-Clean-Win11').NetworkAdapters | ForEach-Object { "adapter status=$($_.Status)" } | Add-Content $out
