$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead((Resolve-Path artifacts/f1-ui/baseline/source-before.zip))
$changes=[Collections.Generic.List[object]]::new()
$files=[Collections.Generic.List[object]]::new()
try{
 foreach($entry in $zip.Entries){
  if(!$entry.Name){continue}
  if(!(Test-Path $entry.FullName)){throw "Archived source missing: $($entry.FullName)"}
  $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
  try{$before=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$sha.Dispose();$stream.Dispose()}
  $after=(Get-FileHash $entry.FullName).Hash
  $files.Add([pscustomobject]@{File=$entry.FullName;Sha256=$after})
  if($before -ne $after){$changes.Add([pscustomobject]@{File=$entry.FullName;Before=$before;After=$after})}
 }
}finally{$zip.Dispose()}
$engine=@($changes|Where-Object {$_.File.StartsWith('src/SystemCore.Core/') -or $_.File.StartsWith('src/SystemCore.Infrastructure/')})
$allowed=@('src/SystemCore.Core/ProductIdentity.cs','src/SystemCore.Infrastructure/Cleaner/WindowsElevatedCleanerClient.cs','src/SystemCore.Infrastructure/Startup/StartupElevation.cs','src/SystemCore.Infrastructure/Startup/StartupValidationEnvironment.cs')
if(@($engine|Where-Object {$_.File -notin $allowed}).Count){throw 'Unexpected engine source change'}
if(@($changes|Where-Object {$_.File.EndsWith('packages.lock.json')}).Count){throw 'Existing dependency lock changed'}
$phase9=Get-Content artifacts/phase9/summary.json -Raw|ConvertFrom-Json
$historical=(Get-FileHash artifacts/phase9/app/SystemCore.App.dll).Hash
# Preserve the historical record without rewriting it.
$recorded=$phase9.AppAssemblyHash
if(!$recorded){throw 'Historical App hash schema must be reviewed'}
if($recorded -ne $historical){throw 'Historical Phase 9 App assembly differs from accepted evidence'}
[pscustomobject]@{
 Result='PASS';ArchivedFilesStillPresent=$true;ExistingDependencyLocksUnchanged=$true
 EngineFiles=$engine;ModifiedArchivedFiles=$changes.Count;Changes=$changes;CurrentArchivedSourceHashes=$files
 HistoricalPhase9AppSha256=$historical
 Note='Existing-file comparison against source-before.zip, not a Git diff. The snapshot includes newly added, then-unmerged presentation files. Additional presentation resources, probes and docs are mapped in the implementation report.'
}|ConvertTo-Json -Depth 6|Set-Content artifacts/f1-ui/test-results/source-change-review.json -Encoding utf8
