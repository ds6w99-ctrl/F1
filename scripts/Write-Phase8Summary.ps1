param([string]$EvidencePath='artifacts/phase8/completion')
$ErrorActionPreference='Stop'
$evidence=$EvidencePath
$core=Get-Content "$evidence/core/result.json" -Raw|ConvertFrom-Json
$runtime=Get-Content "$evidence/stability/result.json" -Raw|ConvertFrom-Json
if($core.Result -ne 'PASS' -or !$runtime.Completed -or !$runtime.NormalClose -or !$runtime.RestartNormalClose){throw 'Completion evidence incomplete'}
$suites=[ordered]@{}
foreach($pair in @(@('ProductUI','ui/checks.json'),@('ThemeLocalization','theme-localization/checks.json'),@('MemoryUI','memory-ui/checks.json'),@('NativeRegression','shell-regression/ui-results.json'))){
 $checks=Get-Content "$evidence/$($pair[1])" -Raw|ConvertFrom-Json
 if(!$checks -or @($checks|Where-Object {!$_.Passed}).Count){throw "$($pair[0]) failed"}
 $suites[$pair[0]]=@($checks).Count
}
foreach($file in Get-ChildItem "$evidence/core" -Recurse -Filter '*.txt'){
 $lines=Get-Content $file.FullName
 if($lines -match '^FAIL'){throw "Failed suite: $file"}
 $suites[$file.BaseName]=@($lines|Where-Object {$_ -match '^PASS'}).Count
}
$compat=Get-Content "$evidence/core/compatibility.json" -Raw|ConvertFrom-Json
if(@($compat.Results|Where-Object Result -ne 'PASS').Count){throw 'Compatibility failed'}
$suites.Compatibility=@($compat.Results).Count
foreach($configuration in @('Debug','Release')){if((Get-Content "$evidence/build/$configuration.txt" -Raw) -notmatch '0 Warning\(s\)\s+0 Error\(s\)'){throw "$configuration build not clean"}}
$hashes=[ordered]@{}
$testedHashes=Get-Content "$evidence/core/engine-hashes.json" -Raw|ConvertFrom-Json
foreach($assembly in @('Core','Infrastructure')){
 $paths=@("artifacts/phase8/app/SystemCore.$assembly.dll","artifacts/phase8/app/ElevatedHost/SystemCore.$assembly.dll","tests/SystemCore.ProductTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.$assembly.dll","tests/SystemCore.MemoryToolsTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.$assembly.dll")
 $values=@($paths|ForEach-Object {(Get-FileHash $_).Hash})
 if(@($values|Select-Object -Unique).Count -ne 1){throw "$assembly engine mismatch"}
 if($values[0] -ne $testedHashes.$assembly){throw "$assembly differs from the accepted Core run; rerun Core validation"}
 $hashes[$assembly]=$values[0]
}
$assemblyText=[Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Resolve-Path artifacts/phase8/app/SystemCore.App.dll)))
if($assemblyText.Contains('MemoryValidationEnvironment')){throw 'Production contains memory fixture'}
$stderr=@(Get-ChildItem $evidence -Recurse -File|Where-Object {$_.Name -like 'stderr*' -or $_.Name -eq 'counter-error.txt'})
if(@($stderr|Where-Object Length -gt 0).Count){throw 'Review nonempty stderr before gate'}
$gui=Get-Content "$evidence/stability/process.json" -Raw|ConvertFrom-Json
if(@($gui|Where-Object {!$_.Responding}).Count){throw 'Unresponsive process sample'}
$stages=[ordered]@{}
foreach($stage in @('initial-dashboard','cycle','cooldown','minimized')){
 $points=@($gui|Where-Object Stage -eq $stage);$first=$points[0];$last=$points[-1]
 $stages[$stage]=[ordered]@{Samples=$points.Count;Seconds=$last.Seconds-$first.Seconds;PrivateStart=$first.PrivateBytes;PrivateEnd=$last.PrivateBytes;PrivatePeak=($points.PrivateBytes|Measure-Object -Maximum).Maximum;WorkingSetStart=$first.WorkingSet;WorkingSetEnd=$last.WorkingSet;HandlesStart=$first.Handles;HandlesEnd=$last.Handles;ThreadsStart=$first.Threads;ThreadsEnd=$last.Threads;NormalizedCpuPercent=100*($last.CpuSeconds-$first.CpuSeconds)/($last.Seconds-$first.Seconds)/[Environment]::ProcessorCount}
}
$counters=Import-Csv "$evidence/stability/counters.csv"
$managed=@($counters|Where-Object {$_.'Counter Name' -eq 'GC Heap Size (MB)'}|ForEach-Object {[Math]::Round([double]::Parse($_.'Mean/Increment',[Globalization.CultureInfo]::InvariantCulture)*1000000)})
if(!$managed.Count){throw 'Managed samples missing'}
$validManaged=@($managed|Where-Object {$_ -ge 0})
$exceptions=@($counters|Where-Object {$_.'Counter Name' -like 'Exception Count*'}|ForEach-Object {[double]::Parse($_.'Mean/Increment',[Globalization.CultureInfo]::InvariantCulture)})
$summary=[ordered]@{
 Phase=8;DevelopmentGate='PASS';OwnerReview='PENDING';ReleaseGate='PENDING';Phase9='Not started';Phase10='Not started';GeneratedAt=[DateTimeOffset]::UtcNow.ToString('o');Evidence=$evidence;Publication='artifacts/phase8/app';Suites=$suites
 EngineHashes=$hashes;AppAssemblyHash=(Get-FileHash artifacts/phase8/app/SystemCore.App.dll).Hash;ProductionMemoryFixtureExcluded=$true;CompatibilityHost=$compat
 Gui=$runtime;GuiStages=$stages
 GuiRuntimeCounters=[ordered]@{ManagedSamples=$managed.Count;InvalidManagedSamples=@($managed|Where-Object {$_ -lt 0}).Count;ManagedBytesFirst=$validManaged[0];ManagedBytesLast=$validManaged[-1];ManagedBytesMin=($validManaged|Measure-Object -Minimum).Minimum;ManagedBytesMax=($validManaged|Measure-Object -Maximum).Maximum;AllThrownExceptions=($exceptions|Measure-Object -Sum).Sum;ExceptionNote='Runtime counter includes handled exceptions. Types were not traced; this is not an unhandled-crash count.';RawCsv="$evidence/stability/counters.csv"}
 Diagnostics=[ordered]@{MaxLogFiles=10;MaxLogFileBytes=2097152;MaxLogBytes=20971520;QueueCapacity=128;MaxManualExports=5;ExportEvidence="$evidence/ui/diagnostic-export.zip";Uploaded=$false}
 Limitations=@('Desktop limited token, not a separate Standard User account','Tool availability checks handler/files; organization policy may still restrict a page','Other tool launches use injected platform verification; real System Information launch is separately recorded','An interrupted export can leave an incomplete ZIP occupying a package slot; remove through the Exports folder','Initial private memory grew and did not return to startup baseline; later-cycle and cooldown evidence supports only this observed development workload','ExitCode was null; normal close observed, zero exit code not verified','Full Windows edition/account/Narrator/DPI matrix, signing, installer, clean VM and long production soak remain Release QA')
}
$summary|ConvertTo-Json -Depth 12|Set-Content artifacts/phase8/summary.json -Encoding utf8
[pscustomobject]@{Result='PASS';EngineHashes=$hashes;AppAssemblyHash=$summary.AppAssemblyHash;CleanBuilds=$true;EmptyStderr=$true;GeneratedAt=$summary.GeneratedAt}|ConvertTo-Json -Depth 4|Set-Content "$evidence/final-verification.json" -Encoding utf8
