param(
    [string]$ProjectPath = "C:\Users\Falcone\Documents\F1",
    [string]$RemoteUrl = ""
)

$ErrorActionPreference = "Stop"

Write-Host "=== F1 Git Setup ===" -ForegroundColor Cyan
Write-Host "Project: $ProjectPath"

if (!(Test-Path $ProjectPath)) {
    throw "Project path does not exist: $ProjectPath"
}

Set-Location $ProjectPath

function Get-GitPath {
    $cmd = Get-Command git -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @(
        "$env:ProgramFiles\Git\cmd\git.exe",
        "$env:ProgramFiles\Git\bin\git.exe",
        "${env:ProgramFiles(x86)}\Git\cmd\git.exe"
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) {
            return $candidate
        }
    }

    return $null
}

$git = Get-GitPath

if (!$git) {
    Write-Host "Git not found. Installing Git with winget..." -ForegroundColor Yellow

    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (!$winget) {
        throw "winget is not available. Install Git manually from https://git-scm.com/ and run this script again."
    }

    winget install --id Git.Git -e --source winget --accept-source-agreements --accept-package-agreements --silent

    $git = Get-GitPath
    if (!$git) {
        throw "Git was installed but could not be found. Close PowerShell, open it again, then rerun this script."
    }
}

Write-Host "Git: $(& $git --version)" -ForegroundColor Green

# Global user identity (ask only if missing)
$currentName = & $git config --global user.name
$currentEmail = & $git config --global user.email

if ([string]::IsNullOrWhiteSpace($currentName)) {
    $name = Read-Host "Git user.name (example: Falcone)"
    if (![string]::IsNullOrWhiteSpace($name)) {
        & $git config --global user.name $name
    }
}

if ([string]::IsNullOrWhiteSpace($currentEmail)) {
    $email = Read-Host "Git user.email"
    if (![string]::IsNullOrWhiteSpace($email)) {
        & $git config --global user.email $email
    }
}

& $git config --global core.autocrlf true
& $git config --global init.defaultBranch main

# Initialize repo only if not already a repository
$insideRepo = $false
try {
    $result = & $git rev-parse --is-inside-work-tree 2>$null
    if ($result -eq "true") { $insideRepo = $true }
} catch {}

if (!$insideRepo) {
    Write-Host "Initializing Git repository..." -ForegroundColor Cyan
    & $git init
    & $git branch -M main
} else {
    Write-Host "Existing Git repository detected." -ForegroundColor Green
}

# .gitignore
$gitignorePath = Join-Path $ProjectPath ".gitignore"
$requiredIgnore = @'
# Visual Studio / Rider
.vs/
.idea/
*.user
*.suo
*.userosscache
*.sln.docstates

# .NET / WinUI build outputs
**/bin/
**/obj/
TestResults/
coverage/
.coverage/
*.trx
*.coverage
*.coveragexml

# Publish / generated artifacts
artifacts/
publish/

# Temporary / logs
*.tmp
*.temp
*.log
*.bak
*.cache

# OS
Thumbs.db
Desktop.ini

# Local diagnostics
*.dmp
*.etl
*.etlx
*.gcdump

# NuGet
packages/
*.nupkg
*.snupkg

# Keep source-controlled documentation and assets
!docs/
!src/
!tests/
!scripts/
'@

if (!(Test-Path $gitignorePath)) {
    Set-Content -Path $gitignorePath -Value $requiredIgnore -Encoding UTF8
    Write-Host "Created .gitignore" -ForegroundColor Green
} else {
    $existing = Get-Content $gitignorePath -Raw
    if ($existing -notmatch "F1 Git baseline rules") {
        Add-Content -Path $gitignorePath -Value "`n# F1 Git baseline rules`n$requiredIgnore" -Encoding UTF8
        Write-Host "Updated .gitignore" -ForegroundColor Green
    }
}

# .gitattributes
$gitattributesPath = Join-Path $ProjectPath ".gitattributes"
$attrs = @'
* text=auto

*.cs text eol=crlf
*.xaml text eol=crlf
*.ps1 text eol=crlf
*.cmd text eol=crlf
*.bat text eol=crlf

*.md text eol=lf
*.json text eol=lf
*.yml text eol=lf
*.yaml text eol=lf

*.png binary
*.jpg binary
*.jpeg binary
*.ico binary
*.zip binary
*.exe binary
*.dll binary
'@

if (!(Test-Path $gitattributesPath)) {
    Set-Content -Path $gitattributesPath -Value $attrs -Encoding UTF8
    Write-Host "Created .gitattributes" -ForegroundColor Green
}

# Show status before baseline
Write-Host "`n=== Git status before baseline ===" -ForegroundColor Cyan
& $git status --short

# Stage source baseline
& $git add .

# Commit if there are staged changes
$staged = & $git diff --cached --name-only
if ($staged) {
    Write-Host "`nCreating clean F1 baseline commit..." -ForegroundColor Cyan
    & $git commit -m "chore: clean F1 baseline before next OpenCode session"
} else {
    Write-Host "No staged changes to commit." -ForegroundColor Yellow
}

# Tag the known pre-Phase-10 baseline if possible
$tagExists = & $git tag -l "pre-phase10-baseline"
if (!$tagExists) {
    try {
        & $git tag -a "pre-phase10-baseline" -m "F1 clean baseline before Phase 10"
        Write-Host "Created tag: pre-phase10-baseline" -ForegroundColor Green
    } catch {
        Write-Host "Tag skipped: no commit available yet." -ForegroundColor Yellow
    }
}

# Optional remote setup
if (![string]::IsNullOrWhiteSpace($RemoteUrl)) {
    $origin = & $git remote get-url origin 2>$null
    if ($LASTEXITCODE -eq 0 -and $origin) {
        Write-Host "Updating origin remote..." -ForegroundColor Cyan
        & $git remote set-url origin $RemoteUrl
    } else {
        Write-Host "Adding origin remote..." -ForegroundColor Cyan
        & $git remote add origin $RemoteUrl
    }

    Write-Host "Remote: $RemoteUrl" -ForegroundColor Green

    Write-Host "Pushing main and baseline tag..." -ForegroundColor Cyan
    & $git push -u origin main
    & $git push origin pre-phase10-baseline
} else {
    Write-Host "`nNo RemoteUrl supplied. Git is configured locally." -ForegroundColor Yellow
    Write-Host "Later you can connect GitHub with:"
    Write-Host 'git remote add origin https://github.com/YOURNAME/YOURREPO.git'
    Write-Host 'git push -u origin main'
}

Write-Host "`n=== Final status ===" -ForegroundColor Cyan
& $git status
Write-Host "`n=== Recent commit ===" -ForegroundColor Cyan
& $git log -1 --oneline

Write-Host "`nF1 Git setup complete." -ForegroundColor Green
Write-Host "Baseline tag: pre-phase10-baseline"
