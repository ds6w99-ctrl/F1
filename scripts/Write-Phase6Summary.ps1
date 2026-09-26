$ErrorActionPreference='Stop'
$root='artifacts/phase6/completion'
function UiSuite([string]$path){
 $rows=Get-Content $path -Raw|ConvertFrom-Json
 if(!$rows.Count -or @($rows|Where-Object {-not $_.Passed}).Count){throw "UI failure: $path"}
 [pscustomobject]@{Checks=$rows.Count;Passed=$true;Evidence=$path}
}
function TextSuite([string]$path){
 $lines=Get-Content $path
 $passes=@($lines|Where-Object {$_ -match '^PASS:'}).Count
 if(!$passes -or @($lines|Where-Object {$_ -match '^FAIL:'}).Count){throw "Test failure: $path"}
 [pscustomobject]@{Checks=$passes;Passed=$true;Evidence=$path}
}
$suites=[ordered]@{
 StartupInventory=TextSuite "$root/inventory-tests.txt"
 StartupMutation=TextSuite "$root/mutation.txt"
 MachineFixtures=TextSuite "$root/machine-fixture.txt"
 StartupSyntheticUI=UiSuite "$root/synthetic-ui/checks.json"
 StartupProductionUI=UiSuite "$root/production-ui/checks.json"
 NativeRegression=UiSuite "$root/regression/ui-results.json"
}
foreach($name in @('unit','cleaner','cleaner-executor','cleaner-completion','uninstaller','system-information','performance')){$suites[$name]=TextSuite "$root/core-regression/$name.txt"}
$compat=Get-Content "$root/compatibility.json" -Raw|ConvertFrom-Json
if(@($compat.Results|Where-Object {$_.Result -ne 'PASS'}).Count){throw 'Compatibility failure'}
$suites['Compatibility']=[pscustomobject]@{Checks=$compat.Results.Count;Passed=$true;Evidence="$root/compatibility.json"}
foreach($name in @('debug','release')){
 $text=Get-Content "$root/$name.txt" -Raw
 if($text -notmatch 'Build succeeded' -or $text -notmatch '0 Warning' -or $text -notmatch '0 Error'){throw "$name build not clean"}
}
$idle=Get-Content "$root/stability/idle-process.json" -Raw|ConvertFrom-Json
$idleResult=Get-Content "$root/stability/idle-result.json" -Raw|ConvertFrom-Json
$core=Get-Content "$root/stability/core.json" -Raw|ConvertFrom-Json
$cycles=@($core.Samples|Where-Object Phase -eq 'Cycle')
$cooldown=@($core.Samples|Where-Object Phase -eq 'Cooldown')
if(!$idleResult.NormalClose -or $idleResult.Elevated -or @($idle|Where-Object {-not $_.Responding}).Count -or $cycles.Count -ne 20 -or $core.Checks -ne 380){throw 'Stability incomplete'}
$hashes=@()
foreach($assembly in @('SystemCore.App.dll','SystemCore.Infrastructure.dll')){
 if([Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Resolve-Path "artifacts/phase6/app/$assembly"))).Contains('StartupValidationEnvironment')){throw "Validation entry point in production: $assembly"}
}
foreach($name in @('SystemCore.Core.dll','SystemCore.Infrastructure.dll')){
 $app=(Get-FileHash "artifacts/phase6/app/$name").Hash
 $test=(Get-FileHash "tests/SystemCore.StartupTests/bin/Release/net10.0-windows10.0.26100.0/$name").Hash
 $helper=(Get-FileHash "artifacts/phase6/app/ElevatedHost/$name").Hash
 if($app -ne $test -or $app -ne $helper){throw "Engine mismatch: $name"}
 $hashes+=[pscustomobject]@{Assembly=$name;Sha256=$app;TestAndHelperMatch=$true}
}
$warm=@($idle|Where-Object Seconds -ge 20)[0]
$inventory=Get-Content "$root/inventory.json" -Raw|ConvertFrom-Json
if(!$inventory.Complete){throw 'Final real inventory incomplete'}
$result=[ordered]@{
 Phase6A='PASS';Development='PASS';Release='PENDING';CompletedAt=[DateTimeOffset]::UtcNow.ToString('o')
 Report='docs/PHASE-6-REPORT.md';Publication='artifacts/phase6/app/SystemCore.App.exe'
 Inventory=$inventory
 Suites=$suites;CoreLifecycleChecks=$core.Checks;EngineHashes=$hashes
 GuiIdle=[ordered]@{Seconds=$idleResult.MeasuredSeconds;TokenElevated=$idleResult.Elevated;NormalClose=$idleResult.NormalClose;ExitCode=$idleResult.ExitCode;
  WarmupPrivateBytes=$warm.PrivateBytes;FinalPrivateBytes=$idle[-1].PrivateBytes;PeakPrivateBytes=($idle|Measure-Object PrivateBytes -Maximum).Maximum;
  WarmupHandles=$warm.Handles;FinalHandles=$idle[-1].Handles;Method=$idleResult.Method}
 CoreStability=[ordered]@{Cycles=$cycles.Count;Cycle1=$cycles[0];Cycle20=$cycles[-1];CooldownFinal=$cooldown[-1];
  LateCycleMinimumPrivateBytes=($cycles|Where-Object Cycle -ge 10|Measure-Object PrivateBytes -Minimum).Minimum;
  LateCycleMaximumPrivateBytes=($cycles|Where-Object Cycle -ge 10|Measure-Object PrivateBytes -Maximum).Maximum;
  InvalidManagedSamples=@($core.Samples|Where-Object {-not $_.ManagedValid}).Count}
 Limits='Synthetic mutations only. GUI memory measured without UIA. Full release/UAC/edition/enterprise/Narrator/DPI/long-soak matrices remain Release QA. Phase 7 awaits owner review.'
}
$result|ConvertTo-Json -Depth 10|Set-Content artifacts/phase6/summary.json -Encoding utf8
$result|ConvertTo-Json -Depth 10
