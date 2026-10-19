# Host-side installer lifecycle test in the retail VM (install -> verify -> run -> uninstall -> reinstall).
param(
    [string]$VmIp = '172.20.56.242',
    [string]$AdminUser = 'F1Admin',
    [string]$AdminPass = 'F1Vm!2026',
    [string]$Installer = 'G:\F1\artifacts\installer\F1-1.0.0-win-x64-setup.exe',
    [string]$Evidence = 'G:\F1\artifacts\audit-baseline\vm2-evidence'
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Evidence | Out-Null
$cred = New-Object System.Management.Automation.PSCredential($AdminUser, (ConvertTo-SecureString $AdminPass -AsPlainText -Force))
$s = New-PSSession -ComputerName $VmIp -Credential $cred -Authentication Negotiate -ErrorAction Stop
$results = New-Object System.Collections.Generic.List[object]
function Add-Result([string]$name, [bool]$ok, $detail = $null) {
    $results.Add([ordered]@{ Name = $name; Passed = $ok; Detail = $detail })
    "{0}: {1} {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail
}
function Verify-Installed([string]$round) {
    Invoke-Command -Session $s -ScriptBlock {
        param($round)
        $out = [ordered]@{ Round = $round }
        $out.Exe = Test-Path 'C:\Program Files\F1\App\F1.exe'
        $out.Pri = Test-Path 'C:\Program Files\F1\App\F1.pri'
        $out.Helper = Test-Path 'C:\Program Files\F1\App\ElevatedHost\SystemCore.ElevatedHost.exe'
        $out.DesktopShortcut = Test-Path 'C:\Users\Public\Desktop\F1.lnk'
        $out.StartMenu = Test-Path 'C:\ProgramData\Microsoft\Windows\Start Menu\Programs\F1.lnk'
        $out.UninstallKey = [bool](Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue | Where-Object { (Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue).DisplayName -like 'F1*' })
        $fkey = Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue | Where-Object { (Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue).DisplayName -like 'F1*' } | Select-Object -First 1
        $out.UninstallKeyPath = if ($fkey) { $fkey.PSChildName } else { $null }
        $key = if ($fkey) { Get-ItemProperty $fkey.PSPath } else { $null }
        $out.DisplayName = $key.DisplayName
        $out.DisplayVersion = $key.DisplayVersion
        $out.UninstallString = $key.UninstallString
        if (Test-Path 'C:\Users\Public\Desktop\F1.lnk') {
            $sh = New-Object -ComObject WScript.Shell
            $out.ShortcutTarget = $sh.CreateShortcut('C:\Users\Public\Desktop\F1.lnk').TargetPath
        }
        [pscustomobject]$out
    } -ArgumentList $round
}

Copy-Item -ToSession $s $Installer -Destination 'C:\F1Audit\F1-setup.exe' -Force

# ---- 0. baseline ----
Invoke-Command -Session $s { 'baseline installed: ' + (Test-Path 'C:\Program Files\F1') }

# ---- 1. INSTALL (close any running instance first: silent Inno aborts with exit 5 otherwise) ----
$install = Invoke-Command -Session $s -ScriptBlock {
    Get-Process F1 -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep 2
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process 'C:\F1Audit\F1-setup.exe' -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/LOG=C:\F1Audit\install.log' -Wait -PassThru
    $sw.Stop()
    [pscustomobject]@{ Exit = $p.ExitCode; Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1) }
}
Add-Result 'Installer exit code 0' ($install.Exit -eq 0) "exit=$($install.Exit) in $($install.Seconds)s"
$v = Verify-Installed 'install-1'
Add-Result 'Installed F1.exe' $v.Exe
Add-Result 'Installed F1.pri' $v.Pri
Add-Result 'Installed elevated helper' $v.Helper
Add-Result 'Desktop shortcut created' $v.DesktopShortcut
Add-Result 'Start menu shortcut created' $v.StartMenu
Add-Result 'Windows Settings uninstall entry' $v.UninstallKey $v.DisplayName
Add-Result 'Uninstall version 1.0.0' ($v.DisplayVersion -eq '1.0.0') $v.DisplayVersion
Add-Result 'Shortcut target is the app' ($v.ShortcutTarget -eq 'C:\Program Files\F1\App\F1.exe') $v.ShortcutTarget

