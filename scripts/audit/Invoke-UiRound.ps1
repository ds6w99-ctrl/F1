# Host-side: orchestrate the interactive UI round inside the VM.
# 1) revert VM to audit-apps-installed (elevated), 2) reinstall 7-Zip + sentinel + extract the
# current portable payload, 3) launch F1 in the console session via HKCU Run + reboot,
# 4) run the UIA driver in the interactive session, 5) pull evidence to the host.
param(
    [string]$VmIp = '172.31.123.171',
    [string]$AdminUser = 'F1Admin',
    [string]$AdminPass = 'F1Vm!2026',
    [switch]$SkipRevert
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$evidence = Join-Path $root 'artifacts\audit-baseline\vm-evidence\UI-ROUND'
New-Item -ItemType Directory -Force $evidence | Out-Null
$sec = ConvertTo-SecureString $AdminPass -AsPlainText -Force
$cred = New-Object System.Management.Automation.PSCredential($AdminUser, $sec)

if (-not $SkipRevert) {
    Start-Process -FilePath 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -Verb RunAs -Wait `
        -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'Elevated-RevertApps.ps1')
    Start-Sleep 20
}

# reconnect after reboot
$s = $null
for ($i = 0; $i -lt 30 -and -not $s; $i++) { Start-Sleep 10; try { $s = New-PSSession -ComputerName $VmIp -Credential $cred -Authentication Negotiate -ErrorAction Stop } catch { } }
if (-not $s) { throw 'VM not reachable after revert/boot' }

# stage payload + scripts + probe
Copy-Item -ToSession $s (Join-Path $root 'artifacts\portable\F1-1.0.0-win-x64-portable.zip') -Destination 'C:\F1Audit\F1-portable.zip' -Force
Copy-Item -ToSession $s (Join-Path $root 'artifacts\audit-tools\RealWorldProbe') -Destination 'C:\F1Audit\probe' -Recurse -Force
Copy-Item -ToSession $s (Join-Path $root 'scripts\audit\vm\Run-UiRound.ps1') -Destination 'C:\F1Audit\scripts\Run-UiRound.ps1' -Force

Invoke-Command -Session $s -ScriptBlock {
    Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
    # extract the current payload
    Remove-Item 'C:\F1Audit\F1' -Recurse -Force -ErrorAction SilentlyContinue
    tar -x -f 'C:\F1Audit\F1-portable.zip' -C 'C:\F1Audit'
    "F1.exe: $(Test-Path 'C:\F1Audit\F1\F1.exe')"
    # reinstall 7-Zip (checkpoint had it broken) + sentinel; Notepad++ sentinels already in dir
    if (-not (Test-Path 'C:\Program Files\7-Zip\7z.exe')) {
        $p = Start-Process msiexec.exe -ArgumentList @('/i','C:\F1Audit\apps\7z2409-x64.msi','/qn','/norestart') -Wait -PassThru
        "7zip install exit: $($p.ExitCode) 7z.exe=$(Test-Path 'C:\Program Files\7-Zip\7z.exe')"
    }
    'audit sentinel ui-round' | Set-Content 'C:\Program Files\7-Zip\audit-sentinel.txt'
    if (-not (Test-Path 'C:\Program Files\Notepad++\notepad++.exe')) {
        $p2 = Start-Process 'C:\F1Audit\apps\npp.8.8.1.Installer.x64.exe' -ArgumentList '/S' -Wait -PassThru
        "npp install exit: $($p2.ExitCode) notepad++.exe=$(Test-Path 'C:\Program Files\Notepad++\notepad++.exe')"
    }
    # launch F1 in the console session via HKCU Run + reboot
    New-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name 'F1AuditUi' -Value 'C:\F1Audit\F1\F1.exe' -PropertyType String -Force | Out-Null
    'run key set; rebooting in 5s'
} | Out-String
Invoke-Command -Session $s -ScriptBlock { shutdown /r /t 5 /f; 'reboot issued' } | Out-String
Remove-PSSession $s
Start-Sleep 60
$s2 = $null
for ($i = 0; $i -lt 30 -and -not $s2; $i++) { Start-Sleep 10; try { $s2 = New-PSSession -ComputerName $VmIp -Credential $cred -Authentication Negotiate -ErrorAction Stop } catch { } }
if (-not $s2) { throw 'VM not reachable after reboot' }
Invoke-Command -Session $s2 -ScriptBlock {
    Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
    'F1 process: ' + ((Get-Process F1 -ErrorAction SilentlyContinue | Measure-Object).Count)
    # run the UIA driver in the interactive session
    schtasks /Create /TN 'F1AuditUiRound' /TR "cmd /c powershell -NoProfile -ExecutionPolicy Bypass -File C:\F1Audit\scripts\Run-UiRound.ps1 > C:\F1Audit\ui-task.log 2>&1" /SC ONCE /ST 23:59 /RU F1Admin /RP 'F1Vm!2026' /IT /F | Out-String
    schtasks /Run /TN 'F1AuditUiRound' | Out-String
} | Out-String
Remove-PSSession $s2
# wait for the driver to finish, then pull evidence
$s3 = New-PSSession -ComputerName $VmIp -Credential $cred -Authentication Negotiate -ErrorAction SilentlyContinue
if ($s3) {
    $done = $false
    for ($i = 0; $i -lt 40 -and -not $done; $i++) { Start-Sleep 10; $st = Invoke-Command -Session $s3 { (schtasks /Query /TN 'F1AuditUiRound' /FO CSV | Select-Object -Skip 1 | ConvertFrom-Csv).Status }; if ($st -ne 'Running') { $done = $true } }
    Invoke-Command -Session $s3 { Get-Content 'C:\F1Audit\ui-evidence\ui-round.txt' -ErrorAction SilentlyContinue; Get-Content 'C:\F1Audit\ui-task.log' -Tail 15 -ErrorAction SilentlyContinue } | Out-String
    Copy-Item -FromSession $s3 'C:\F1Audit\ui-evidence' -Destination $evidence -Recurse -Force -ErrorAction SilentlyContinue
    Remove-PSSession $s3
}
"UI ROUND EVIDENCE -> $evidence"
Get-ChildItem $evidence -Recurse -File | Select-Object Name, Length | Format-Table -AutoSize
