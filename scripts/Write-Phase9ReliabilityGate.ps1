$ErrorActionPreference='Stop'
$root='artifacts/phase9/9a'
$regression=Get-Content "$root/regression/result.json" -Raw|ConvertFrom-Json
$runtime=Get-Content "$root/verified-runtime/result.json" -Raw|ConvertFrom-Json
if($regression.Result -ne 'PASS' -or !$runtime.Completed -or !$runtime.NormalClose -or !$runtime.RestartNormalClose){throw '9A required evidence missing or failed'}
foreach($configuration in @('Debug','Release')){
 if((Get-Content "$root/build/$configuration.txt" -Raw) -notmatch '0 Warning\(s\)\s+0 Error\(s\)'){throw '9A build review required'}
}
$suites=[ordered]@{}
foreach($file in Get-ChildItem "$root/regression" -Recurse -Filter '*.txt'){
 $lines=Get-Content $file.FullName
 if($lines -match '^FAIL'){throw "Failed regression $($file.Name)"}
 $suites[$file.BaseName]=@($lines|Where-Object {$_ -match '^PASS'}).Count
}
foreach($folder in @('ui','memory-ui')){
 $checks=Get-Content "$root/$folder/checks.json" -Raw|ConvertFrom-Json
 if(!$checks -or @($checks|Where-Object {!$_.Passed}).Count){throw "$folder failed"}
 $suites[$folder]=$checks.Count
}
$early=Get-Content "$root/early-close/result.json" -Raw|ConvertFrom-Json
if(@($early).Count -ne 3 -or @($early|Where-Object {!$_.NormalClose}).Count){throw 'Early-close verification incomplete'}
$auditFiles=@(Get-ChildItem "$root/verified-runtime" -Filter 'audit-*.json')+@(Get-ChildItem "$root/early-close" -Filter 'audit-*.json')
if($auditFiles.Count -ne 5){throw 'Expected main/restart plus three early-close audit lifetimes'}
$lifetimes=@();$inventory=@()
foreach($file in $auditFiles){
 $audit=Get-Content $file.FullName -Raw|ConvertFrom-Json
 if($audit.Stage -ne 'ProcessExit' -or $audit.Overflow -ne 0 -or $audit.Unhandled -ne 0 -or $audit.UIUnhandled -ne 0 -or $audit.UnobservedTasks -ne 0 -or @($audit.UnexpectedBoundaries.PSObject.Properties).Count -ne 0){throw "Reliability event requires investigation: $($file.Name)"}
 foreach($group in $audit.Groups){
  $classification=if($group.Type -eq 'System.Runtime.InteropServices.COMException' -and $group.HResult -eq -2147023728 -and $group.Frames.Contains('AccessibilitySettings.add_HighContrastChanged')){'Expected capability failure; exact HRESULT handler; once per window'}
  elseif($group.Type -in @('System.OperationCanceledException','System.Threading.Tasks.TaskCanceledException') -and $group.Frames -match 'SystemCore\.(Core\.Performance\.PerformanceMonitorService|Infrastructure\.(Startup|SystemInformation)|App\.ViewModels\.DashboardViewModel|Core\.SystemInformation)'){'Expected shutdown cancellation; owned token/catch reviewed'}
  elseif($file.Directory.Name -eq 'early-close' -and $group.Type -eq 'System.OperationCanceledException' -and $group.HResult -eq -2146233029 -and $group.Count -eq 1 -and $group.Frames -eq "System.Runtime.EH.DispatchEx`nSystem.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw`nSystem.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop`nSystem.Threading.Tasks.Task.ExecuteWithThreadLocal`nSystem.Threading.ThreadPoolWorkQueue.Dispatch`nSystem.Threading.PortableThreadPool+WorkerThread.WorkerThreadStart`nSystem.Threading.Thread.StartCallback"){'Expected cancellation rethrow while ExecutionContext restores thread-pool context; runtime source reviewed; individual object identity not correlated'}
  else{throw "Unclassified exception group in $($file.Name): $($group.Type)"}
  $inventory+=[pscustomobject]@{Lifetime=$file.Name;Type=$group.Type;Count=$group.Count;HResult=$group.HResult;Frames=$group.Frames;Classification=$classification;Handled='Source catch-site review plus zero terminal callbacks; first-chance alone is not catch evidence';Impact='Unavailable optional subscription or normal shutdown cancellation'}
 }
 $lifetimes+=[pscustomobject]@{File=$file.Name;FirstChance=$audit.FirstChance;Unhandled=$audit.Unhandled;UIUnhandled=$audit.UIUnhandled;UnobservedTasks=$audit.UnobservedTasks;UnexpectedBoundaries=0}
}
$stderr=@(Get-ChildItem "$root/verified-runtime","$root/early-close","$root/ui","$root/memory-ui" -Recurse -File|Where-Object {$_.Name -like 'stderr*' -or $_.Name -eq 'counter-error.txt'})
if(@($stderr|Where-Object Length -gt 0).Count){throw 'Review nonempty 9A stderr'}
$hashes=[ordered]@{}
foreach($assembly in @('Core','Infrastructure')){
 $published=(Get-FileHash "$root/verified-app/SystemCore.$assembly.dll").Hash
 $tested=(Get-FileHash "tests/SystemCore.ReliabilityTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.$assembly.dll").Hash
 if($published -ne $tested){throw '9A tested/published engine mismatch'}
 $hashes[$assembly]=$published
}
$inventory|ConvertTo-Json -Depth 5|Set-Content "$root/exception-inventory.json" -Encoding utf8
[ordered]@{ReliabilityGate='PASS';GeneratedAt=[DateTimeOffset]::UtcNow.ToString('o');CounterSource='System.Runtime EventCounters thrown-exception increments';BaselineMainFirstChance=1625;VerifiedLifetimes=$lifetimes;TotalFirstChance=($lifetimes.FirstChance|Measure-Object -Sum).Sum;HandledUnexpected=0;Unhandled=0;UnobservedTasks=0;TopTypes=@($inventory|Group-Object Type|ForEach-Object{[pscustomobject]@{Type=$_.Name;Count=($_.Group.Count|Measure-Object -Sum).Sum}});Suites=$suites;Runtime=$runtime;EarlyClose=$early;EngineHashes=$hashes;AppHash=(Get-FileHash "$root/verified-app/SystemCore.App.dll").Hash;Scope='Observed lifetimes with source-reviewed catch sites. No forced GC; not proof of unobserved-event absence in every future finalization. 9B may begin only after this gate.'}|ConvertTo-Json -Depth 8|Set-Content "$root/gate.json" -Encoding utf8
