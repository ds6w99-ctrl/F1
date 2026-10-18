# Elevated: inspect Hyper-V module state and available snapshot cmdlets.
$log = 'G:\F1\artifacts\audit-baseline\vm-module-diag.log'
"diag $(Get-Date -Format o)" | Set-Content $log
try { Import-Module Hyper-V -Force -ErrorAction Stop 2>&1 | Out-String | Add-Content $log } catch { "import failed: $($_.Exception.Message)" | Add-Content $log }
Get-Module Hyper-V | Select-Object Name,Version,Path | Format-List | Out-String -Width 300 | Add-Content $log
Get-Command -Module Hyper-V -Name '*VM*' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name | Out-String | Add-Content $log
Get-Command Revert-VM, Restore-VMSnapshot, Checkpoint-VM -ErrorAction SilentlyContinue | Select-Object Name,Source | Out-String | Add-Content $log
$PSVersionTable.PSVersion | Out-String | Add-Content $log
