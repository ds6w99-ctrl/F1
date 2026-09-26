param([string]$EvidenceRoot='artifacts/phase5')
$ErrorActionPreference='Stop'
function Lines([string]$relative){Get-Content (Join-Path $EvidenceRoot $relative)}
function Json([string]$relative){Get-Content (Join-Path $EvidenceRoot $relative) -Raw|ConvertFrom-Json}
function TextChecks([string]$relative){$lines=Lines $relative;[pscustomobject]@{Passed=@($lines|Where-Object {$_ -match '^PASS:'}).Count;Failed=@($lines|Where-Object {$_ -match '^FAIL:'}).Count;Evidence=$relative}}
function UiChecks([string]$relative){$checks=Json $relative;[pscustomobject]@{Passed=@($checks|Where-Object {$_.Passed}).Count;Failed=@($checks|Where-Object {!$_.Passed}).Count;Evidence=$relative}}
function Stats($samples){[pscustomobject]@{PrivateMin=($samples|Measure-Object PrivateBytes -Minimum).Minimum;PrivateMax=($samples|Measure-Object PrivateBytes -Maximum).Maximum;HandlesMin=($samples|Measure-Object Handles -Minimum).Minimum;HandlesMax=($samples|Measure-Object Handles -Maximum).Maximum}}
$idle=Json 'completion-stability/idle-process.json'
$core=Json 'completion-stability/core-lifecycle.json'
$active=@($core|Where-Object {!$_.CooldownSeconds -and $_.Cycle -gt 0})
$late=@($active|Where-Object {$_.Cycle -ge 6})
$invalid=@($core|Where-Object {$_.ManagedBytes -lt 0})
$compatibility=Json 'completion/compatibility-desktop-token.txt'
$scanMetrics=@((Lines 'completion/uninstaller-desktop-token.txt')|Where-Object {$_ -match '^\{"Metric":"SyntheticLeftoverScan"'}|ForEach-Object {$_|ConvertFrom-Json})
$hashes=foreach($assembly in @('SystemCore.Core.dll','SystemCore.Infrastructure.dll')) {
 $published=(Get-FileHash (Join-Path $EvidenceRoot "app/$assembly") -Algorithm SHA256).Hash
 $test=(Get-FileHash "tests/SystemCore.UninstallerTests/bin/Release/net10.0-windows10.0.26100.0/$assembly" -Algorithm SHA256).Hash
 [pscustomobject]@{Assembly=$assembly;Published=$published;Harness=$test;Match=$published -eq $test}
}
$summary=[pscustomobject]@{
 Generated=(Get-Date).ToString('o')
 Suites=@((TextChecks 'completion/uninstaller-desktop-token.txt'),(TextChecks 'verified/unit.txt'),(TextChecks 'final-checks/cleaner-scanner.txt'),(TextChecks 'verified/cleaner-executor.txt'),(TextChecks 'verified/cleaner-completion.txt'),(TextChecks 'completion/system-information.txt'),(TextChecks 'completion/performance.txt'),(UiChecks 'completion-ui/checks.json'),(UiChecks 'completion-regression/ui-results.json'),(TextChecks 'completion-stability/core-tests.txt'))
 Compatibility=[pscustomobject]@{Passed=@($compatibility.Results|Where-Object {$_.Result -eq 'PASS'}).Count;Failed=@($compatibility.Results|Where-Object {$_.Result -eq 'FAIL'}).Count;Elevated=$compatibility.Elevated;WindowsVersion=$compatibility.WindowsVersion;Edition=$compatibility.Edition}
 Idle=[pscustomobject]@{Result=(Json 'completion-stability/idle-result.json');Warm=(@($idle|Where-Object {$_.Seconds -ge 30})[0]);Last=$idle[-1];Stats=(Stats @($idle|Where-Object {$_.Seconds -ge 30}));StderrBytes=(Get-Item (Join-Path $EvidenceRoot 'completion-stability/idle-stderr.txt')).Length}
 Core=[pscustomobject]@{First=$core[0];FirstCycle=$active[0];LastCycle=$active[-1];CooldownLast=$core[-1];LateStats=(Stats $late);InvalidManagedSamples=$invalid;MetricPolicy='Negative GC.GetTotalMemory(false) samples excluded, raw readings retained; independent last-GC heap/commit available'}
 SyntheticScan=$scanMetrics
 EngineHashes=$hashes
 UiStderrBytes=(Get-Item (Join-Path $EvidenceRoot 'completion-ui/stderr.txt')).Length
 RegressionStderrBytes=(Get-Item (Join-Path $EvidenceRoot 'completion-regression/stderr.txt')).Length
}
$summary|ConvertTo-Json -Depth 10|Set-Content (Join-Path $EvidenceRoot 'summary.json') -Encoding utf8
$summary|ConvertTo-Json -Depth 10
if(@($summary.Suites|Where-Object {$_.Failed -gt 0}).Count -or $summary.Compatibility.Failed -gt 0 -or $summary.Compatibility.Elevated -or $summary.Idle.Result.Elevated -or @($hashes|Where-Object {!$_.Match}).Count){throw 'Evidence contains failed checks or a mismatched engine/token'}
