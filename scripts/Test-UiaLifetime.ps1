param([string]$EvidencePath='artifacts/debug/native-final-diagnostic/uia-lifetime')
$ErrorActionPreference='Stop'
$capture=[IO.File]::ReadAllText((Resolve-Path scripts/Test-NativeHeapCapture.ps1))
. ([scriptblock]::Create($capture.Substring(0,$capture.IndexOf("Diagnostic 'Start'")))) -EvidencePath $EvidencePath
$vmmap='C:\Users\Falcone\AppData\Local\Temp\opencode\native-memory-tools\VMMap\vmmap64.exe'
$p=$null
function Snapshot([string]$label){
 $p.Refresh()
 [pscustomobject]@{Label=$label;Utc=[DateTime]::UtcNow.ToString('o');Pid=$p.Id;PrivateBytes=$p.PrivateMemorySize64;WorkingSet=$p.WorkingSet64;Handles=$p.HandleCount;Threads=$p.Threads.Count}|ConvertTo-Json|Set-Content "$EvidencePath/$label.json" -Encoding utf8
 $v=Start-Process $vmmap -ArgumentList '-accepteula','-p',"$($p.Id)",('"'+"$EvidencePath/$label.xml"+'"') -PassThru
 try {if(!$v.WaitForExit(60000) -or $v.ExitCode){throw 'VMMap capture failed'}}finally{$v.Dispose()}
}
Diagnostic 'Start'
try {
 $p=Start-Process (Resolve-Path artifacts/debug/final-validation-app/SystemCore.App.exe) -PassThru -RedirectStandardError "$EvidencePath/stderr.txt"
 [pscustomobject]@{Pid=$p.Id;StartedUtc=$p.StartTime.ToUniversalTime().ToString('o')}|ConvertTo-Json|Set-Content "$EvidencePath/process.json" -Encoding utf8
 Start-Sleep -Seconds 60;Snapshot 'baseline'
 $script=(Resolve-Path scripts/Test-UiaReader.ps1).Path
 $command="& ([scriptblock]::Create([IO.File]::ReadAllText('$script'))) -TargetPid $($p.Id) -Seconds 90 | Set-Content '$EvidencePath/reader.json' -Encoding utf8"
 $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
 $reader=Start-Process powershell.exe -ArgumentList '-NoProfile','-EncodedCommand',$encoded -PassThru
 try {if(!$reader.WaitForExit(150000) -or $reader.ExitCode){throw 'UIA reader failed'}}finally{$reader.Dispose()}
 Snapshot 'reader-exited'
 Start-Sleep -Seconds 90;Snapshot 'disconnected-idle'
} finally {
 if($p){if(!$p.HasExited){$null=$p.CloseMainWindow();$null=$p.WaitForExit(10000)};$p.Dispose()}
 Diagnostic 'Stop'
}
