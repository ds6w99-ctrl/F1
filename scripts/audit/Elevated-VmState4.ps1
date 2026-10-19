# Elevated: report VM state only.
Import-Module Hyper-V -ErrorAction Continue
$out = 'G:\F1\artifacts\audit-baseline\vm-state4.txt'
"recorded $(Get-Date -Format o)" | Set-Content $out
Get-VM | Select-Object Name,State,Uptime | Format-Table -AutoSize | Out-String -Width 200 | Add-Content $out
(Get-VM -Name 'F1-Clean-Win11').NetworkAdapters | ForEach-Object { "IPs=$($_.IPAddresses -join ',')" } | Add-Content $out
Get-Service vmms, vmcompute | Select-Object Name,Status | Format-Table -AutoSize | Out-String -Width 200 | Add-Content $out
