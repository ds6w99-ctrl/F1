$ErrorActionPreference='Stop'
$evidence='artifacts/phase9/9c'
foreach($phase in @('9a','9b')){
 $gate=Get-Content "artifacts/phase9/$phase/gate.json" -Raw|ConvertFrom-Json
 if(($phase -eq '9a' -and $gate.ReliabilityGate -ne 'PASS') -or ($phase -eq '9b' -and $gate.SecurityGate -ne 'PASS')){throw 'Sequential prerequisite missing'}
}
$auditRuntime=Get-Content "$evidence/audit-runtime/result.json" -Raw|ConvertFrom-Json
if(!$auditRuntime.Completed -or !$auditRuntime.NormalClose -or !$auditRuntime.RestartNormalClose){throw 'Final exception audit did not complete'}
$auditFiles=@(Get-ChildItem "$evidence/audit-runtime" -Filter 'audit-*.json')
if($auditFiles.Count -ne 2){throw 'Expected two final audit lifetimes'}
$audits=@();$groups=@()
foreach($file in $auditFiles){
 $audit=Get-Content $file.FullName -Raw|ConvertFrom-Json
 if($audit.Stage -ne 'ProcessExit' -or $audit.Overflow -ne 0 -or $audit.Unhandled -ne 0 -or $audit.UIUnhandled -ne 0 -or $audit.UnobservedTasks -ne 0 -or @($audit.UnexpectedBoundaries.PSObject.Properties).Count){throw 'Final audit failure requires review'}
 foreach($group in $audit.Groups){
  $expected=($group.Type -eq 'System.Runtime.InteropServices.COMException' -and $group.HResult -eq -2147023728 -and $group.Frames.Contains('AccessibilitySettings.add_HighContrastChanged') -and $group.Count -eq 1) -or
   ($group.Type -eq 'System.Threading.Tasks.TaskCanceledException' -and $group.HResult -eq -2146233029 -and $group.Frames.Contains('PerformanceMonitorService'))
  if(!$expected){throw "Unclassified final audit: $($group.Type), $($group.Count)"}
  $groups+=$group
 }
 $audits+=@{File=$file.Name;FirstChance=$audit.FirstChance;Unhandled=$audit.Unhandled;UIUnhandled=$audit.UIUnhandled;UnobservedTasks=$audit.UnobservedTasks;UnexpectedBoundaries=0}
}
$app= [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Resolve-Path artifacts/phase9/app/SystemCore.App.dll)))
if($app.Contains('ReliabilityAudit') -or $app.Contains('MemoryValidationEnvironment')){throw 'Ordinary publication contains validation instrumentation'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead((Resolve-Path "$evidence/ui/diagnostic-export.zip"))
try{
 $names=@($zip.Entries.FullName|Sort-Object)
 if(($names -join ',') -ne 'logs/recent.jsonl,manifest.json'){throw 'Unexpected ZIP entries'}
 $records=0;$text=''
 foreach($entry in $zip.Entries){
  $reader=[IO.StreamReader]::new($entry.Open());try{$content=$reader.ReadToEnd()}finally{$reader.Dispose()};$text+=$content
  if($entry.FullName -eq 'manifest.json'){$manifest=$content|ConvertFrom-Json;if($manifest.AppVersion -ne '0.9.0-dev' -or $manifest.Architecture -ne 'X64'){throw 'Wrong exported metadata'}}
  else{foreach($line in $content.Split("`n")|Where-Object {$_ -ne ''}){$record=$line|ConvertFrom-Json;if(@($record.PSObject.Properties.Name|Where-Object {$_ -notin @('Timestamp','Feature','Event','Code')}).Count){throw 'Non-allowlisted export field'};$records++}}
 }
 foreach($value in @($env:USERNAME,$env:USERPROFILE,[Environment]::MachineName)) {if($value -and $text.IndexOf($value,[StringComparison]::OrdinalIgnoreCase) -ge 0){throw 'Private identifier in export'}}
}finally{$zip.Dispose()}
@{Result='PASS';Entries=$names;Records=$records;Version=$manifest.AppVersion;Architecture=$manifest.Architecture;Uploaded=$false;IdentifierCheck='Current username/profile/machine and exact record-field allowlist'}|ConvertTo-Json|Set-Content "$evidence/export-review.json" -Encoding utf8
# Reuse the accepted build/hash/UI/native metric checks, redirecting every output
# before execution so no prior-phase evidence is modified.
$source=[IO.File]::ReadAllText((Resolve-Path scripts/Write-Phase8Summary.ps1))
$source=$source.Replace('artifacts/phase8/completion','artifacts/phase9/9c').Replace('artifacts/phase8/app','artifacts/phase9/app').Replace('artifacts/phase8/summary.json','artifacts/phase9/summary.json').Replace('Phase=8','Phase=9')
& ([scriptblock]::Create($source)) -EvidencePath $evidence
$summary=Get-Content artifacts/phase9/summary.json -Raw|ConvertFrom-Json
$summary.PSObject.Properties.Remove('Phase9')
$summary|Add-Member -NotePropertyName SequentialGates -NotePropertyValue @{Reliability9A='PASS';Security9B='PASS';Regression9C='PASS'}
$summary|Add-Member -NotePropertyName FinalExceptionAudit -NotePropertyValue @{Lifetimes=$audits;Groups=$groups;HandledUnexpected=0;Unhandled=0;UnobservedTask=0;Method='Separate audit publication, same Core/Infrastructure; main stability is uninstrumented';Runtime=$auditRuntime}
$summary|Add-Member -NotePropertyName Version -NotePropertyValue '0.9.0-dev'
foreach($assembly in @('Core','Infrastructure')){
 foreach($path in @("$evidence/audit-app/SystemCore.$assembly.dll","tests/SystemCore.SecurityTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.$assembly.dll","tests/SystemCore.StartupTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.$assembly.dll")){
  if((Get-FileHash $path).Hash -ne $summary.EngineHashes.$assembly){throw 'Final security/recovery/audit engine mismatch'}
 }
}
$points=Get-Content "$evidence/stability/process.json" -Raw|ConvertFrom-Json
$cycles=@($points|Where-Object Stage -eq 'cycle');$late=@($cycles|Select-Object -Skip 13)
$active=@($points|Where-Object Stage -ne 'restart');$first=$active[0];$last=$active[-1]
$summary|Add-Member -NotePropertyName RuntimeInterpretation -NotePropertyValue @{LateCycles='14-24';LatePrivateBytesMin=($late.PrivateBytes|Measure-Object -Minimum).Minimum;LatePrivateBytesMax=($late.PrivateBytes|Measure-Object -Maximum).Maximum;NormalizedCpuAverage=100*($last.CpuSeconds-$first.CpuSeconds)/($last.Seconds-$first.Seconds)/[Environment]::ProcessorCount;Conclusion='Observed warm-up then bounded later cycles, declining cooldown; handles/threads decrease. Limited development workload, not general leak-free certification.'}
$summary|Add-Member -NotePropertyName TotalChecks -NotePropertyValue (($summary.Suites.PSObject.Properties.Value|Measure-Object -Sum).Sum)
$summary|Add-Member -NotePropertyName AdditionalFuzzInputs -NotePropertyValue 1000
$summary.GuiRuntimeCounters.ExceptionNote='Runtime interval throw counter only; source-attributed six-cycle audit is separate and does not retrospectively identify each uninstrumented throw.'
$summary.Limitations=@('Limited desktop token, not a separate Standard User account','Live UAC accept/deny/cancel/alternate-admin matrix deferred to Release QA','Main runtime throw counter and separate method-only audit have different lifetimes; zero observed unhandled/unobserved is not a universal guarantee','Hard termination can leave a bounded pending export; no successful ZIP reported before atomic commit','Observed plateau applies only to this development workload, not a general leak-free assertion','ExitCode may be null; normal close is not proof of zero exit status','Full edition/account/Narrator/DPI matrix, signing, installer, clean VM and long production soak remain Release QA')
$summary|ConvertTo-Json -Depth 14|Set-Content artifacts/phase9/summary.json -Encoding utf8
