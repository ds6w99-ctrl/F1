param([string]$UiEvidence='artifacts/f1-ui/test-results/ui-06',[string]$StabilityEvidence='artifacts/f1-ui/test-results/stability')
$ErrorActionPreference='Stop'
$root='artifacts/f1-ui';$suites=[ordered]@{}
Get-ChildItem "$root/test-results/core" -Recurse -File -Filter '*.txt'|ForEach-Object{
 $text=[IO.File]::ReadAllText($_.FullName)
 if($text -match '(?m)^FAIL(?::| )'){throw "Failed core suite $($_.Name)"}
 $suites['Core/'+$_.BaseName]=[regex]::Matches($text,'(?m)^PASS(?::| )').Count
}
$compat=Get-Content "$root/test-results/core/compatibility.json" -Raw|ConvertFrom-Json
if(@($compat.Results|Where-Object Result -ne 'PASS').Count){throw 'Compatibility failed'}
$suites.Compatibility=@($compat.Results).Count
$checkFiles=@("$UiEvidence/checks.json","$root/screenshots/checks.json","$root/screenshots-populated/checks.json","$root/test-results/pages/memory/checks.json","$root/test-results/pages/themes/theme-localization/checks.json","$root/test-results/pages/shell/ui-results.json","$root/test-results/interactions/checks.json","$root/test-results/calibration/checks.json","$root/test-results/helper-identity/checks.json","$root/test-results/about-direction/checks.json")
$checkFiles+=@(Get-ChildItem "$root/test-results/fixtures" -Recurse -Filter 'checks.json'|ForEach-Object FullName)
$checkFiles+="$root/test-results/memory-framing/checks.json"
foreach($file in $checkFiles){$checks=Get-Content $file -Raw|ConvertFrom-Json;if(!$checks.Count -or @($checks|Where-Object{!$_.Passed}).Count){throw "Failed checks $file"};$suites[$file]=$checks.Count}
$completed=@{
 "$UiEvidence/checks.json"='Restart closes normally';
 "$root/screenshots/checks.json"='Normal close';
 "$root/screenshots-populated/checks.json"='Populated capture closes normally';
 "$root/test-results/pages/memory/checks.json"='Memory app closes normally';
 "$root/test-results/pages/themes/theme-localization/checks.json"='Theme test closes normally';
 "$root/test-results/pages/shell/ui-results.json"='Final clean close';
 "$root/test-results/interactions/checks.json"='Alt F4 closes normally';
 "$root/test-results/calibration/checks.json"='Calibration normal close';
 "$root/test-results/about-direction/checks.json"='Normal close';
 "$root/test-results/memory-framing/checks.json"='Memory framing normal close';
 "$root/test-results/fixtures/uninstaller/checks.json"='Normal shutdown';
 "$root/test-results/fixtures/startup/checks.json"='Fixture source and state teardown verified';
 "$root/test-results/fixtures/memory/checks.json"='Synthetic normal shutdown';
 "$root/test-results/fixtures/cleaner/checks.json"='Clean shutdown'
}
foreach($file in $completed.Keys){$checks=Get-Content $file -Raw|ConvertFrom-Json;if(!@($checks|Where-Object {$_.Test -eq $completed[$file] -and $_.Passed}).Count){throw "Completion proof missing: $file"}}
$helper=Get-Content "$root/test-results/helper-identity/result.json" -Raw|ConvertFrom-Json
if($helper.Result -ne 'PASS' -or $helper.Checks -ne 8){throw 'Helper identity fixture incomplete'}
$export=Get-Content "$root/test-results/export-review.json" -Raw|ConvertFrom-Json
if($export.Result -ne 'PASS'){throw 'Export review incomplete'}
$sourceReview=Get-Content "$root/test-results/source-change-review.json" -Raw|ConvertFrom-Json
if($sourceReview.Result -ne 'PASS' -or !$sourceReview.ExistingDependencyLocksUnchanged -or !$sourceReview.ArchivedFilesStillPresent){throw 'Source compatibility review incomplete'}
foreach($file in $sourceReview.CurrentArchivedSourceHashes){if((Get-FileHash $file.File).Hash -ne $file.Sha256){throw "Source review stale: $($file.File)"}}
$runtime=Get-Content "$StabilityEvidence/result.json" -Raw|ConvertFrom-Json
$baseline=Get-Content "$root/baseline/stability/result.json" -Raw|ConvertFrom-Json
foreach($run in @($runtime,$baseline)){
 if(!$run.Completed -or !$run.NormalClose -or !$run.RestartNormalClose -or $run.Elevated -or $run.Cycles -ne 6 -or $run.Seconds -lt 300){throw 'Stability incomplete'}
 if(@($run.ControllerAssemblies|Where-Object{$_ -match 'UIAutomation'}).Count){throw 'UIAutomation was loaded in passive measurement'}
}
foreach($file in (Get-ChildItem $StabilityEvidence -Filter 'stderr-*.txt')){if($file.Length -ne 0){throw "Runtime stderr requires review: $($file.Name)"}}
function Metrics([string]$path){
 $all=Get-Content "$path/process.json" -Raw|ConvertFrom-Json
 $active=@($all|Where-Object Stage -ne 'restart');$cycles=@($all|Where-Object Stage -eq 'cycle');$cool=@($all|Where-Object Stage -eq 'cooldown');$min=@($all|Where-Object Stage -eq 'minimized')
 if(@($active|Where-Object{!$_.Responding}).Count){throw 'Unresponsive runtime sample'}
 if($cycles.Count -ne 6 -or $min.Count -ne 24 -or $cool.Count -ne 24){throw 'Matched native workload samples missing'}
 $stageCpu=[ordered]@{};$stages=[ordered]@{}
 foreach($stage in @('initial-dashboard','cycle','cooldown','minimized')){
  $samples=@($active|Where-Object Stage -eq $stage)
  $stageCpu[$stage]=[Math]::Round(100*($samples[-1].CpuSeconds-$samples[0].CpuSeconds)/($samples[-1].Seconds-$samples[0].Seconds)/[Environment]::ProcessorCount,3)
  $stages[$stage]=[ordered]@{Samples=$samples.Count;FirstToLastSeconds=[Math]::Round($samples[-1].Seconds-$samples[0].Seconds,3);PrivateMiB=@([Math]::Round($samples[0].PrivateBytes/1MB,3),[Math]::Round($samples[-1].PrivateBytes/1MB,3));Handles=@($samples[0].Handles,$samples[-1].Handles);Threads=@($samples[0].Threads,$samples[-1].Threads);NormalizedCpuPercent=$stageCpu[$stage]}
 }
 $late=@($cycles|Select-Object -Last 3|ForEach-Object{[Math]::Round($_.PrivateBytes/1MB,3)})
 [ordered]@{CyclePrivateMB=@([Math]::Round($cycles[0].PrivateBytes/1MB,3),[Math]::Round($cycles[-1].PrivateBytes/1MB,3));CooldownPrivateMB=@([Math]::Round($cool[0].PrivateBytes/1MB,3),[Math]::Round($cool[-1].PrivateBytes/1MB,3));MinimizedPrivateMB=@([Math]::Round($min[0].PrivateBytes/1MB,3),[Math]::Round($min[-1].PrivateBytes/1MB,3));CycleHandles=@($cycles[0].Handles,$cycles[-1].Handles);CycleThreads=@($cycles[0].Threads,$cycles[-1].Threads);NormalizedCpuPercent=[Math]::Round(100*($active[-1].CpuSeconds-$active[0].CpuSeconds)/($active[-1].Seconds-$active[0].Seconds)/[Environment]::ProcessorCount,3);StageNormalizedCpu=$stageCpu;Stages=$stages;LateCyclePrivateMiBRange=@(($late|Measure-Object -Minimum).Minimum,($late|Measure-Object -Maximum).Maximum);LogicalProcessorCount=[Environment]::ProcessorCount;UnresponsiveSamples=0;SampleCount=$active.Count;Units='Private MB compatibility fields use 1024^2 bytes (MiB). CPU normalized across host logical processors. Stage duration is first-to-last sample; minimized window is 24 waits of 5 seconds.'}
}
function RuntimeCounters([string]$path){
 $rows=Import-Csv "$path/counters.csv"
 $heap=@($rows|Where-Object 'Counter Name' -eq 'GC Heap Size (MB)'|ForEach-Object{[double]::Parse($_.'Mean/Increment',[Globalization.CultureInfo]::InvariantCulture)})
 if(!$heap.Count -or @($heap|Where-Object{$_ -lt 0}).Count){throw 'Missing or invalid managed heap counter'}
 $throws=@($rows|Where-Object 'Counter Name' -like 'Exception Count*'|ForEach-Object{[double]::Parse($_.'Mean/Increment',[Globalization.CultureInfo]::InvariantCulture)})
 $timers=@($rows|Where-Object 'Counter Name' -eq 'Number of Active Timers'|ForEach-Object{[double]::Parse($_.'Mean/Increment',[Globalization.CultureInfo]::InvariantCulture)})
 [ordered]@{ManagedHeapSamples=$heap.Count;ManagedHeapCounterMBRange=@(($heap|Measure-Object -Minimum).Minimum,($heap|Measure-Object -Maximum).Maximum);IntervalThrowSum=($throws|Measure-Object -Sum).Sum;TimerRange=@(($timers|Measure-Object -Minimum).Minimum,($timers|Measure-Object -Maximum).Maximum);Note='System.Runtime counter MB units as emitted; throw counts include handled exceptions and do not identify catch outcomes.'}
}
$hashes=[ordered]@{}
foreach($name in @('F1.dll','F1.exe','F1.pri','SystemCore.Core.dll','SystemCore.Infrastructure.dll','ElevatedHost/SystemCore.ElevatedHost.exe')){$hashes[$name]=(Get-FileHash "$root/publish/$name").Hash}
foreach($assembly in @('Core','Infrastructure')){
 $expected=$hashes["SystemCore.$assembly.dll"]
 foreach($path in @("$root/publish/ElevatedHost/SystemCore.$assembly.dll","tests/SystemCore.ProductTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.$assembly.dll","tests/SystemCore.SecurityTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.$assembly.dll")){if((Get-FileHash $path).Hash -ne $expected){throw 'Final engine hash mismatch'}}
}
foreach($file in $helper.HelperHashes){if((Get-FileHash "$root/publish/ElevatedHost/$($file.File)").Hash -ne $file.Sha256){throw 'Helper identity evidence differs from delivery'}}
$app=[Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Resolve-Path "$root/publish/F1.dll")))
foreach($marker in @('ReliabilityAudit','MemoryValidationEnvironment','StartupValidationEnvironment','CleanerValidationPrepare')){if($app.Contains($marker)){throw "Fixture/audit instrumentation in ordinary F1: $marker"}}
$shots=Get-Content "$root/screenshots/metadata.json" -Raw|ConvertFrom-Json
$populated=Get-Content "$root/screenshots-populated/metadata.json" -Raw|ConvertFrom-Json
if(@($populated|Where-Object AssemblySha256 -ne $hashes['F1.dll']).Count){throw 'Populated screenshots differ from delivery'}
if(@($shots|Where-Object AssemblySha256 -ne $hashes['F1.dll']).Count){throw 'Screenshot assembly differs from delivery'}
$imageHashes=[Collections.Generic.List[object]]::new()
foreach($set in @(@{Directory="$root/screenshots";Shots=$shots},@{Directory="$root/screenshots-populated";Shots=$populated})){
 foreach($shot in $set.Shots){$path=Join-Path $set.Directory $shot.File;$imageHashes.Add([pscustomobject]@{File=$path;Sha256=(Get-FileHash $path).Hash})}
}
foreach($language in @('ar','en')){foreach($theme in @('Dark','Light','System')){foreach($page in @('Dashboard','SystemInfo','Performance','MemoryTools','Cleaner','Uninstaller','Startup','Tools','Settings','About')){
 if(@($shots|Where-Object File -eq "$language-$theme-$page.png").Count -ne 1){throw 'Incomplete screenshot page/theme/language matrix'}
}}}
if(@($shots|Where-Object File -like '*-minimum.png').Count -ne 10 -or $populated.Count -ne 36){throw 'Supplementary screenshot matrix incomplete'}
$imageHashes|ConvertTo-Json -Depth 3|Set-Content "$root/test-results/image-hashes.json" -Encoding utf8
$comparison=Get-Content "$root/comparisons/method.json" -Raw|ConvertFrom-Json
if((Get-FileHash $comparison.Runtime).Hash -ne $comparison.RuntimeSha256 -or (Get-FileHash docs/design/reference/f1-approved-reference.png).Hash -ne $comparison.ReferenceSha256){throw 'Comparison inputs differ from current images'}
foreach($name in @('side-by-side.png','overlay-50-percent.png','difference.png','contact-ar-Dark.png','contact-en-Light.png','contact-ar-Dark-populated.png','contact-en-Light-populated.png')){if(!(Test-Path "$root/comparisons/$name")){throw "Missing comparison $name"}}
$build='artifacts/f1-ui/test-results/build-06'
foreach($file in @('restore.txt','Debug.txt','Release.txt','publish-app.txt','publish-helper.txt')){if(!(Test-Path "$build/$file")){throw 'Missing build evidence'}}
foreach($file in @('Debug.txt','Release.txt')){$text=[IO.File]::ReadAllText((Resolve-Path "$build/$file"));if($text -notmatch 'Build succeeded' -or $text -notmatch '0 Warning\(s\)' -or $text -notmatch '0 Error\(s\)'){throw 'Build result is not clean'}}
$summary=[ordered]@{
  Task='F1 UI rebrand';Version='0.9.0-dev';Generated=[DateTimeOffset]::UtcNow;Publication='artifacts/f1-ui/publish/F1.exe';
 Gates=@{Build='PASS';RuntimeAndRegression='PASS';BrandingAndDataCompatibility='PASS (development scope)';AllPagesUiCoverage='PASS';VisualReference='PENDING';ReleaseProduction='UNCHANGED - not approved by UI task';OwnerReview='PENDING'};
  Suites=$suites;TotalChecks=($suites.Values|Measure-Object -Sum).Sum;AdditionalFuzzInputs=1000;Hashes=$hashes;
  EvidenceLineage=@{FinalBuild=$build;FinalProductUi=$UiEvidence;FinalScreenshots='artifacts/f1-ui/screenshots';FinalPopulatedScreenshots='artifacts/f1-ui/screenshots-populated';FinalPassiveRuntime=$StabilityEvidence;EarlierF1PageAndFixtureRegression='build-02 / test-results/pages / test-results/fixtures';LaterProductChange='Only About/Settings version presentation and its supporting label/value properties; final product UX, focused Arabic probe and full screenshot matrix rerun. Core/Infrastructure hashes match the tested engines.'};
  MemoryImageReframing='artifacts/f1-ui/test-results/memory-framing; eight images recaptured after a scroll-framing review, same delivery assembly; original attempts retained under iterations';
 Runtime=$runtime;BaselineRuntime=$baseline;Before=(Metrics "$root/baseline/stability");After=(Metrics $StabilityEvidence);
 Counters=@{Before=(RuntimeCounters "$root/baseline/stability");After=(RuntimeCounters $StabilityEvidence)};
 HelperParentVerification=$helper;DiagnosticExport=$export;
 SourceReview='artifacts/f1-ui/test-results/source-change-review.json';
 Screenshots=@{Count=@($shots).Count;Metadata='artifacts/f1-ui/screenshots/metadata.json';PopulatedCount=@($populated).Count;PopulatedMetadata='artifacts/f1-ui/screenshots-populated/metadata.json';Dpi=@($shots.Dpi|Sort-Object -Unique);Hashes='artifacts/f1-ui/test-results/image-hashes.json';Reference='ar-Dark-Dashboard-reference.png';Comparison='artifacts/f1-ui/comparisons/side-by-side.png'};
 Asset=@{CleanBackground='MISSING';TemporaryBackground='Owner-authorized original local mountain SVG';FlattenedReferenceShipped=$false};
 Limits=@('Only current Windows 11 Pro x64 host and limited Explorer desktop token; not an independent Standard User account','125/150/200 percent DPI, full Narrator/high-contrast/edition matrix remain unverified','Live positive UAC/alternate credentials, signing/protected install, installer/clean VMs remain Release QA','Short matched six-cycle development comparison; not universal leak-free certification','Normal close observed; null ExitCode is not verified zero','Photographic mountain source missing; original temporary illustration and functional copy/icon/material differences are disclosed; no similarity percentage asserted')
}
$summary|ConvertTo-Json -Depth 10|Set-Content "$root/summary.json" -Encoding utf8
