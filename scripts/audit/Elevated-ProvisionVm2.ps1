# Elevated: create the retail VM and offline-provision Windows 11 Pro from the official ISO.
Import-Module Hyper-V -ErrorAction Stop
$log = 'G:\F1\artifacts\audit-baseline\vm2-provision.log'
"vm2 provision $(Get-Date -Format o)" | Set-Content $log
function L([string]$m) { $line = "[{0:HH:mm:ss}] {1}" -f (Get-Date), $m; $line | Tee-Object -FilePath $log -Append | Out-Null; Write-Host $line }

$vmName = 'F1-Retail-Win11'
$vhdDir = 'G:\VMs\F1Retail'
$vhdPath = Join-Path $vhdDir 'F1-Retail-Win11.vhdx'
New-Item -ItemType Directory -Force $vhdDir | Out-Null

if (Get-VM -Name $vmName -ErrorAction SilentlyContinue) { L "VM exists; removing"; Stop-VM -Name $vmName -TurnOff -Force -ErrorAction SilentlyContinue; Remove-VM -Name $vmName -Force }
if (Test-Path $vhdPath) { Remove-Item $vhdPath -Force }
New-VHD -Path $vhdPath -SizeBytes 64GB -Dynamic | Out-Null
L "VHD created"
New-VM -Name $vmName -Generation 2 -MemoryStartupBytes 2048MB -VHDPath $vhdPath -SwitchName 'Default Switch' | Out-Null
Set-VM -Name $vmName -ProcessorCount 4 -AutomaticCheckpointsEnabled $false
Set-VMFirmware -VMName $vmName -EnableSecureBoot Off
L "VM created"
# boot order: disk only (offline provision applies the image to the disk)
& 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -ExecutionPolicy Bypass -File 'G:\F1\scripts\Invoke-F1VmOfflineProvision.ps1' -VmName $vmName -VhdPath $vhdPath -IsoPath 'G:\ISO\Win11_Client_x64_en-us_26300.iso' -UnattendXml 'G:\F1\scripts\vm\autounattend-offline-retail.xml' -ImageIndex 6 -LogPath 'G:\F1\artifacts\audit-baseline\vm2-provision-inner.log' 2>&1 | Out-String | Add-Content $log
L "provision script exit: $LASTEXITCODE"
