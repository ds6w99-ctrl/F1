$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$path='artifacts/f1-ui/test-results/ui-06/diagnostic-export.zip'
$zip=[IO.Compression.ZipFile]::OpenRead((Resolve-Path $path))
try{
 $names=@($zip.Entries.FullName|Sort-Object)
 if(($names -join ',') -ne 'logs/recent.jsonl,manifest.json'){throw 'Unexpected diagnostic archive entries'}
 $records=0;$text=''
 foreach($entry in $zip.Entries){
  $reader=[IO.StreamReader]::new($entry.Open());try{$content=$reader.ReadToEnd()}finally{$reader.Dispose()};$text+=$content
  if($entry.FullName -eq 'manifest.json'){$manifest=$content|ConvertFrom-Json;if($manifest.AppVersion -ne '0.9.0-dev' -or $manifest.Architecture -ne 'X64'){throw 'Wrong diagnostic metadata'}}
  else{foreach($line in $content.Split("`n")|Where-Object {$_ -ne ''}){$record=$line|ConvertFrom-Json;if(@($record.PSObject.Properties.Name|Where-Object {$_ -notin @('Timestamp','Feature','Event','Code')}).Count){throw 'Unexpected private export field'};$records++}}
 }
 foreach($value in @($env:USERNAME,$env:USERPROFILE,[Environment]::MachineName)){if($value -and $text.IndexOf($value,[StringComparison]::OrdinalIgnoreCase) -ge 0){throw 'Private identifier in diagnostic export'}}
}finally{$zip.Dispose()}
@{Result='PASS';Entries=$names;Records=$records;Version=$manifest.AppVersion;Architecture=$manifest.Architecture;Uploaded=$false;Source=$path}|ConvertTo-Json|Set-Content artifacts/f1-ui/test-results/export-review.json -Encoding utf8
