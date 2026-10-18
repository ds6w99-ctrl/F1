# Host-side audit orchestrator: one uninstall round against the VM.
# Runs the F1 engine probe (uninstall-clean: uninstall -> leftover scan -> optional delete in one
# process, mirroring the UI flow), captures independent before/after system states, diffs them,
# and copies all evidence back to the host.
param(
    [string]$VmIp = '172.31.123.171',
    [string]$AdminUser = 'F1Admin',
    [string]$AdminPass = 'F1Vm!2026',
    [string]$Round,                    # e.g. 'U-02'
    [Parameter(Mandatory)][string]$AppName,  # exact DisplayName for the probe
    [string]$Keywords = '',            # semicolon-separated state-diff keywords
    [switch]$Quiet,
    [switch]$Deep,
    [switch]$LeftoverDelete,
    [switch]$IncludePersonal,
    [int]$TimeoutMin = 15
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$evidence = Join-Path $root "artifacts\audit-baseline\vm-evidence\$Round"
New-Item -ItemType Directory -Force $evidence | Out-Null
$kwList = @($Keywords -split ';' | Where-Object { $_ } | ForEach-Object { $_.Trim() })
$sec = ConvertTo-SecureString $AdminPass -AsPlainText -Force
$cred = New-Object System.Management.Automation.PSCredential($AdminUser, $sec)
$s = New-PSSession -ComputerName $VmIp -Credential $cred -Authentication Negotiate -ErrorAction Stop
try {
    # ---- 1. uninstall + leftover scan (+ optional delete) via the F1 engine probe ----
    $quietFlag = if ($Quiet) { ' --quiet' } else { '' }
    $deepFlag = if ($Deep) { ' --deep' } else { '' }
    $delFlag = if ($LeftoverDelete) { ' --delete' } else { '' }
    $incFlag = if ($IncludePersonal) { ' --include-personal' } else { '' }
    $outPath = "C:\F1Audit\evidence\$Round-uninstall.json"
    $cmd = "& 'C:\F1Audit\probe\RealWorldProbe.exe' uninstall-clean --name '$AppName' --out '$outPath' --timeout-min $TimeoutMin$quietFlag$deepFlag$delFlag$incFlag"
    $result = Invoke-Command -Session $s -ScriptBlock { param($c) $o = & { Invoke-Expression $c } 2>&1 | Out-String; "exit=$LASTEXITCODE`n$o" } -ArgumentList $cmd
    $result | Tee-Object (Join-Path $evidence 'uninstall.txt')
    if ($result -notmatch 'exit=0') { Write-Warning "probe exit != 0 for $AppName" }

    # ---- 2. capture state after uninstall(+cleanup) and diff ----
    Invoke-Command -Session $s -ScriptBlock {
        param($round, $kwJoined)
        $kwList = @($kwJoined -split ';' | Where-Object { $_ })
        Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
        & 'C:\F1Audit\scripts\Capture-State.ps1' -Label "$round-after" -OutDir 'C:\F1Audit\states' -AppKeyNames @($kwList)
        & 'C:\F1Audit\scripts\Compare-States.ps1' -Before 'C:\F1Audit\states\installed.json' -After "C:\F1Audit\states\$round-after.json" -Out "C:\F1Audit\evidence\$round-compare.json" -Keywords @($kwList)
    } -ArgumentList $Round, ($kwList -join ';')

    # ---- 3. pull evidence ----
    Invoke-Command -Session $s -ScriptBlock {
        param($round)
        New-Item -ItemType Directory -Force 'C:\F1Audit\pull' | Out-Null
        Remove-Item 'C:\F1Audit\pull\*' -Recurse -Force -ErrorAction SilentlyContinue
        Copy-Item 'C:\F1Audit\evidence\*' -Destination 'C:\F1Audit\pull\' -Include "*$round*" -Force -ErrorAction SilentlyContinue
    } -ArgumentList $Round
    Copy-Item -FromSession $s 'C:\F1Audit\pull' -Destination $evidence -Recurse -Force -ErrorAction SilentlyContinue
    Get-ChildItem $evidence -Recurse -File | Select-Object Name, Length | Format-Table -AutoSize
    "ROUND $Round COMPLETE -> $evidence"
} finally { Remove-PSSession $s }
