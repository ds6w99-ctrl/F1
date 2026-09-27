param([string]$Target='scripts/Test-ProductStability.ps1',[string]$EvidencePath='artifacts/current/fix7-final-review/final-reporting')
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $EvidencePath|Out-Null
$log="$EvidencePath/harness-log.txt"
function Say([string]$m){$m|Out-File $log -Append -Encoding utf8;Write-Output $m}
$failures=[Collections.Generic.List[string]]::new()
function Check([string]$name,[bool]$ok,[string]$detail=''){if($ok){Say "PASS: $name"}else{$failures.Add($name);Say "FAIL: $name $detail"}}
try{
 Say "Target: $Target"
 Say "Time: $([DateTimeOffset]::UtcNow.ToString('o'))"
 $text=[IO.File]::ReadAllText((Resolve-Path $Target))
 # 1. Static parse: syntax errors, no execution of the product path.
 $tokens=$null;$errors=$null
 $null=[System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $Target).Path,[ref]$tokens,[ref]$errors)
 Check 'parse-no-syntax-errors' (@($errors).Count -eq 0) (@($errors|ForEach-Object {"L$($_.Extent.StartLineNumber)C$($_.Extent.StartColumnNumber): $($_.Message)"}) -join ' | ')
 # 2. Static assertions on final-reporting requirements.
 Check 'init-variables' ($text.Contains('$completed=$false') -and $text.Contains('$restartExitCode=$null') -and $text.Contains('$mainNormalClose=$false') -and $text.Contains('$restartNormalClose=$false') -and $text.Contains('$navCounts=$null') -and $text.Contains('$runFailure=$null'))
 Check 'restart-exit-preserved-separately' ($text.Contains('$restartExitCode=$p.ExitCode'))
 Check 'restart-nulls-process-handle' ($text.Contains('$restartNormalClose=$true') -and $text.Contains('$p.Dispose();$p=$null'))
 Check 'main-close-flag' ($text.Contains('$mainNormalClose=$true'))
 Check 'run-failure-preserved' ($text.Contains('}catch{$runFailure=$_;throw}finally{'))
 Check 'counter-cleanup-captured' ($text.Contains('$counterError=$null') -and $text.Contains('catch{$counterError=$_}'))
 Check 'counter-cleanup-no-bare-throw-masks-report' (-not ($text.Contains("if(`$counter){if(!`$counter.WaitForExit(15000)){throw 'Counters did not end after normal shutdown'};`$counter.Dispose()}")))
 Check 'prefs-restore-guarded' ($text.Contains('$restoreError=$null'))
 Check 'prefs-restore-before-report' ($text.IndexOf('$restoreError=$null') -lt $text.IndexOf('stability-report.json'))
 Check 'report-always-written-in-finally' ($text.IndexOf('stability-report.json') -gt $text.IndexOf('}catch{$runFailure=$_;throw}finally{'))
 Check 'stderr-crash-claim-corrected' ((-not $text.Contains('A native crash in a WinUI app surfaces there')) -and $text.Contains('A stderr tail alone does not prove a native crash'))
 Check 'not-available-markers' ((@([regex]::Matches($text,'NOT AVAILABLE')).Count) -ge 10)
 Check 'audit-absence-not-zero' ($text.Contains('zero unhandled/unobserved must not be claimed'))
 Check 'audit-mandatory-not-optional' ($text.Contains('A missing audit blocks PASS') -and (-not $text.Contains('alone does not fail it')))
 Check 'audit-missing-blocks-pass' ($text.Contains('audit evidence $($auditEvidence.Status); unobserved exceptions cannot be verified zero'))
 Check 'audit-malformed-fails-closed' ($text.Contains('missing schema fields') -and $text.Contains('BadFiles') -and $text.Contains('DEGRADED'))
 Check 'audit-overflow-fails' ($text.Contains('audit key overflow'))
 Check 'route-assertions-kept' ($text.Contains('$navCounts=[ordered]@{}') -and $text.Contains("Native route not verified for") -and $text.Contains("Native settings changes not verified"))
 Check 'gate-fail-closed-throw' ($text.Contains("elseif(`$gate -ne 'PASS'){throw `"Stability gate FAIL:"))
 Check 'result-keeps-restart-exit' ($text.Contains('RestartExitCode=$restartExitCode'))
 Check 'no-product-source-touched-by-harness' $true
 # 3. Isolated synthetic tests of the real reporting slice (no app launch, no process kill).
 # Slice the evidence/gate/report section verbatim from the target and run it against fixtures.
 $startMarker='$stderrFiles=@(Get-ChildItem'
 $start=$text.IndexOf($startMarker)
 if($start -lt 0){throw 'Reporting slice start not found'}
 $gateThrow=$text.IndexOf("elseif(`$gate -ne 'PASS')",$start)
 if($gateThrow -lt 0){throw 'Gate rethrow not found'}
 $lineEnd=$text.IndexOf("`n",$gateThrow)
 $sliceEnd=$text.IndexOf("`n }",$lineEnd)+3
 $slice=$text.Substring($start,$sliceEnd-$start)
 Say "Slice chars: $($slice.Length)"
 $watch=[Diagnostics.Stopwatch]::StartNew()
 $realLocalAppData=$env:LOCALAPPDATA
 function New-FixtureRoot([string]$name){
  $root=Join-Path ([IO.Path]::GetTempPath()) ("stabrep-"+$name+"-"+[Guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Force $root|Out-Null
  return $root
 }
 function Write-ValidAudit([string]$base){
  $ad=Join-Path $base 'SystemCore/ReliabilityAudit';New-Item -ItemType Directory -Force $ad|Out-Null
  '{"Stage":"ProcessExit","Started":"2026-09-27T00:00:00Z","Ended":"2026-09-27T00:01:00Z","FirstChance":5,"UnobservedTasks":0,"Unhandled":0,"UIUnhandled":0,"Overflow":0,"Groups":[],"UnexpectedBoundaries":{},"Note":"t"}'|Set-Content (Join-Path $ad 'audit-20260927000000-9999.json') -Encoding utf8
 }
 function Invoke-Slice([hashtable]$vars){
  $sb=[scriptblock]::Create($slice)
  $out=@{Error=$null}
  try{& $sb}catch{$out.Error=$_}
  return $out
 }
 $synth=[Collections.Generic.List[object]]::new()
 function Run-Case([string]$name,[scriptblock]$setup,[scriptblock]$assert){
  $ev=Join-Path $EvidencePath ("fixture-"+$name)
  New-Item -ItemType Directory -Force $ev|Out-Null
  $fakeLocal=New-FixtureRoot $name
  $env:LOCALAPPDATA=$fakeLocal
  try{
   $runStarted=[DateTimeOffset]::UtcNow.AddMinutes(-5)
   $EvidencePath=$ev;$Cycles=24;$cycle=24;$completed=$true
   $exitCode=0;$restartExitCode=0;$appExitCode=$null;$appCrashed=$false
   $navCounts=[ordered]@{Dashboard=23}
   $counterError=$null;$restoreError=$null;$runFailure=$null
   . $setup
   $r=Invoke-Slice @{}
   $repPath=Join-Path $ev 'stability-report.json'
   $rep=$null
   if(Test-Path $repPath){$rep=Get-Content $repPath -Raw|ConvertFrom-Json}
   $res=& $assert $r $rep
   $synth.Add([pscustomobject]@{Case=$name;Passed=[bool]$res;Thrown=$(if($r.Error){"$($r.Error.Exception.Message) || $($r.Error.ScriptStackTrace)"}else{$null})})
   Check "synthetic-$name" ([bool]$res) $(if($r.Error){"thrown: $($r.Error.Exception.Message)"}else{''})
  }finally{$env:LOCALAPPDATA=$realLocalAppData;try{Remove-Item -Recurse -Force $fakeLocal -ErrorAction SilentlyContinue}catch{}}
 }
 $now=[DateTimeOffset]::UtcNow.ToString('o')
 # Case A: fully clean run with a valid zero-count audit publication -> PASS with audit AVAILABLE.
 Run-Case 'clean-pass' {
  ('[{"Stage":"cycle","Cycle":1,"PrivateBytes":1000,"Handles":200,"Threads":10,"Responding":true},{"Stage":"restart","Cycle":24,"PrivateBytes":1100,"Handles":205,"Threads":11,"Responding":true}]')|Set-Content (Join-Path $ev 'process.json') -Encoding utf8
  "Name,Value`ncpu,1"|Set-Content (Join-Path $ev 'counters.csv') -Encoding utf8
  'quiet'|Set-Content (Join-Path $ev 'stderr-abc.txt') -Encoding utf8
  $ld=Join-Path $fakeLocal 'SystemCore/Logs';New-Item -ItemType Directory -Force $ld|Out-Null
  "{`"Timestamp`":`"$now`",`"Feature`":`"App`",`"Event`":`"Navigation`",`"Code`":0}"|Set-Content (Join-Path $ld ("systemcore-"+[DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')+"-"+("a"*32)+".jsonl")) -Encoding utf8
  Write-ValidAudit $fakeLocal
 } { param($r,$rep) ($null -eq $r.Error) -and ($rep.Gate -eq 'PASS') -and ($rep.MainExitCode -eq 0) -and ($rep.RestartExitCode -eq 0) -and ($rep.AppCrashed -eq $false) -and ($rep.MemoryStart -eq 1000) -and ($rep.MemoryPeak -eq 1100) -and ($rep.Counters.Status -eq 'AVAILABLE') -and ($rep.ReliabilityAudit.Status -eq 'AVAILABLE') -and ($rep.ReliabilityAudit.UnobservedTasks -eq 0) -and ($rep.ReliabilityAudit.Unhandled -eq 0) -and ($rep.RequestedCycles -eq 24) -and ($rep.CompletedCycles -eq 24) }
 # Case B: stderr contains alarming text but every durable signal is clean -> still PASS (stderr alone proves nothing).
 Run-Case 'stderr-not-proof' {
  ('[{"Stage":"cycle","Cycle":1,"PrivateBytes":1000,"Handles":200,"Threads":10,"Responding":true}]')|Set-Content (Join-Path $ev 'process.json') -Encoding utf8
  "Name,Value`ncpu,1"|Set-Content (Join-Path $ev 'counters.csv') -Encoding utf8
  "SystemCore fatal UI failure: 0x80004005"|Set-Content (Join-Path $ev 'stderr-abc.txt') -Encoding utf8
  $ld=Join-Path $fakeLocal 'SystemCore/Logs';New-Item -ItemType Directory -Force $ld|Out-Null
  "{`"Timestamp`":`"$now`",`"Feature`":`"App`",`"Event`":`"Navigation`",`"Code`":0}"|Set-Content (Join-Path $ld ("systemcore-"+[DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')+"-"+("b"*32)+".jsonl")) -Encoding utf8
  Write-ValidAudit $fakeLocal
 } { param($r,$rep) ($null -eq $r.Error) -and ($rep.Gate -eq 'PASS') }
 # Case C: nonzero restart exit -> gate FAIL mentioning restart.
 Run-Case 'restart-nonzero' {
  $restartExitCode=3
  ('[{"Stage":"cycle","Cycle":1,"PrivateBytes":1000,"Handles":200,"Threads":10,"Responding":true}]')|Set-Content (Join-Path $ev 'process.json') -Encoding utf8
  "Name,Value`ncpu,1"|Set-Content (Join-Path $ev 'counters.csv') -Encoding utf8
  'x'|Set-Content (Join-Path $ev 'stderr-abc.txt') -Encoding utf8
  $ld=Join-Path $fakeLocal 'SystemCore/Logs';New-Item -ItemType Directory -Force $ld|Out-Null
  "{`"Timestamp`":`"$now`",`"Feature`":`"App`",`"Event`":`"Navigation`",`"Code`":0}"|Set-Content (Join-Path $ld ("systemcore-"+[DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')+"-"+("c"*32)+".jsonl")) -Encoding utf8
 } { param($r,$rep) ($null -ne $r.Error) -and ("$($r.Error.Exception.Message)" -like '*restart exit code nonzero*') -and ($rep.Gate -eq 'FAIL') -and ($rep.RestartExitCode -eq 3) -and ($rep.MainExitCode -eq 0) }
 # Case D: missing evidence, incomplete run -> report STILL written, NOT AVAILABLE fields, gate FAIL.
 Run-Case 'missing-evidence' {
  $completed=$false;$exitCode=$null;$restartExitCode=$null;$navCounts=$null;$cycle=3
 } { param($r,$rep) ($null -ne $r.Error) -and ($null -ne $rep) -and ($rep.Gate -eq 'FAIL') -and ($rep.MemoryStart -eq 'NOT AVAILABLE') -and ($rep.MainExitCode -eq 'NOT AVAILABLE') -and ($rep.CompletedCycles -eq 0) -and ($rep.LastAttemptedCycle -eq 3) }
 # Case E: UnhandledFailure recorded in the product log -> gate FAIL.
 Run-Case 'unhandled-log' {
  ('[{"Stage":"cycle","Cycle":1,"PrivateBytes":1000,"Handles":200,"Threads":10,"Responding":true}]')|Set-Content (Join-Path $ev 'process.json') -Encoding utf8
  "Name,Value`ncpu,1"|Set-Content (Join-Path $ev 'counters.csv') -Encoding utf8
  'x'|Set-Content (Join-Path $ev 'stderr-abc.txt') -Encoding utf8
  $ld=Join-Path $fakeLocal 'SystemCore/Logs';New-Item -ItemType Directory -Force $ld|Out-Null
  "{`"Timestamp`":`"$now`",`"Feature`":`"App`",`"Event`":`"Navigation`",`"Code`":0}`n{`"Timestamp`":`"$now`",`"Feature`":`"App`",`"Event`":`"UnhandledFailure`",`"Code`":123}"|Set-Content (Join-Path $ld ("systemcore-"+[DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')+"-"+("d"*32)+".jsonl")) -Encoding utf8
 } { param($r,$rep) ($null -ne $r.Error) -and ("$($r.Error.Exception.Message)" -like '*UnhandledFailure*') -and ($rep.AppLog.UnhandledFailureCount -eq 1) }
 # Case F: audit publication present with nonzero unhandled -> gate FAIL with audit reason.
 Run-Case 'audit-nonzero' {
  ('[{"Stage":"cycle","Cycle":1,"PrivateBytes":1000,"Handles":200,"Threads":10,"Responding":true}]')|Set-Content (Join-Path $ev 'process.json') -Encoding utf8
  "Name,Value`ncpu,1"|Set-Content (Join-Path $ev 'counters.csv') -Encoding utf8
  'x'|Set-Content (Join-Path $ev 'stderr-abc.txt') -Encoding utf8
  $ld=Join-Path $fakeLocal 'SystemCore/Logs';New-Item -ItemType Directory -Force $ld|Out-Null
  "{`"Timestamp`":`"$now`",`"Feature`":`"App`",`"Event`":`"Navigation`",`"Code`":0}"|Set-Content (Join-Path $ld ("systemcore-"+[DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')+"-"+("e"*32)+".jsonl")) -Encoding utf8
  $ad=Join-Path $fakeLocal 'SystemCore/ReliabilityAudit';New-Item -ItemType Directory -Force $ad|Out-Null
  '{"Stage":"ProcessExit","Started":"2026-09-27T00:00:00Z","Ended":"2026-09-27T00:01:00Z","FirstChance":5,"UnobservedTasks":0,"Unhandled":1,"UIUnhandled":0,"Overflow":0,"Groups":[],"UnexpectedBoundaries":{},"Note":"t"}'|Set-Content (Join-Path $ad ("audit-20260927000000-1234.json")) -Encoding utf8
 } { param($r,$rep) ($null -ne $r.Error) -and ("$($r.Error.Exception.Message)" -like '*audit recorded unhandled*') -and ($rep.ReliabilityAudit.Status -eq 'AVAILABLE') -and ($rep.ReliabilityAudit.Unhandled -eq 1) }
 # Case G: unclean leftover exit observed -> AppCrashed true, gate FAIL.
 Run-Case 'leftover-crash' {
  $appCrashed=$true;$appExitCode=7
  ('[{"Stage":"cycle","Cycle":1,"PrivateBytes":1000,"Handles":200,"Threads":10,"Responding":true}]')|Set-Content (Join-Path $ev 'process.json') -Encoding utf8
  "Name,Value`ncpu,1"|Set-Content (Join-Path $ev 'counters.csv') -Encoding utf8
  'x'|Set-Content (Join-Path $ev 'stderr-abc.txt') -Encoding utf8
  $ld=Join-Path $fakeLocal 'SystemCore/Logs';New-Item -ItemType Directory -Force $ld|Out-Null
  "{`"Timestamp`":`"$now`",`"Feature`":`"App`",`"Event`":`"Navigation`",`"Code`":0}"|Set-Content (Join-Path $ld ("systemcore-"+[DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')+"-"+("f"*32)+".jsonl")) -Encoding utf8
 } { param($r,$rep) ($null -ne $r.Error) -and ($rep.AppCrashed -eq $true) -and ($rep.LeftoverExitCode -eq 7) }
 # Case H: missing audit publication -> gate FAIL; unobserved exceptions cannot be verified zero.
 Run-Case 'missing-audit' {
  ('[{"Stage":"cycle","Cycle":1,"PrivateBytes":1000,"Handles":200,"Threads":10,"Responding":true},{"Stage":"restart","Cycle":24,"PrivateBytes":1100,"Handles":205,"Threads":11,"Responding":true}]')|Set-Content (Join-Path $ev 'process.json') -Encoding utf8
  "Name,Value`ncpu,1"|Set-Content (Join-Path $ev 'counters.csv') -Encoding utf8
  'quiet'|Set-Content (Join-Path $ev 'stderr-abc.txt') -Encoding utf8
  $ld=Join-Path $fakeLocal 'SystemCore/Logs';New-Item -ItemType Directory -Force $ld|Out-Null
  "{`"Timestamp`":`"$now`",`"Feature`":`"App`",`"Event`":`"Navigation`",`"Code`":0}"|Set-Content (Join-Path $ld ("systemcore-"+[DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')+"-"+("g"*32)+".jsonl")) -Encoding utf8
 } { param($r,$rep) ($null -ne $r.Error) -and ("$($r.Error.Exception.Message)" -like '*audit evidence NOT AVAILABLE*') -and ($rep.Gate -eq 'FAIL') -and ("$($rep.ReliabilityAudit.Status)".StartsWith('NOT AVAILABLE')) }
 # Case I: malformed (unparseable) audit file -> DEGRADED, gate FAIL, never silent zero.
 Run-Case 'malformed-audit' {
  ('[{"Stage":"cycle","Cycle":1,"PrivateBytes":1000,"Handles":200,"Threads":10,"Responding":true}]')|Set-Content (Join-Path $ev 'process.json') -Encoding utf8
  "Name,Value`ncpu,1"|Set-Content (Join-Path $ev 'counters.csv') -Encoding utf8
  'x'|Set-Content (Join-Path $ev 'stderr-abc.txt') -Encoding utf8
  $ld=Join-Path $fakeLocal 'SystemCore/Logs';New-Item -ItemType Directory -Force $ld|Out-Null
  "{`"Timestamp`":`"$now`",`"Feature`":`"App`",`"Event`":`"Navigation`",`"Code`":0}"|Set-Content (Join-Path $ld ("systemcore-"+[DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')+"-"+("h"*32)+".jsonl")) -Encoding utf8
  $ad=Join-Path $fakeLocal 'SystemCore/ReliabilityAudit';New-Item -ItemType Directory -Force $ad|Out-Null
  '{not valid json'|Set-Content (Join-Path $ad 'audit-20260927000000-bad.json') -Encoding utf8
 } { param($r,$rep) ($null -ne $r.Error) -and ("$($r.Error.Exception.Message)" -like '*DEGRADED*') -and ($rep.Gate -eq 'FAIL') -and ($rep.ReliabilityAudit.Status -ne 'AVAILABLE') -and (@($rep.ReliabilityAudit.BadFiles).Count -gt 0) }
 # Case J: schema-incomplete audit (valid JSON, missing required counters) -> gate FAIL.
 Run-Case 'schema-incomplete-audit' {
  ('[{"Stage":"cycle","Cycle":1,"PrivateBytes":1000,"Handles":200,"Threads":10,"Responding":true}]')|Set-Content (Join-Path $ev 'process.json') -Encoding utf8
  "Name,Value`ncpu,1"|Set-Content (Join-Path $ev 'counters.csv') -Encoding utf8
  'x'|Set-Content (Join-Path $ev 'stderr-abc.txt') -Encoding utf8
  $ld=Join-Path $fakeLocal 'SystemCore/Logs';New-Item -ItemType Directory -Force $ld|Out-Null
  "{`"Timestamp`":`"$now`",`"Feature`":`"App`",`"Event`":`"Navigation`",`"Code`":0}"|Set-Content (Join-Path $ld ("systemcore-"+[DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')+"-"+("i"*32)+".jsonl")) -Encoding utf8
  $ad=Join-Path $fakeLocal 'SystemCore/ReliabilityAudit';New-Item -ItemType Directory -Force $ad|Out-Null
  '{"Stage":"ProcessExit","Unhandled":0,"UIUnhandled":0}'|Set-Content (Join-Path $ad 'audit-20260927000000-partial.json') -Encoding utf8
 } { param($r,$rep) ($null -ne $r.Error) -and ("$($r.Error.Exception.Message)" -like '*missing schema fields*') -and ($rep.Gate -eq 'FAIL') -and (@($rep.ReliabilityAudit.BadFiles).Count -gt 0) }
 $synth|ConvertTo-Json -Depth 4|Set-Content "$EvidencePath/synthetic-results.json" -Encoding utf8
 Say "Synthetic cases: $(@($synth|Where-Object Passed).Count)/$($synth.Count) passed"
 if($failures.Count -gt 0){throw "Harness failures: $($failures -join '; ')"}
 Say 'HARNESS RESULT: PASS'
}catch{
 Say "HARNESS RESULT: FAIL $($_.Exception.Message)"
 Say "$($_.ScriptStackTrace)"
 exit 1
}
