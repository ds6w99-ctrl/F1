param(
    [string]$VmName = 'F1-Clean-Win11',
    [string]$IsoPath = 'G:\ISO\Win11Eval.iso',
    [string]$VmRoot = 'G:\VMs\F1Clean',
    [string]$AnswerXml = 'G:\F1\scripts\vm\autounattend.xml',
    [string]$SetupExe = 'G:\F1\artifacts\release-1.0.0\F1-1.0.0-win-x64-setup.exe',
    [string]$PortableZip = 'G:\F1\artifacts\release-1.0.0\F1-1.0.0-win-x64-portable.zip',
    [string]$AcceptanceScript = 'G:\F1\scripts\Test-F1VmAcceptance.ps1',
    [string]$EvidencePath = 'G:\F1\artifacts\vm-validation',
    [string]$AdminUser = 'F1Admin',
    [string]$AdminPass = 'F1Vm!2026',
    [int]$WinRmTimeoutSeconds = 3600,
    [switch]$SkipCreate,
    [switch]$SkipAcceptance,
    [switch]$RemoveVm,
    [switch]$ForceRecreate
)
# Host-side orchestrator for clean-machine (VM) release validation.
# MUST run elevated (Hyper-V cmdlets + host TrustedHosts). One elevated run creates the VM,
# performs an unattended Windows 11 install from the evaluation ISO, waits for WinRM, copies
# the release artifacts and the acceptance kit, runs the acceptance inside the VM, and copies
# the evidence back to the host. Re-runnable: an existing VM is reused unless -ForceRecreate.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
New-Item -ItemType Directory -Force $EvidencePath | Out-Null
$log = Join-Path $EvidencePath 'vm-validation.log'
function Log([string]$message) { $line = "[{0:HH:mm:ss}] {1}" -f (Get-Date), $message; $line | Tee-Object -FilePath $log -Append | Out-Null; Write-Host $line }

# ---------- preconditions ----------
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script from an elevated PowerShell (Hyper-V cmdlets require administrator).' }
Import-Module Hyper-V -ErrorAction Stop
if (!(Get-Service vmms -ErrorAction SilentlyContinue)) { throw 'Hyper-V VMMS service is not registered; a reboot after enabling Hyper-V is required.' }
if ((Get-Service vmms).Status -ne 'Running') { Start-Service vmms }
if (!(Test-Path $IsoPath)) { throw "ISO not found: $IsoPath" }
foreach ($required in @($SetupExe, $PortableZip, $AcceptanceScript, $AnswerXml)) { if (!(Test-Path $required)) { throw "Missing: $required" } }

$switch = Get-VMSwitch -Name 'Default Switch' -ErrorAction SilentlyContinue
if (!$switch) { throw 'Default Switch is missing; run: New-VMSwitch -Name "Default Switch" -SwitchType Internal' }
$vm = Get-VM -Name $VmName -ErrorAction SilentlyContinue
if ($vm -and $ForceRecreate) {
    Log "Removing existing VM $VmName (ForceRecreate)"
    Stop-VM -VM $vm -TurnOff -Force -ErrorAction SilentlyContinue
    Remove-VM -VM $vm -Force
    $vm = $null
}
if (!$vm) {
    Log "Creating VM $VmName"
    New-Item -ItemType Directory -Force $VmRoot | Out-Null
    $disk = Join-Path $VmRoot "$VmName.vhdx"
    $answerVhd = Join-Path $VmRoot 'answer.vhdx'
    if (!(Test-Path $disk)) { New-VHD -Path $disk -SizeBytes 64GB -Dynamic | Out-Null }
    if (!(Test-Path $answerVhd)) { New-VHD -Path $answerVhd -SizeBytes 128MB -Dynamic | Out-Null }

    # Answer file disk: mount, format FAT32, copy autounattend.xml, detach.
    $disksBefore = @(Get-Disk | Select-Object -ExpandProperty Number)
    Mount-VHD -Path $answerVhd | Out-Null
    Start-Sleep -Seconds 3
    $answerDisk = Get-Disk | Where-Object { $_.Number -notin $disksBefore -and $_.BusType -eq 'File Backed Virtual' } | Select-Object -First 1
    if (!$answerDisk) { Dismount-VHD -Path $answerVhd; throw 'Answer VHD did not mount.' }
    $dp = Join-Path $VmRoot 'answer-diskpart.txt'
    @("select disk $($answerDisk.Number)", 'clean', 'create partition primary', 'format fs=fat32 quick label=ANSWER', 'assign letter=Q', 'exit') | Set-Content $dp -Encoding ascii
    & diskpart.exe /s $dp | Out-Null
    Start-Sleep -Seconds 2
    Copy-Item $AnswerXml 'Q:\autounattend.xml' -Force
    @('select volume Q', 'remove letter=Q', 'exit') | Set-Content $dp -Encoding ascii
    & diskpart.exe /s $dp | Out-Null
    Start-Sleep -Seconds 1
    Dismount-VHD -Path $answerVhd | Out-Null

    New-VM -Name $VmName -MemoryStartupBytes 4GB -Generation 2 -VHDPath $disk -Path $VmRoot -SwitchName $switch.Name | Out-Null
    Set-VM -VMName $VmName -ProcessorCount 4 -AutomaticCheckpointsEnabled $false -CheckpointType Disabled
    Set-VM -VMName $VmName -MemoryStartupBytes 4GB -DynamicMemory -MemoryMinimumBytes 2GB -MemoryMaximumBytes 8GB
    Set-VMFirmware -VMName $VmName -EnableSecureBoot Off
    Add-VMDvdDrive -VMName $VmName -Path $IsoPath
    Add-VMHardDiskDrive -VMName $VmName -Path $answerVhd
    $dvd = Get-VMDvdDrive -VMName $VmName
    Set-VMFirmware -VMName $VmName -FirstBootDevice $dvd
    Log 'VM created (Gen2, Secure Boot off, bypasses in answer file)'
    $vm = Get-VM -Name $VmName
}

