# Elevated: offline-inspect the guest's current disk and registry (SYSTEM + SOFTWARE hives).
$log = 'G:\F1\artifacts\audit-baseline\vm-offline.log'
"offline $(Get-Date -Format o)" | Set-Content $log
$vm = Get-VM -Name 'F1-Clean-Win11'
if ($vm.State -ne 'Off') { Stop-VM -Name 'F1-Clean-Win11' -Force -ErrorAction Continue | Out-Null; Start-Sleep 5; "stopped" | Add-Content $log }
$path = (Get-VMHardDiskDrive -VMName 'F1-Clean-Win11' | Select-Object -First 1).Path
"current disk: $path" | Add-Content $log
$mounted = Mount-VHD -Path $path -PassThru -ErrorAction Continue
if (-not $mounted) { "mount failed" | Add-Content $log; exit 1 }
try {
    $partitions = Get-Partition -DiskNumber $mounted.DiskNumber -ErrorAction SilentlyContinue
    $partitions | Select-Object PartitionNumber,DriveLetter,Size | Out-String | Add-Content $log
    $win = $partitions | Where-Object { $_.Size -gt 20GB } | Select-Object -First 1
    if (-not $win.DriveLetter) {
        $free = [char[]]([int][char]'K'..[int][char]'Z') | Where-Object { -not (Get-Partition -DriveLetter $_) -and -not (Get-PSDrive -Name $_) } | Select-Object -First 1
        Set-Partition -DiskNumber $mounted.DiskNumber -PartitionNumber $win.PartitionNumber -NewDriveLetter $free | Out-Null
        Start-Sleep 2
        $win = Get-Partition -DiskNumber $mounted.DiskNumber -PartitionNumber $win.PartitionNumber
        "assigned letter: $($win.DriveLetter)" | Add-Content $log
    }
    if (-not $win.DriveLetter) { "no letter" | Add-Content $log; exit 1 }
    $root = "$($win.DriveLetter):\"
    if (-not (Test-Path "$root\Windows\System32\config\SYSTEM")) { "not a windows volume" | Add-Content $log; exit 1 }
    reg load HKLM\OFF_SYS "$root\Windows\System32\config\SYSTEM" 2>&1 | Out-String | Add-Content $log
    reg load HKLM\OFF_SOFT "$root\Windows\System32\config\SOFTWARE" 2>&1 | Out-String | Add-Content $log
    $cs = Get-ItemProperty 'HKLM:\OFF_SYS\ControlSet001\Services\CurrentControlSet' -ErrorAction SilentlyContinue
    "computer: $((Get-ItemProperty 'HKLM:\OFF_SYS\ControlSet001\Control\ComputerName\ComputerName' -ErrorAction SilentlyContinue).ComputerName)" | Add-Content $log
    $pol = Get-ItemProperty 'HKLM:\OFF_SOFT\Microsoft\Windows\CurrentVersion\Policies\System' -ErrorAction SilentlyContinue
    "EnableLUA: $($pol.EnableLUA)" | Add-Content $log
    "ConsentPromptBehaviorAdmin: $($pol.ConsentPromptBehaviorAdmin)" | Add-Content $log
    "LocalAccountTokenFilterPolicy: $($pol.LocalAccountTokenFilterPolicy)" | Add-Content $log
    $wl = Get-ItemProperty 'HKLM:\OFF_SOFT\Microsoft\Windows NT\CurrentVersion\Winlogon' -ErrorAction SilentlyContinue
    "AutoAdminLogon: $($wl.AutoAdminLogon) DefaultUserName: $($wl.DefaultUserName)" | Add-Content $log
    "CurrentBuild: $((Get-ItemProperty 'HKLM:\OFF_SOFT\Microsoft\Windows NT\CurrentVersion' -ErrorAction SilentlyContinue).CurrentBuildNumber)" | Add-Content $log
    "EditionID: $((Get-ItemProperty 'HKLM:\OFF_SOFT\Microsoft\Windows NT\CurrentVersion' -ErrorAction SilentlyContinue).EditionID)" | Add-Content $log
    reg unload HKLM\OFF_SYS 2>&1 | Out-String | Add-Content $log
    reg unload HKLM\OFF_SOFT 2>&1 | Out-String | Add-Content $log
}
finally {
    Dismount-VHD -Path $path -ErrorAction SilentlyContinue | Out-Null
    "dismounted" | Add-Content $log
}
