param([ValidateSet('Probe','Start','Stop','Disable')][string]$Action='Probe',
 [Parameter(Mandatory=$true)][string]$EvidencePath)
$ErrorActionPreference='Stop'
# Elevated diagnostics only. This script must never launch SystemCore.
$principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if(!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Diagnostics require elevation'}
$wpr="$env:WINDIR\System32\wpr.exe"
New-Item -ItemType Directory -Force $EvidencePath | Out-Null
Start-Transcript -Path "$EvidencePath/wpr-$Action.txt" -Force
try {
 switch($Action){
  'Probe' { & $wpr -status; & $wpr -HeapTracingConfig SystemCore.App.exe }
  'Start' {
   & $wpr -HeapTracingConfig SystemCore.App.exe enable
   if($LASTEXITCODE){throw "Heap configuration failed: $LASTEXITCODE"}
   & $wpr -start Heap -filemode
   if($LASTEXITCODE){& $wpr -HeapTracingConfig SystemCore.App.exe disable;throw 'Heap recording failed'}
  }
  'Stop' {
   try {& $wpr -stop "$EvidencePath/native-heap.etl";if($LASTEXITCODE){throw 'Trace save failed'}}
   finally {& $wpr -HeapTracingConfig SystemCore.App.exe disable}
  }
  'Disable' { & $wpr -HeapTracingConfig SystemCore.App.exe disable }
 }
} finally {Stop-Transcript}
