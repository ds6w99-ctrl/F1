param(
    [string]$VmName = 'F1-Clean-Win11',
    [string]$VhdPath = 'G:\VMs\F1Clean\F1-Clean-Win11.vhdx',
    [string]$IsoPath = 'G:\ISO\Win11Eval.iso',
    [string]$UnattendXml = 'G:\F1\scripts\vm\autounattend-offline.xml',
    [int]$ImageIndex = 1,
    [string]$LogPath = 'G:\F1\artifacts\vm-validation\offline-provision.log'
)
# Offline-provisions the clean VM's OS disk directly from the Windows 11 evaluation ISO:
#   mount VHDX -> GPT partitions (EFI/MSR/Windows) -> dism /Apply-Image -> bcdboot ->
#   drop the specialize+oobeSystem answer file -> boot from disk. This removes the DVD
#   "press any key to boot" prompt that blocks unattended installs. Run elevated.
$ErrorActionPreference = 'Stop'
function Log([string]$m) { $line = "[{0:HH:mm:ss}] {1}" -f (Get-Date), $m; $line | Tee-Object -FilePath $LogPath -Append | Out-Null; Write-Host $line }
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run elevated.' }
foreach ($p in @($VhdPath, $IsoPath, $UnattendXml)) { if (!(Test-Path $p)) { throw "Missing: $p" } }
if ('S', 'W' | Where-Object { Get-Volume -DriveLetter $_ -ErrorAction SilentlyContinue }) { throw 'Drive letters S or W are in use on the host.' }

Import-Module Hyper-V -ErrorAction Stop
Log "Stopping VM $VmName"
Stop-VM -Name $VmName -TurnOff -Force -ErrorAction SilentlyContinue

# Mount OS VHDX and partition it.
Log 'Mounting OS VHDX'
$disksBefore = @(Get-Disk | Select-Object -ExpandProperty Number)
Mount-VHD -Path $VhdPath | Out-Null
Start-Sleep -Seconds 3
$disk = Get-Disk | Where-Object { $_.Number -notin $disksBefore -and $_.BusType -eq 'File Backed Virtual' } | Select-Object -First 1
if (!$disk) { Dismount-VHD -Path $VhdPath; throw 'OS VHDX did not mount.' }
$dp = Join-Path $env:TEMP 'f1-offline-diskpart.txt'
@(
    "select disk $($disk.Number)", 'clean', 'convert gpt',
    'create partition efi size=300', 'format quick fs=fat32 label=System', 'assign letter=S',
    'create partition msr size=16',
    'create partition primary', 'format quick fs=ntfs label=Windows', 'assign letter=W',
    'exit'
) | Set-Content $dp -Encoding ascii
& diskpart.exe /s $dp | Out-Null
Start-Sleep -Seconds 2
if (!(Test-Path 'W:\') -or !(Test-Path 'S:\')) { throw 'Partitions S:/W: were not assigned.' }

# Mount ISO and apply the image.
Log 'Applying Windows 11 Enterprise Evaluation image'
$img = Mount-DiskImage -ImagePath $IsoPath -PassThru
$isoLetter = $null
for ($i = 0; $i -lt 30 -and !$isoLetter; $i++) { Start-Sleep -Milliseconds 500; $v = Get-Volume -DiskImage $img -ErrorAction SilentlyContinue; if ($v) { $isoLetter = $v.DriveLetter } }
if (!$isoLetter) { throw 'ISO did not mount.' }
$wim = "${isoLetter}:\sources\install.wim"
if (!(Test-Path $wim)) { throw "install.wim not found at $wim" }
& dism.exe /Apply-Image /ImageFile:$wim /Index:$ImageIndex /ApplyDir:W:\ /CheckIntegrity
if ($LASTEXITCODE -ne 0) { throw "dism /Apply-Image failed with $LASTEXITCODE" }
Log 'Image applied; creating boot files'
& bcdboot.exe W:\Windows /s S: /f UEFI /v
if ($LASTEXITCODE -ne 0) { throw "bcdboot failed with $LASTEXITCODE" }
New-Item -ItemType Directory -Force 'W:\Windows\Panther' | Out-Null
Copy-Item $UnattendXml 'W:\Windows\Panther\Unattend.xml' -Force
Log 'Answer file staged in the offline image'

# Clean up host-side mounts.
@('select volume S', 'remove letter=S', 'select volume W', 'remove letter=W', 'exit') | Set-Content $dp -Encoding ascii
& diskpart.exe /s $dp | Out-Null
Start-Sleep -Seconds 1
Dismount-VHD -Path $VhdPath | Out-Null
Dismount-DiskImage -ImagePath $IsoPath | Out-Null

# Boot from the disk (no DVD).
Log 'Configuring disk-first boot and starting VM'
$vhdName = Split-Path $VhdPath -Leaf
$diskDrv = Get-VMHardDiskDrive -VMName $VmName | Where-Object { (Split-Path $_.Path -Leaf) -eq $vhdName } | Select-Object -First 1
if (-not $diskDrv) { throw "Disk drive for $vhdName not found on $VmName" }
Set-VMFirmware -VMName $VmName -FirstBootDevice $diskDrv
Start-VM -Name $VmName
Log 'Provisioned VM started; first boot will run specialize/OOBE and enable WinRM'