# ---------- boot + wait for WinRM ----------
if ($vm.State -ne 'Running') { Start-VM -VM $vm }
Log 'VM started; waiting for WinRM (unattended install can take several minutes)'
$ip = $null
$clock = [Diagnostics.Stopwatch]::StartNew()
$credential = New-Object System.Management.Automation.PSCredential($AdminUser, (ConvertTo-SecureString $AdminPass -AsPlainText -Force))
while ($clock.Elapsed.TotalSeconds -lt $WinRmTimeoutSeconds) {
    $addresses = @((Get-VMNetworkAdapter -VMName $VmName -ErrorAction SilentlyContinue).IPAddresses | Where-Object { $_ -match '^\d{1,3}(\.\d{1,3}){3}$' -and $_ -notmatch '^169\.254\.' })
    foreach ($address in $addresses) {
        try {
            $wsman = Test-WSMan -ComputerName $address -ErrorAction Stop
            $ip = $address
            break
        } catch { }
    }
    if ($ip) { break }
    Start-Sleep -Seconds 15
}
if (!$ip) { throw "WinRM did not become available within $WinRmTimeoutSeconds seconds; inspect the VM console." }
Log "WinRM available at $ip"
Set-Item WSMan:\localhost\Client\TrustedHosts -Value '*' -Force
$session = New-PSSession -ComputerName $ip -Credential $credential -Authentication Negotiate -ErrorAction Stop
try {
    $os = Invoke-Command -Session $session -ScriptBlock {
        $o = Get-CimInstance Win32_OperatingSystem
        [pscustomobject]@{ Caption = $o.Caption; Version = $o.Version; Build = [int](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuildNumber; Edition = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').EditionID; ComputerName = $env:COMPUTERNAME }
    }
    Log "Guest: $($os.Caption) build $($os.Build) ($($os.Edition))"
    if (!$SkipAcceptance) {
        Invoke-Command -Session $session -ScriptBlock { New-Item -ItemType Directory -Force 'C:\F1Kit', 'C:\F1Evidence' | Out-Null }
        Copy-Item -ToSession $session $AcceptanceScript -Destination 'C:\F1Kit\Test-F1VmAcceptance.ps1' -Force
        $setupName = Split-Path $SetupExe -Leaf
        $portableName = Split-Path $PortableZip -Leaf
        Copy-Item -ToSession $session $SetupExe -Destination ('C:\F1Kit\' + $setupName) -Force
        Copy-Item -ToSession $session $PortableZip -Destination ('C:\F1Kit\' + $portableName) -Force
        Log 'Artifacts copied to the guest; running the acceptance kit (install + launch + uninstall + portable)'
        $run = Invoke-Command -Session $session -ArgumentList $setupName, $portableName -ScriptBlock {
            param($setupName, $portableName)
            $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'C:\F1Kit\Test-F1VmAcceptance.ps1' -Mode Both `
                -SetupPath ('C:\F1Kit\' + $setupName) `
                -PortablePath ('C:\F1Kit\' + $portableName) `
                -EvidencePath 'C:\F1Evidence' 2>&1 | Out-String
            [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $out }
        }
        Log "Acceptance exit code: $($run.ExitCode)"
        Write-Host $run.Output
        Copy-Item -FromSession $session 'C:\F1Evidence\*' -Destination $EvidencePath -Force -ErrorAction SilentlyContinue
        Copy-Item -FromSession $session 'C:\F1Kit\Test-F1VmAcceptance.ps1' -Destination $EvidencePath -Force -ErrorAction SilentlyContinue
    }
} finally {
    if ($session) { Remove-PSSession $session }
}
$summary = [ordered]@{ VmName = $VmName; Ip = $ip; Guest = $os; CompletedAt = [DateTimeOffset]::Now; EvidencePath = $EvidencePath }
$summary | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $EvidencePath 'vm-validation-summary.json') -Encoding utf8
Log "Done. Evidence: $EvidencePath"
if ($RemoveVm) {
    Log 'Removing VM'
    Stop-VM -Name $VmName -TurnOff -Force -ErrorAction SilentlyContinue
    Remove-VM -Name $VmName -Force
}
