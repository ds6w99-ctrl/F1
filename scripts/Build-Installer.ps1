param(
    [Parameter(Mandatory=$true)][string]$Compiler,
    # installer/F1.iss is the product build. Pass installer/SystemCore.Phase0.iss for the
    # isolated phase-0 validation fixture. The two use different AppIds on purpose.
    [string]$Spec = 'installer/F1.iss',
    [switch]$EnableSigning,
    [string]$SigningCommand,
    [string]$PublisherThumbprint
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (!(Test-Path 'artifacts/publish/App/F1.pri')) { throw 'Build and publish the app first.' }
    if (!(Test-Path $Spec)) { throw "Installer spec not found: $Spec" }
    $version = & $Compiler --version
    if ($LASTEXITCODE -ne 0 -or "$version" -notmatch '7\.1\.0') { throw 'Inno Setup 7.1.0 is required.' }
    $argsList = @('--no-ide-signtools')
    if ($EnableSigning) {
        if (!$SigningCommand -or !$PublisherThumbprint) { throw 'Signing command and expected certificate thumbprint are required.' }
        foreach ($exe in @('App/F1.exe', 'App/ElevatedHost/SystemCore.ElevatedHost.exe')) {
            $signature = Get-AuthenticodeSignature "artifacts/publish/$exe"
            if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $PublisherThumbprint) {
                throw "Sign and verify the application payload before packaging: $exe"
            }
        }
        $argsList += @('--define=EnableSigning', "--signtool=systemcore=$SigningCommand")
    }
    $argsList += $Spec
    & $Compiler @argsList
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $output = Get-ChildItem 'artifacts/installer' -Filter '*.exe' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (!$output) { throw 'Installer compilation produced no output.' }
    $exe = "artifacts/installer/$($output.Name)"
    if ($EnableSigning) {
        $signature = Get-AuthenticodeSignature $exe
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $PublisherThumbprint) {
            throw 'Installer signing verification failed.'
        }
    }
    Get-FileHash $exe -Algorithm SHA256
} finally { Pop-Location }
