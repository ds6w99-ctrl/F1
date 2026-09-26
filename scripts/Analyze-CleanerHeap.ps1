param([Parameter(Mandatory=$true)][string]$DumpPath,
 [Parameter(Mandatory=$true)][string]$OutputPath,
 [string]$DumpTool='C:\Users\Falcone\AppData\Local\Temp\opencode\memory-tools\dotnet-dump.exe')
$ErrorActionPreference='Stop'
# Offline analysis only. -live distinguishes rooted objects from not-yet-collected
# allocations in a non-GC-triggering dump. gcroot follow-up uses concrete addresses.
$commands=@('dumpheap -stat','dumpheap -live -stat','eeheap -gc','gchandles',
 'dumpheap -type CleanerScanSession','dumpheap -type CleanerItem',
 'dumpheap -type CleanerViewModel','dumpheap -type CleanerView',
 'dumpheap -type CleanerScanState','dumpheap -type DispatcherQueueTimer','exit')
$arguments=@('analyze',$DumpPath)
foreach($command in $commands){$arguments+=@('-c',$command)}
& $DumpTool @arguments | Out-File $OutputPath -Encoding utf8
if($LASTEXITCODE){throw 'Offline heap analysis failed'}
