param(
    [Parameter(Mandatory=$true)][string]$Compiler,
    [switch]$EnableSigning,
    [string]$SigningCommand,
    [string]$PublisherThumbprint
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (!(Test-Path 'artifacts/publish/App/F1.pri')) { throw 'Build and publish the app first.' }
    $version = & $Compiler --version
    if ($LASTEXITCODE -ne 0 -or "$version" -notmatch '7\.1\.0') { throw 'Inno Setup 7.1.0 is required.' }
    $argsList = @('--no-ide-signtools')
    if ($EnableSigning) {
        if (!$SigningCommand -or !$PublisherThumbprint) { throw 'Signing command and expected certificate thumbprint are required.' }
        foreach ($exe in @('App/F1.exe', 'ElevatedHost/SystemCore.ElevatedHost.exe')) {
            $signature = Get-AuthenticodeSignature "artifacts/publish/$exe"
            if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $PublisherThumbprint) {
                throw "Sign and verify the application payload before packaging: $exe"
            }
        }
        $argsList += @('--define=EnableSigning', "--signtool=systemcore=$SigningCommand")
    }
    $argsList += 'installer/SystemCore.Phase0.iss'
    & $Compiler @argsList
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $exe = 'artifacts/installer/SystemCore-Phase0-0.0.1-win-x64-setup.exe'
    if ($EnableSigning) {
        $signature = Get-AuthenticodeSignature $exe
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $PublisherThumbprint) {
            throw 'Installer signing verification failed.'
        }
    }
    Get-FileHash $exe -Algorithm SHA256
} finally { Pop-Location }
