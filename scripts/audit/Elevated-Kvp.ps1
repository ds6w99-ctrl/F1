# Elevated: read guest KVP exchange data (tells whether the guest OS is fully booted).
$out = 'G:\F1\artifacts\audit-baseline\vm-kvp.txt'
"recorded $(Get-Date -Format o)" | Set-Content $out
$items = Get-CimInstance -Namespace root\virtualization\v2 -ClassName Msvm_KvpExchangeDataItem -ErrorAction SilentlyContinue
"total kvp items: $($items.Count)" | Add-Content $out
$items | Where-Object { $_.Source -eq 1 } | Select-Object Name, Data | Format-Table -AutoSize | Out-String -Width 200 | Add-Content $out
