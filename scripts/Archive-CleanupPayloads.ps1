$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$ledger='artifacts/project-cleanup/archived-payloads.json'
$archives=[Collections.Generic.List[object]]::new()
if(Test-Path $ledger){$existing=Get-Content $ledger -Raw|ConvertFrom-Json;foreach($entry in $existing){$archives.Add($entry)}}
$payloads=@(Get-ChildItem docs/archive/evidence -Recurse -File | Where-Object {$_.Name -in @('F1.exe','SystemCore.App.exe')} | Where-Object {Test-Path (Join-Path $_.DirectoryName ($_.BaseName+'.runtimeconfig.json'))} | Select-Object -ExpandProperty DirectoryName -Unique)
foreach($dir in $payloads){
 $zip=$dir+'-payload.zip'
 if(!(Test-Path $zip)){[IO.Compression.ZipFile]::CreateFromDirectory($dir,$zip,[IO.Compression.CompressionLevel]::Fastest,$false)}
 $opened=[IO.Compression.ZipFile]::OpenRead($zip)
 try{
  $files=@(Get-ChildItem $dir -File -Recurse);$entries=@{}
  foreach($entry in $opened.Entries){if($entry.Name){$entries[$entry.FullName.Replace('\','/')]=$entry}}
  if($entries.Count -ne $files.Count){throw "Archive count mismatch: $dir"}
  foreach($file in $files){
   $relative=$file.FullName.Substring($dir.Length+1).Replace('\','/');$entry=$entries[$relative]
   if(!$entry -or $entry.Length -ne $file.Length){throw "Archive mismatch: $relative"}
   $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
   try{if([BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') -ne (Get-FileHash $file.FullName).Hash){throw "Hash mismatch: $relative"}}
   finally{$stream.Dispose();$sha.Dispose()}
  }
 }finally{$opened.Dispose()}
 if(!($archives|Where-Object {$_.Directory -eq $dir})){$archives.Add([pscustomobject]@{Directory=$dir;Archive=$zip;Files=$files.Count;Sha256=(Get-FileHash $zip).Hash})}
 $archives|ConvertTo-Json -Depth 4|Set-Content $ledger -Encoding UTF8
 Remove-Item -LiteralPath $dir -Recurse -Force
}
