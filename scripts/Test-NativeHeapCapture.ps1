param([string]$EvidencePath='artifacts/debug/native-final-diagnostic/profiled', [int]$DurationSeconds=960)
$ErrorActionPreference='Stop'
$principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Run the controller unelevated; only WPR may be elevated'}
$EvidencePath=[IO.Path]::GetFullPath($EvidencePath)
New-Item -ItemType Directory -Force $EvidencePath | Out-Null
function Diagnostic([string]$action) {
 $script=(Resolve-Path scripts/Invoke-NativeTrace.ps1).Path.Replace("'","''")
 $path=$EvidencePath.Replace("'","''")
 $command="& ([scriptblock]::Create([IO.File]::ReadAllText('$script'))) -Action $action -EvidencePath '$path'"
 $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
 $diagnostic=Start-Process powershell.exe -Verb RunAs -ArgumentList '-NoProfile','-EncodedCommand',$encoded -PassThru
 try {$diagnostic.WaitForExit();if($diagnostic.ExitCode){throw "Elevated diagnostic $action failed: $($diagnostic.ExitCode)"}}
 finally {$diagnostic.Dispose()}
}
Diagnostic 'Start'
try {
 & ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Test-CleanerLifecycle.ps1)))) -AppPath artifacts/debug/final-validation-app/SystemCore.App.exe -EvidencePath "$EvidencePath/lifecycle" -DurationSeconds $DurationSeconds -MinimumCycles 30 -StartupIdleSeconds 60 -NativeMapTool C:\Users\Falcone\AppData\Local\Temp\opencode\native-memory-tools\VMMap\vmmap64.exe
} finally {Diagnostic 'Stop'}
