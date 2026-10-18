# VM-side part 2: installers that need an interactive session (VS Code user setup hangs in
# session 0). Runs as an INTERACTIVE elevated task (/IT /RL HIGHEST) in the F1Admin console session.
param([string]$AppsDir = 'C:\F1Audit\apps', [string]$Log = 'C:\F1Audit\install2.log')
$ErrorActionPreference = 'Continue'
[System.IO.File]::WriteAllText($Log, '')
function Step([string]$name) { $line = "=== $name @ $(Get-Date -Format o) ==="; Write-Host $line; Add-Content $Log $line -Encoding ASCII }
function Run([string]$name, [string]$exe, [string[]]$installArgs) {
    Step $name
    $p = Start-Process -FilePath $exe -ArgumentList $installArgs -Wait -PassThru
    $code = if ($p -and $p.HasExited) { $p.ExitCode } else { 'no-process' }
    $line = "exit=$code"
    Write-Host $line; Add-Content $Log $line -Encoding ASCII
}

Run 'VS Code user setup (interactive session)' "$AppsDir\VSCodeUserSetup-x64.exe" @('/VERYSILENT', '/NORESTART', '/MERGETASKS=!runcode')
Run 'Everything x64' "$AppsDir\Everything-1.4.1.1026.x64-Setup.exe" @('/S')
Run 'Chrome machine install' "$AppsDir\chrome_installer.exe" @('/silent', '/install')

Step 'Sentinel fixtures'
'audit sentinel: planted inside 7-Zip install dir' | Set-Content 'C:\Program Files\7-Zip\audit-sentinel.txt'
'audit sentinel: planted inside Notepad++ install dir' | Set-Content 'C:\Program Files\Notepad++\audit-sentinel.txt'
New-Item -ItemType Directory -Force 'C:\Program Files\Notepad++\audit-sub' | Out-Null
'nested sentinel' | Set-Content 'C:\Program Files\Notepad++\audit-sub\nested.txt'
'planted: 7-Zip, Notepad++ (+subdir)' | Add-Content $Log -Encoding ASCII

$line = "INSTALL PART2 DONE @ $(Get-Date -Format o)"
Write-Host $line; Add-Content $Log $line -Encoding ASCII
