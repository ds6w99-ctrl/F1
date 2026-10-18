# VM-side: install the real-world audit apps from C:\F1Audit\apps (official installers,
# hashes recorded on the host). Silent where the app supports it; every install logged (ASCII).
param([string]$AppsDir = 'C:\F1Audit\apps', [string]$Log = 'C:\F1Audit\install.log')
$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force $AppsDir | Out-Null
[System.IO.File]::WriteAllText($Log, '')
function Step([string]$name) { $line = "=== $name @ $(Get-Date -Format o) ==="; Write-Host $line; Add-Content $Log $line -Encoding ASCII }
function Run([string]$name, [string]$exe, [string[]]$installArgs, [int]$waitSec = 240) {
    Step $name
    $p = Start-Process -FilePath $exe -ArgumentList $installArgs -Wait -PassThru
    $code = if ($p -and $p.HasExited) { $p.ExitCode } else { 'no-process' }
    $line = "exit=$code"
    Write-Host $line; Add-Content $Log $line -Encoding ASCII
    return $code
}

# 1. 7-Zip 24.09 x64 MSI (per-machine, HKLM, MSI product code)
Run '7-Zip MSI x64' 'msiexec.exe' @('/i', "$AppsDir\7z2409-x64.msi", '/qn', '/norestart', '/L*v', "$AppsDir\7zip-install.log")

# 2. PuTTY 0.83 32-bit MSI (32-bit per-machine MSI)
Run 'PuTTY x86 MSI' 'msiexec.exe' @('/i', "$AppsDir\putty-0.83-x86.msi", '/qn', '/norestart', '/L*v', "$AppsDir\putty-install.log")

# 3. Notepad++ 8.8.1 NSIS x64 (per-machine EXE installer)
Run 'Notepad++ NSIS x64' "$AppsDir\npp.8.8.1.Installer.x64.exe" @('/S')

# 4. Visual Studio Code user setup (per-user NSIS, HKCU uninstall)
Run 'VS Code user setup' "$AppsDir\VSCodeUserSetup-x64.exe" @('/VERYSILENT', '/NORESTART', '/MERGETASKS=!runcode')

# 5. Everything 1.4.1.1026 x64 (per-machine EXE installer; installs a Windows service)
Run 'Everything x64' "$AppsDir\Everything-1.4.1.1026.x64-Setup.exe" @('/S')

# 6. Google Chrome (per-machine EXE installer; registers scheduled tasks)
Run 'Chrome machine install' "$AppsDir\chrome_installer.exe" @('/silent', '/install')

# 7. Windows Terminal MSIX: this 25H2 image ships Windows Terminal 1.24 inbox (a real MSIX
#    package already registered per-user) - used for the MSIX removal test. The downloaded
#    1.22 bundle is older than inbox and Add-AppxPackage correctly refuses to downgrade.
#    Recorded, not treated as a failure.
Step 'Windows Terminal MSIX (inbox)'
$wt = Get-AppxPackage *WindowsTerminal* -ErrorAction SilentlyContinue
$line = if ($wt) { "inbox WindowsTerminal $($wt.Version) present; MSIX test will use it" } else { 'inbox Terminal missing' }
Write-Host $line; Add-Content $Log $line -Encoding ASCII

# 8. Notepad++ portable (must NOT appear as installable anywhere)
Step 'Notepad++ portable extraction'
Expand-Archive -Path "$AppsDir\npp.8.8.1.portable.x64.zip" -DestinationPath 'C:\F1Audit\portable' -Force
'portable sentinel' | Set-Content 'C:\F1Audit\portable\notepad-plus-plus-8.8.1\audit-portable-sentinel.txt'

# Documented sentinel fixtures planted INSIDE app-owned install directories after install,
# to observe how real uninstallers and F1's engine treat app-owned files the uninstaller
# does not remove. They are inside app-owned directories only.
Step 'Sentinel fixtures'
'audit sentinel: planted inside 7-Zip install dir' | Set-Content 'C:\Program Files\7-Zip\audit-sentinel.txt'
'audit sentinel: planted inside Notepad++ install dir' | Set-Content 'C:\Program Files\Notepad++\audit-sentinel.txt'
New-Item -ItemType Directory -Force 'C:\Program Files\Notepad++\audit-sub' | Out-Null
'nested sentinel' | Set-Content 'C:\Program Files\Notepad++\audit-sub\nested.txt'
'planted: 7-Zip, Notepad++ (+subdir)' | Add-Content $Log -Encoding ASCII

$line = "INSTALL SCRIPT DONE @ $(Get-Date -Format o)"
Write-Host $line; Add-Content $Log $line -Encoding ASCII
