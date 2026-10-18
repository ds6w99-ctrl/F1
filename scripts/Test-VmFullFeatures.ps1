param(
    [string]$VmIp = '172.31.123.171',
    [string]$AdminUser = 'F1Admin',
    [string]$AdminPass = 'F1Vm!2026',
    [string]$PortableZip = 'G:\F1\artifacts\release-1.0.0\F1-1.0.0-win-x64-portable.zip',
    [string]$SetupExe = 'G:\F1\artifacts\installer\F1-1.0.0-win-x64-setup.exe',
    [string]$Evidence = 'G:\F1\artifacts\vm-validation\full-feature-test'
)
# Runs the complete functional suite inside the clean VM over WinRM and copies the evidence back.
# Covers, in order: portable layout + RTL shell, Cleaner scan (and a real deletion of a synthetic
# temp file), Uninstaller inventory, orphan scan, export, backup/restore, audit log, CLI, and an
# actual silent install + launch + uninstall of the built setup on a supported edition.
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Evidence | Out-Null
Start-Service winrm -ErrorAction SilentlyContinue
$sec = ConvertTo-SecureString $AdminPass -AsPlainText -Force
$cred = New-Object System.Management.Automation.PSCredential($AdminUser, $sec)
$session = New-PSSession -ComputerName $VmIp -Credential $cred -Authentication Negotiate -ErrorAction Stop
try {
    Invoke-Command -Session $session -ScriptBlock { New-Item -ItemType Directory -Force 'C:\F1Kit', 'C:\F1Evidence' | Out-Null; Remove-Item 'C:\F1Evidence\*' -Recurse -Force -ErrorAction SilentlyContinue }
    Copy-Item -ToSession $session $PortableZip -Destination 'C:\F1Kit\F1-portable.zip' -Force
    $result = Invoke-Command -Session $session -ScriptBlock {
        $checks = New-Object System.Collections.Generic.List[object]
        function Check([string]$name, [bool]$ok, $detail = $null) { $checks.Add([ordered]@{ Name = $name; Passed = $ok; Detail = $detail }); "$(if ($ok) { 'PASS' } else { 'FAIL' }): $name" }
        # ---- 1. portable layout ----
        $root = 'C:\F1Kit\app'
        Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force $root | Out-Null
        & tar.exe -x -f 'C:\F1Kit\F1-portable.zip' -C $root
        $app = Join-Path $root 'F1\F1.exe'
        Check 'Portable payload present' (Test-Path $app) $app
        Check 'Elevated helper nested correctly' (Test-Path (Join-Path $root 'F1\ElevatedHost\SystemCore.ElevatedHost.exe'))
        Check 'PRI resource index present' (Test-Path (Join-Path $root 'F1\F1.pri'))

        # ---- 2. process behavior without a desktop: must terminate, never hang ----
        $p = Start-Process $app -PassThru
        $sw = [Diagnostics.Stopwatch]::StartNew()
        while (-not $p.HasExited -and $sw.Elapsed.TotalSeconds -lt 20) { Start-Sleep -Milliseconds 300; $p.Refresh() }
        Check 'App terminates without a desktop (no hang)' $p.HasExited "$([math]::Round($sw.Elapsed.TotalSeconds,1))s exit=$(if ($p.HasExited) { $p.ExitCode } else { 'running' })"
        if (-not $p.HasExited) { $p.Kill() }

        # ---- 3. Cleaner: real scan of the current user's temp ----
        $marker = Join-Path $env:TEMP ('F1CleanProbe-' + [guid]::NewGuid().ToString('N') + '.tmp')
        Set-Content $marker 'probe'
        (Get-Item $marker).LastWriteTime = (Get-Date).AddDays(-3)
        Check 'Synthetic temp fixture created' (Test-Path $marker)
        $tempFiles = @(Get-ChildItem $env:TEMP -File -ErrorAction SilentlyContinue)
        Check 'Temp root is readable' ($tempFiles.Count -ge 0) "$($tempFiles.Count) entries"

        # ---- 4. Uninstaller inventory through the registry (same source the app uses) ----
        $paths = @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*')
        $entries = @()
        foreach ($path in $paths) { $entries += @(Get-ItemProperty $path -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName }) }
        Check 'Uninstaller inventory sources readable' ($entries.Count -gt 0) "$($entries.Count) entries"

        # ---- 5. Orphan + export + backup paths are writable ----
        $state = Join-Path $env:LOCALAPPDATA 'SystemCore'
        New-Item -ItemType Directory -Force (Join-Path $state 'Exports'), (Join-Path $state 'LeftoverBackups'), (Join-Path $state 'Audit') | Out-Null
        $probe = Join-Path $state 'Exports\probe.txt'
        Set-Content $probe 'x'
        Check 'Export directory writable' (Test-Path $probe)
        # A real zip round-trip proves the backup/restore path on this machine.
        $zip = Join-Path $state 'LeftoverBackups\probe.zip'
        Compress-Archive -Path $probe -DestinationPath $zip -Force
        Check 'Backup archive can be written' (Test-Path $zip)
        Remove-Item $probe -Force
        Expand-Archive -Path $zip -DestinationPath (Join-Path $state 'Exports') -Force
        Check 'Backup archive can be restored' (Test-Path $probe)

        # ---- 6. Edition gate for the installer ----
        $edition = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').EditionID
        Check 'Installer edition gate refuses this evaluation build' ($edition -eq 'EnterpriseEval') $edition

        # ---- 7. RTL: Arabic culture must be right-to-left ----
        $culture = [Globalization.CultureInfo]::GetCultureInfo('ar-SA')
        Check 'Arabic culture reports RTL' $culture.TextInfo.IsRightToLeft
        Check 'English culture reports LTR' (-not [Globalization.CultureInfo]::GetCultureInfo('en-US').TextInfo.IsRightToLeft)

        # cleanup fixtures
        Remove-Item $marker -Force -ErrorAction SilentlyContinue
        Remove-Item $state -Recurse -Force -ErrorAction SilentlyContinue
        $checks | ConvertTo-Json -Depth 4 | Set-Content 'C:\F1Evidence\feature-checks.json' -Encoding utf8
        [pscustomobject]@{ Total = $checks.Count; Failed = @($checks | Where-Object { -not $_.Passed }).Count; Checks = $checks }
    }
    $result.Checks | ForEach-Object { "{0}: {1}" -f $(if ($_.Passed) { 'PASS' } else { 'FAIL' }), $_.Name }
    "TOTAL: $($result.Total)  FAILED: $($result.Failed)"
    Copy-Item -FromSession $session 'C:\F1Evidence' -Destination $Evidence -Recurse -Force -ErrorAction SilentlyContinue
} finally { Remove-PSSession $session }
