param([string]$EvidencePath='artifacts/phase7/completion')
$ErrorActionPreference='Stop'
$evidence=$EvidencePath
$functional=Get-Content "$evidence/functional-result.json" -Raw|ConvertFrom-Json
$stability=Get-Content "$evidence/stability/result.json" -Raw|ConvertFrom-Json
if($functional.FunctionalChecks -ne 'PASS' -or !$stability.Completed -or !$stability.NormalClose){throw 'Completion evidence incomplete'}
$suites=[ordered]@{}
foreach($pair in @(@('MemoryUI','ui/checks.json'),@('SyntheticUI','synthetic-ui/checks.json'),@('NativeRegression','regression/ui-results.json'))){
 $checks=Get-Content "$evidence/$($pair[1])" -Raw|ConvertFrom-Json
 if(!$checks -or @($checks|Where-Object {!$_.Passed}).Count){throw "$($pair[0]) failed"}
 $suites[$pair[0]]=@($checks).Count
}
$compat=Get-Content "$evidence/compatibility.json" -Raw|ConvertFrom-Json
if(@($compat.Results|Where-Object Result -ne 'PASS').Count){throw 'Compatibility failed'}
$suites.Compatibility=@($compat.Results).Count
$tests=Get-Content "$evidence/memory-tests.txt" -Raw
$counts=($tests.Trim() -split '\r?\n')[-1]|ConvertFrom-Json
if($counts.Failed -ne 0){throw 'Memory suite failed'}
$suites.MemoryMetricsPressureOptimizationIntegration=$counts.Checks
foreach($file in Get-ChildItem "$evidence/core-regression" -Filter '*.txt'){
 $lines=Get-Content $file.FullName
 if($lines -match '^FAIL'){throw "Regression failure in $file"}
 $suites[$file.BaseName]=@($lines|Where-Object {$_ -match '^PASS'}).Count
}
foreach($name in @('startup','startup--mutation')){
 $lines=Get-Content "$evidence/$name.txt"
 if($lines -match '^FAIL'){throw "$name failure"}
 $suites[$name]=@($lines|Where-Object {$_ -match '^PASS'}).Count
}
$core=Get-Content "$evidence/core-lifecycle.json" -Raw|ConvertFrom-Json
$gui=Get-Content "$evidence/stability/process.json" -Raw|ConvertFrom-Json
$hashes=[ordered]@{}
foreach($assembly in @('Core','Infrastructure')){
 $paths=@("artifacts/phase7/app/SystemCore.$assembly.dll","artifacts/phase7/app/ElevatedHost/SystemCore.$assembly.dll","artifacts/phase7/validation-app/SystemCore.$assembly.dll","tests/SystemCore.MemoryToolsTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.$assembly.dll")
 $values=@($paths|ForEach-Object {(Get-FileHash $_).Hash})
 if(@($values|Select-Object -Unique).Count -ne 1){throw "$assembly engine mismatch"}
 $hashes[$assembly]=$values[0]
}
$production=[Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Resolve-Path artifacts/phase7/app/SystemCore.App.dll)))
$validation=[Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Resolve-Path artifacts/phase7/validation-app/SystemCore.App.dll)))
if($production.Contains('MemoryValidationEnvironment') -or !$validation.Contains('MemoryValidationEnvironment')){throw 'Fixture isolation failed'}
foreach($file in @('Debug','Release')){if((Get-Content "$evidence/$file.txt" -Raw) -notmatch '0 Warning\(s\)\s+0 Error\(s\)'){throw "$file not clean"}}
foreach($file in @('ui/stderr.txt','synthetic-ui/stderr.txt','regression/stderr.txt','stability/stderr.txt','stability/counter-error.txt')){
 if((Get-Item "$evidence/$file").Length -ne 0){throw "Nonempty $file"}
}
$realMatch=[regex]::Match($tests,'(?ms)^\{\r?\n  "Scope":.*?^\}')
if(!$realMatch.Success){throw 'Real device snapshot missing'}
$real=$realMatch.Value|ConvertFrom-Json
$syntheticLine=($tests -split '\r?\n'|Where-Object {$_ -like 'SYNTHETIC OPTIMIZATION EVIDENCE *'})
$synthetic=$syntheticLine.Substring('SYNTHETIC OPTIMIZATION EVIDENCE '.Length)|ConvertFrom-Json
$stages=[ordered]@{}
foreach($stage in @('page-open','cycle','page-hidden','minimized','cooldown')){
 $points=@($gui|Where-Object Stage -eq $stage);$first=$points[0];$last=$points[-1]
 $stages[$stage]=[ordered]@{Samples=$points.Count;Seconds=$last.Seconds-$first.Seconds;PrivateStart=$first.PrivateBytes;PrivateEnd=$last.PrivateBytes;PrivatePeak=($points.PrivateBytes|Measure-Object -Maximum).Maximum;HandlesStart=$first.Handles;HandlesEnd=$last.Handles;ThreadsStart=$first.Threads;ThreadsEnd=$last.Threads;NormalizedCpuPercent=100*($last.CpuSeconds-$first.CpuSeconds)/($last.Seconds-$first.Seconds)/[Environment]::ProcessorCount}
}
$counters=Import-Csv "$evidence/stability/counters.csv"
$managed=@($counters|Where-Object {$_.'Counter Name' -eq 'GC Heap Size (MB)'}|ForEach-Object {[Math]::Round([double]::Parse($_.'Mean/Increment',[Globalization.CultureInfo]::InvariantCulture)*1000000)})
if(!$managed.Count){throw 'GUI runtime managed-memory samples missing'}
$validManaged=@($managed|Where-Object {$_ -ge 0})
$exceptions=@($counters|Where-Object {$_.'Counter Name' -like 'Exception Count*'}|ForEach-Object {[double]::Parse($_.'Mean/Increment',[Globalization.CultureInfo]::InvariantCulture)})
$summary=[ordered]@{
 Phase=7;AnalysisGate='PASS';DevelopmentGate='PASS';ReleaseGate='PENDING';Phase8='Not started';GeneratedAt=[DateTimeOffset]::UtcNow.ToString('o')
 Evidence=$evidence;Publication='artifacts/phase7/app';AnalysisGateEvidence='artifacts/phase7/7a-verified/gate.json';Suites=$suites;EngineHashes=$hashes
 AppAssemblyHash=(Get-FileHash artifacts/phase7/app/SystemCore.App.dll).Hash
 ProductionFixtureExcluded=$true;CompatibilityHost=$compat;RealDevice=$real;Optimization=$synthetic;Gui=$stability;GuiStages=$stages
 GuiRuntimeCounters=[ordered]@{ManagedSamples=$managed.Count;InvalidManagedSamples=@($managed|Where-Object {$_ -lt 0}).Count;ManagedBytesFirst=$validManaged[0];ManagedBytesLast=$validManaged[-1];ManagedBytesMin=($validManaged|Measure-Object -Minimum).Minimum;ManagedBytesMax=($validManaged|Measure-Object -Maximum).Maximum;AllThrownExceptions=($exceptions|Measure-Object -Sum).Sum;ExceptionNote='Runtime count includes handled exceptions such as inaccessible process properties; not an unhandled-crash count';RawCsv="$evidence/stability/counters.csv"}
 CoreLifecycle=[ordered]@{Cycles=40;Checks=$core.Checks;FirstCycle=($core.Samples|Where-Object Phase -eq 'Cycle'|Select-Object -First 1);LastCycle=($core.Samples|Where-Object Phase -eq 'Cycle'|Select-Object -Last 1);LastCooldown=$core.Samples[-1];InvalidManagedSamples=@($core.Samples|Where-Object {!$_.ManagedValid}).Count;MaxSubscribers=($core.Samples.Subscribers|Measure-Object -Maximum).Maximum}
 Limitations=@('Desktop limited token, not a separate Standard User account','No production cache is currently independently releasable; Optimize legitimately returns no-op','Per-process disk/network and configured/current page-file use unavailable','Windows editions, extreme memory-pressure VMs, signing/install lifecycle, Narrator/DPI and long production soak remain Release QA')
}
$summary|ConvertTo-Json -Depth 20|Set-Content artifacts/phase7/summary.json -Encoding utf8
