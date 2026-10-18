# VM-side system state snapshot for F1 audit verification.
# Captures independent ground truth: uninstall keys, services, tasks, startup, shortcuts,
# top-level directories, Appx packages and a configurable list of app registry keys.
param(
    [string]$Label = 'state',
    [string]$OutDir = 'C:\F1Audit\states',
    [string[]]$AppKeyNames = @()
)
$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$file = Join-Path $OutDir "$Label.json"

$uninstall = @()
foreach ($root in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall','HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')) {
    Get-ChildItem $root -ErrorAction SilentlyContinue | ForEach-Object {
        $p = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
        if ($p.DisplayName) {
            $uninstall += [ordered]@{
                Root = $root.Substring($root.IndexOf(':') + 1); Key = $_.PSChildName
                DisplayName = [string]$p.DisplayName; Publisher = [string]$p.Publisher
                Version = [string]$p.DisplayVersion; UninstallString = [string]$p.UninstallString
                QuietUninstallString = [string]$p.QuietUninstallString; InstallLocation = [string]$p.InstallLocation
                SystemComponent = [string]$p.SystemComponent; NoRemove = [string]$p.NoRemove
                WindowsInstaller = [string]$p.WindowsInstaller
            }
        }
    }
}

$services = @(Get-CimInstance Win32_Service -ErrorAction SilentlyContinue | ForEach-Object {
    [ordered]@{ Name = $_.Name; DisplayName = $_.DisplayName; State = $_.State; StartMode = $_.StartMode; Path = $_.PathName }
})

$tasks = @(Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskPath -notlike '\Microsoft\*' } | ForEach-Object {
    [ordered]@{ Path = ($_.TaskPath + $_.TaskName); State = [string]$_.State }
})

$run = @()
foreach ($root in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run','HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run')) {
    $k = Get-ItemProperty $root -ErrorAction SilentlyContinue
    if ($k) { $k.PSObject.Properties | Where-Object { $_.Name -notmatch '^PS' } | ForEach-Object {
        $run += [ordered]@{ Root = $root.Substring($root.IndexOf(':') + 1); Name = $_.Name; Command = [string]$_.Value }
    } }
}

$shell = New-Object -ComObject WScript.Shell
$shortcuts = @()
foreach ($dir in @([Environment]::GetFolderPath('StartMenu'), [Environment]::GetFolderPath('CommonStartMenu'), [Environment]::GetFolderPath('DesktopDirectory'), [Environment]::GetFolderPath('CommonDesktopDirectory'))) {
    Get-ChildItem $dir -Filter '*.lnk' -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
        $target = $null
        try { $target = $shell.CreateShortcut($_.FullName).TargetPath } catch { }
        $shortcuts += [ordered]@{ Dir = $dir; Name = $_.Name; Target = [string]$target }
    }
}

$dirs = @()
foreach ($base in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:APPDATA, $env:LOCALAPPDATA, $env:ProgramData)) {
    if ($base) { Get-ChildItem $base -Directory -ErrorAction SilentlyContinue | ForEach-Object { $dirs += [ordered]@{ Base = $base; Name = $_.Name } } }
}

$appx = @(Get-AppxPackage -ErrorAction SilentlyContinue | ForEach-Object { [ordered]@{ Name = $_.Name; PackageFullName = $_.PackageFullName; SignatureKind = [string]$_.SignatureKind } })

$appkeys = @()
foreach ($name in $AppKeyNames) {
    foreach ($root in @('HKLM:\SOFTWARE','HKLM:\SOFTWARE\WOW6432Node','HKCU:\SOFTWARE')) {
        $candidate = Join-Path $root $name
        $k = Get-Item $candidate -ErrorAction SilentlyContinue
        if ($k) {
            $values = @($k.GetValueNames() | Where-Object { $_ })
            $appkeys += [ordered]@{ Root = $root; Path = $name; ValueCount = $values.Count; Values = $values }
        }
    }
}

$state = [ordered]@{
    CapturedAt = (Get-Date -Format o); Machine = $env:COMPUTERNAME; User = $env:USERNAME
    WindowsBuild = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuildNumber
    Uninstall = $uninstall; Services = $services; Tasks = $tasks; Run = $run
    Shortcuts = $shortcuts; TopDirs = $dirs; Appx = $appx; AppKeys = $appkeys
}
$state | ConvertTo-Json -Depth 6 | Set-Content $file -Encoding UTF8
"captured -> $file ($($uninstall.Count) uninstall entries, $($services.Count) services, $($tasks.Count) tasks, $($appx.Count) appx)"