# ---- 2. LAUNCH via HKCU Run + reboot (quoted path: it contains spaces) ----
Invoke-Command -Session $s {
    New-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name 'F1Lifecycle' -Value '"C:\Program Files\F1\App\F1.exe"' -PropertyType String -Force | Out-Null
    shutdown /r /t 8 /f
} | Out-Null
Start-Sleep 200
$s = $null
for ($i = 0; $i -lt 30 -and -not $s; $i++) { Start-Sleep 10; try { $s = New-PSSession -ComputerName $VmIp -Credential $cred -Authentication Negotiate -ErrorAction Stop } catch { } }
if (-not $s) { throw 'VM not reachable after launch reboot' }
$launch = Invoke-Command -Session $s {
    $p = Get-Process F1 -ErrorAction SilentlyContinue | Select-Object -First 1
    [pscustomobject]@{
        Running = [bool]$p; Id = $p.Id; Path = $p.Path; Threads = if ($p) { $p.Threads.Count } else { 0 }
        ConsoleSession = if ($p) { $p.SessionId -eq 1 } else { $false }
        AppDataInitialized = Test-Path 'C:\Users\F1Admin\AppData\Local\SystemCore\cache'
        CacheFiles = @(Get-ChildItem 'C:\Users\F1Admin\AppData\Local\SystemCore\cache' -ErrorAction SilentlyContinue).Count
    }
}
# Window handles are not readable across sessions from a WinRM (session 0) query; the in-session
# UI driver pass in this VM records the real window title separately. Here: process in the console
# session with initialized application data proves the installed binary actually started the UI.
Add-Result 'Installed app running in the console session' ($launch.Running -and $launch.ConsoleSession) ("id=$($launch.Id) threads=$($launch.Threads)")
Add-Result 'Installed app initialized its UI data' ($launch.AppDataInitialized -and $launch.CacheFiles -gt 0) ("cache files=$($launch.CacheFiles)")

# ---- 3. UNINSTALL ----
$uninstall = Invoke-Command -Session $s {
    Get-Process F1 -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Remove-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name 'F1Lifecycle' -ErrorAction SilentlyContinue
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process 'C:\Program Files\F1\Uninstall\unins000.exe' -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru
    $sw.Stop()
    [pscustomobject]@{ Exit = $p.ExitCode; Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1) }
}
Add-Result 'Uninstaller exit code 0' ($uninstall.Exit -eq 0) "exit=$($uninstall.Exit) in $($uninstall.Seconds)s"
Start-Sleep 5
$after = Invoke-Command -Session $s {
    [pscustomobject]@{
        ProgramFiles = Test-Path 'C:\Program Files\F1'
        DesktopShortcut = Test-Path 'C:\Users\Public\Desktop\F1.lnk'
        StartMenu = Test-Path 'C:\ProgramData\Microsoft\Windows\Start Menu\Programs\F1.lnk'
        UninstallKey = [bool](Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue | Where-Object { (Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue).DisplayName -like 'F1*' })
        LocalAppData = Test-Path 'C:\Users\F1Admin\AppData\Local\SystemCore'
    }
}
Add-Result 'Program Files\F1 removed' (-not $after.ProgramFiles)
Add-Result 'Desktop shortcut removed' (-not $after.DesktopShortcut)
Add-Result 'Start menu shortcut removed' (-not $after.StartMenu)
Add-Result 'Uninstall entry removed' (-not $after.UninstallKey)
"Remaining user data folder (expected when settings were saved): $($after.LocalAppData)"

# ---- 4. REINSTALL ----
$reinstall = Invoke-Command -Session $s {
    Get-Process F1 -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep 2
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process 'C:\F1Audit\F1-setup.exe' -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru
    $sw.Stop()
    [pscustomobject]@{ Exit = $p.ExitCode; Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1) }
}
Add-Result 'Reinstall exit code 0' ($reinstall.Exit -eq 0) "exit=$($reinstall.Exit) in $($reinstall.Seconds)s"
$v2 = Verify-Installed 'reinstall'
Add-Result 'Reinstalled F1.exe' $v2.Exe
Add-Result 'Reinstalled desktop shortcut' $v2.DesktopShortcut
Add-Result 'Reinstalled uninstall entry' $v2.UninstallKey

$results | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $Evidence 'lifecycle-results.json')
$results | ForEach-Object { "{0}|{1}|{2}" -f $_.Name, $_.Passed, $_.Detail } | Set-Content (Join-Path $Evidence 'lifecycle-summary.txt')
Remove-PSSession $s
"LIFECYCLE DONE -> $Evidence"



