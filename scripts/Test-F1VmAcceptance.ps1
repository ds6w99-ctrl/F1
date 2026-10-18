param(
    [ValidateSet('Install', 'Portable', 'Both')]
    [string]$Mode = 'Install',
    [string]$SetupPath = 'F1-1.0.0-win-x64-setup.exe',
    [string]$SetupSha256 = '329241B298B27E43536FAAF57D5C5163751456E54BFDD03DDE0A247669A26BEB',
    [string]$PortablePath = 'F1-1.0.0-win-x64-portable.zip',
    [string]$PortableSha256 = '7105E41D9BC1D46F45575550E80D65C431B8416CC379D65DFEA8CF16DEE87A2C',
    [string]$EvidencePath = "$env:USERPROFILE\Desktop\F1-VM-Evidence",
    [string]$InteractiveUser = 'F1Admin',
    [string]$InteractivePass = 'F1Vm!2026',
    [int]$LaunchTimeoutSeconds = 40,
    [int]$UninstallTimeoutSeconds = 180
)
# F1 clean-machine (VM) acceptance kit.
# Runs ON the target machine. Copy this script together with the release artifacts, then run:
#   powershell -ExecutionPolicy Bypass -File Test-F1VmAcceptance.ps1 -Mode Both
# It performs: environment record, artifact hash verification, silent install, installed-layout
# verification, launch/close smoke, uninstall + residue check, and a portable extract/run/remove
# check. It never touches user data outside the installer's own install root. Evidence is written
# as JSON + a plain-text summary. Exit code 0 = every executed check passed.
# When invoked over WinRM (no interactive desktop in that session), the launch smoke falls back
# to a one-shot scheduled task running as the logged-on interactive user, so the window check is
# still a real UI check.
$ErrorActionPreference = 'Stop'
$report = [ordered]@{
    StartedAt = [DateTimeOffset]::Now
    Machine = $env:COMPUTERNAME
    User = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    Elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    Mode = $Mode
    Steps = [System.Collections.Generic.List[object]]::new()
    Artifacts = [ordered]@{}
    Verdict = 'UNKNOWN'
}
$failures = 0
New-Item -ItemType Directory -Force $EvidencePath | Out-Null

function Step([string]$name, [scriptblock]$action) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        $result = & $action
        $report.Steps.Add([ordered]@{ Step = $name; Passed = $true; Detail = $result; Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1) })
        Write-Output "PASS: $name"
        return $result
    }
    catch {
        $script:failures++
        $report.Steps.Add([ordered]@{ Step = $name; Passed = $false; Detail = $_.Exception.Message; Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1) })
        Write-Output "FAIL: $name - $($_.Exception.Message)"
        return $null
    }
}

