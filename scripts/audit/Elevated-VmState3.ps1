# Elevated: report VM state, heartbeat, and network adapter IPs.
$out = 'G:\F1\artifacts\audit-baseline\vm-state3.txt'
"recorded $(Get-Date -Format o)" | Set-Content $out
Get-VM -Name 'F1-Clean-Win11' | Select-Object Name,State,Heartbeat,Uptime | Format-List | Out-String -Width 200 | Add-Content $out
(Get-VM -Name 'F1-Clean-Win11').NetworkAdapters | ForEach-Object { "Switch=$($_.SwitchName) Status=$($_.Status) IPs=$($_.IPAddresses -join ',')" } | Add-Content $out
