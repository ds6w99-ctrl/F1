param([string]$AppPath='artifacts/phase5/app/SystemCore.App.exe', [string]$EvidencePath='artifacts/phase5/stability', [int]$IdleSeconds=180)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $EvidencePath | Out-Null
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
# Passive production measurements only: no UIA, accessibility reader, debugger,
# continuous heap tracing, forced GC or memory trimming.
$samples=[Collections.Generic.List[object]]::new()
$process=Start-Process (Resolve-Path $AppPath) -PassThru -RedirectStandardError "$EvidencePath/idle-stderr.txt"
$watch=[Diagnostics.Stopwatch]::StartNew()
try {
 $elevated=& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Get-ProcessElevation.ps1)))) -ProcessId $process.Id
 if($elevated){throw 'Production idle app token is elevated'}
 do {
  $process.Refresh()
  if($process.HasExited){throw 'Production app exited during idle observation'}
  $samples.Add([pscustomobject]@{Seconds=$watch.Elapsed.TotalSeconds;PrivateBytes=$process.PrivateMemorySize64;WorkingSet=$process.WorkingSet64;Handles=$process.HandleCount;Threads=$process.Threads.Count;Responding=$process.Responding})
  $samples|ConvertTo-Json|Set-Content "$EvidencePath/idle-process.json" -Encoding utf8
  Start-Sleep -Seconds 5
 }while($watch.Elapsed.TotalSeconds -lt $IdleSeconds)
 $closed=$process.CloseMainWindow()
 if(!$closed -or !$process.WaitForExit(15000)){throw 'Production app did not close normally'}
 [pscustomobject]@{MeasuredSeconds=$watch.Elapsed.TotalSeconds;NormalClose=$true;Elevated=$elevated;Samples=$samples.Count;ExitCode=$process.ExitCode;Method='Passive process sampling; no UI Automation'}|ConvertTo-Json|Set-Content "$EvidencePath/idle-result.json" -Encoding utf8
}finally{if(!$process.HasExited){$null=$process.CloseMainWindow();$null=$process.WaitForExit(15000)};$process.Dispose()}
& "$env:DOTNET_ROOT\dotnet.exe" tests/SystemCore.UninstallerTests/bin/Release/net10.0-windows10.0.26100.0/SystemCore.UninstallerTests.dll --lifecycle-core "$EvidencePath/core-lifecycle.json" | Out-File "$EvidencePath/core-tests.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Core synthetic lifecycle failed'}