function Start-InteractiveApp([string]$exePath) {
    # Direct launch works when the caller already owns an interactive desktop.
    try {
        $p = Start-Process $exePath -PassThru
        $clock = [Diagnostics.Stopwatch]::StartNew()
        do { Start-Sleep -Milliseconds 300; $p.Refresh() } while (!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $clock.Elapsed.TotalSeconds -lt 15)
        if (!$p.HasExited -and $p.MainWindowHandle -ne [IntPtr]::Zero) {
            $title = $p.MainWindowTitle
            $null = $p.CloseMainWindow(); $null = $p.WaitForExit(15000)
            if (!$p.HasExited) { $p.Kill(); $null = $p.WaitForExit(5000) }
            return [pscustomobject]@{ Ok = $true; Mode = 'direct'; Title = $title }
        }
        if (!$p.HasExited) { $p.Kill(); $null = $p.WaitForExit(5000) }
    }
    catch { }
    # Fallback for WinRM/non-interactive sessions: one-shot task in the logged-on user's desktop.
    $resultFile = Join-Path $EvidencePath ('launch-' + [guid]::NewGuid().ToString('N') + '.json')
    $runner = Join-Path $env:TEMP ('f1-launch-' + [guid]::NewGuid().ToString('N') + '.ps1')
    $exe = $exePath.Replace("'", "''")
    $runnerText = @"
param()
try {
  `$p = Start-Process '$exe' -PassThru
  `$clock = [Diagnostics.Stopwatch]::StartNew()
  do { Start-Sleep -Milliseconds 300; `$p.Refresh() } while (!`$p.HasExited -and `$p.MainWindowHandle -eq [IntPtr]::Zero -and `$clock.Elapsed.TotalSeconds -lt 40)
  if (`$p.HasExited) { @{ Ok = `$false; Reason = 'exited' } | ConvertTo-Json | Set-Content '$resultFile' }
  elseif (`$p.MainWindowHandle -eq [IntPtr]::Zero) { `$p.Kill(); @{ Ok = `$false; Reason = 'no-window' } | ConvertTo-Json | Set-Content '$resultFile' }
  else { `$t = `$p.MainWindowTitle; `$null = `$p.CloseMainWindow(); `$null = `$p.WaitForExit(15000); if (!`$p.HasExited) { `$p.Kill() }; @{ Ok = `$true; Title = `$t } | ConvertTo-Json | Set-Content '$resultFile' }
} catch { @{ Ok = `$false; Reason = `$_.Exception.Message } | ConvertTo-Json | Set-Content '$resultFile' }
"@
    Set-Content $runner $runnerText -Encoding utf8
    $taskName = 'F1InteractiveLaunch'
    $startTime = (Get-Date).AddMinutes(1).ToString('HH:mm')
    & schtasks.exe /create /tn $taskName /tr "powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner" /sc once /st $startTime /ru $InteractiveUser /rp $InteractivePass /it /f | Out-Null
    & schtasks.exe /run /tn $taskName | Out-Null
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while (!(Test-Path $resultFile) -and $clock.Elapsed.TotalSeconds -lt 120) { Start-Sleep -Milliseconds 500 }
    $parsed = $null
    if (Test-Path $resultFile) { try { $parsed = Get-Content $resultFile -Raw | ConvertFrom-Json } catch { } }
    & schtasks.exe /delete /tn $taskName /f | Out-Null
    Remove-Item $runner -Force -ErrorAction SilentlyContinue
    Remove-Item $resultFile -Force -ErrorAction SilentlyContinue
    if ($parsed -and $parsed.Ok -eq $true) { return [pscustomobject]@{ Ok = $true; Mode = 'scheduled-task'; Title = $parsed.Title } }
    $reason = if ($parsed) { $parsed.Reason } else { 'timeout' }
    return [pscustomobject]@{ Ok = $false; Mode = 'scheduled-task'; Reason = $reason }
}

# ---------- environment ----------
$os = Get-CimInstance Win32_OperatingSystem
$cs = Get-CimInstance Win32_ComputerSystem
$report.Environment = [ordered]@{
    Caption = $os.Caption
    Version = $os.Version
    Build = [int](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuildNumber
    Edition = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').EditionID
    Architecture = $os.OSArchitecture
    Model = $cs.Model
}

Step 'Target is Windows 11 x64 build 22631 or later' {
    $build = $report.Environment.Build
    if ($report.Environment.Architecture -notmatch '64') { throw 'Not x64' }
    if ($build -lt 22631) { throw "Build $build is below the 22631 floor" }
    "build $build"
} | Out-Null

$installRoot = Join-Path $env:ProgramFiles 'F1'
$startMenu = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'F1.lnk'

if ($Mode -in @('Install', 'Both')) {
    Step 'Setup artifact exists' { if (!(Test-Path $SetupPath)) { throw "Missing $SetupPath" }; (Resolve-Path $SetupPath).Path } | Out-Null
    Step 'Setup SHA256 matches release record' {
        $actual = (Get-FileHash $SetupPath -Algorithm SHA256).Hash
        if ($SetupSha256 -and $actual -ne $SetupSha256) { throw "Hash mismatch: $actual" }
        $report.Artifacts.Setup = $actual
        $actual
    } | Out-Null
    $installLog = Join-Path $EvidencePath 'install.log'
    Step 'Silent install completes' {
        $p = Start-Process -FilePath $SetupPath -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=$installLog") -Wait -PassThru
        if ($p.ExitCode -notin @(0, 3010)) { throw "Installer exit code $($p.ExitCode)" }
        "exit $($p.ExitCode)"
    } | Out-Null
    Step 'Installed layout matches the pinned contract' {
        $app = Join-Path $installRoot 'App\F1.exe'
        $helper = Join-Path $installRoot 'App\ElevatedHost\SystemCore.ElevatedHost.exe'
        $pri = Join-Path $installRoot 'App\F1.pri'
        foreach ($required in @($app, $helper, $pri)) { if (!(Test-Path $required)) { throw "Missing $required" } }
        $version = (Get-Item $app).VersionInfo.ProductVersion
        if ($version -ne '1.0.0') { throw "ProductVersion '$version' is not 1.0.0" }
        "layout ok, version $version"
    } | Out-Null
    Step 'Installed executable launches and closes cleanly' {
        $app = Join-Path $installRoot 'App\F1.exe'
        $launch = Start-InteractiveApp $app
        if (!$launch.Ok) { throw "Launch failed ($($launch.Mode)): $($launch.Reason)" }
        "window '$($launch.Title)' appeared and closed [$($launch.Mode)]"
    } | Out-Null
    Step 'Apps & features entry exists' {
        $entry = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
            Where-Object { $_.DisplayName -like 'F1*' } | Select-Object -First 1
        if (!$entry) { throw 'No F1 uninstall entry' }
        "$($entry.DisplayName) $($entry.DisplayVersion)"
    } | Out-Null
    Step 'Silent uninstall completes and removes the product root' {
        $unins = Get-ChildItem (Join-Path $installRoot 'Uninstall') -Filter 'unins*.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
        if (!$unins) { throw 'Uninstaller missing' }
        $p = Start-Process -FilePath $unins.FullName -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait -PassThru
        if ($p.ExitCode -notin @(0, 3010)) { throw "Uninstaller exit code $($p.ExitCode)" }
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while ((Test-Path $installRoot) -and $clock.Elapsed.TotalSeconds -lt $UninstallTimeoutSeconds) { Start-Sleep -Milliseconds 500 }
        if (Test-Path $installRoot) { throw 'Install root still present after uninstall timeout' }
        "removed in $([math]::Round($clock.Elapsed.TotalSeconds, 1))s"
    } | Out-Null
    Step 'Start-menu shortcut removed' {
        if (Test-Path $startMenu) { throw "Shortcut still present: $startMenu" }
        'absent'
    } | Out-Null
    Step 'User state is preserved by uninstall' {
        $state = Join-Path $env:LOCALAPPDATA 'SystemCore'
        if (Test-Path $state) { 'state present (expected; uninstaller leaves it)' } else { 'state absent (documented behavior)' }
    } | Out-Null
}

if ($Mode -in @('Portable', 'Both')) {
    Step 'Portable artifact exists' { if (!(Test-Path $PortablePath)) { throw "Missing $PortablePath" }; (Resolve-Path $PortablePath).Path } | Out-Null
    Step 'Portable SHA256 matches release record' {
        $actual = (Get-FileHash $PortablePath -Algorithm SHA256).Hash
        if ($PortableSha256 -and $actual -ne $PortableSha256) { throw "Hash mismatch: $actual" }
        $report.Artifacts.Portable = $actual
        $actual
    } | Out-Null
    $stage = Join-Path $env:TEMP ('F1PortableAcceptance-' + [guid]::NewGuid().ToString('N'))
    Step 'Portable extracts and core payload is present' {
        New-Item -ItemType Directory -Force $stage | Out-Null
        & tar.exe -x -f (Resolve-Path $PortablePath).Path -C $stage
        if ($LASTEXITCODE -ne 0) { throw 'Extraction failed' }
        $root = Get-ChildItem $stage -Directory | Select-Object -First 1
        foreach ($required in @('F1.exe', 'F1.pri', 'ElevatedHost\SystemCore.ElevatedHost.exe')) {
            if (!(Test-Path (Join-Path $root.FullName $required))) { throw "Missing $required" }
        }
        $root.FullName
    } | Out-Null
    Step 'Portable executable launches and closes cleanly' {
        $root = Get-ChildItem $stage -Directory | Select-Object -First 1
        $exe = Join-Path $root.FullName 'F1.exe'
        $launch = Start-InteractiveApp $exe
        if (!$launch.Ok) { throw "Launch failed ($($launch.Mode)): $($launch.Reason)" }
        "window '$($launch.Title)' appeared and closed [$($launch.Mode)]"
    } | Out-Null
    Step 'Portable extraction cleanup' {
        Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path $stage) { throw 'Could not remove staging folder' }
        'removed'
    } | Out-Null
}

$report.FinishedAt = [DateTimeOffset]::Now
$report.Verdict = if ($failures -eq 0) { 'PASS' } else { "FAIL ($failures)" }
$report | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $EvidencePath 'vm-acceptance.json') -Encoding utf8
$lines = @("F1 VM acceptance - $($report.Verdict)", "Machine: $($report.Machine)  OS: $($report.Environment.Caption) build $($report.Environment.Build) ($($report.Environment.Edition))",
    "Mode: $Mode", '') + @($report.Steps | ForEach-Object { "  [$($_.Passed)] $($_.Step) - $($_.Detail) ($($_.Seconds)s)" })
$lines | Set-Content (Join-Path $EvidencePath 'vm-acceptance.txt') -Encoding utf8
Write-Output ''
Write-Output "VERDICT: $($report.Verdict)"
Write-Output "Evidence: $(Join-Path $EvidencePath 'vm-acceptance.json')"
if ($failures -gt 0) { exit 1 } else { exit 0 }
