param([string]$EvidencePath='artifacts/debug/native/isolation')
$ErrorActionPreference='Stop'
$rows=foreach($file in Get-ChildItem $EvidencePath -Filter '*.xml') {
 $xml=[xml][IO.File]::ReadAllText($file.FullName)
 # Top-level regions only: child blocks partition the parent and must not be
 # counted a second time. Commit includes shared backing; PrivateBytes does not.
 $regions=$xml.root.Snapshots.Snapshot.MemoryRegions.Region
 foreach($group in $regions | Group-Object Type) {
  [pscustomobject]@{Snapshot=$file.BaseName;Type=$group.Name;Regions=$group.Count;
   PrivateBytes=($group.Group|Measure-Object PrivateBytes -Sum).Sum;
   CommittedBytes=($group.Group|Measure-Object Commit -Sum).Sum;
   PrivateWorkingSet=($group.Group|Measure-Object PrivateWS -Sum).Sum}
 }
}
$rows | Export-Csv "$EvidencePath/vmmap-breakdown.csv" -NoTypeInformation -Encoding utf8
$rows | ConvertTo-Json -Depth 4 | Set-Content "$EvidencePath/vmmap-breakdown.json" -Encoding utf8
$images=foreach($file in Get-ChildItem $EvidencePath -Filter '*.xml') {
 $xml=[xml][IO.File]::ReadAllText($file.FullName)
 foreach($r in $xml.root.Snapshots.Snapshot.MemoryRegions.Region | Where-Object {$_.Type -match 'Image'}) {
  [pscustomobject]@{Snapshot=$file.BaseName;Image=$r.Details;PrivateBytes=[long]$r.PrivateBytes;Commit=[long]$r.Commit}
 }
}
$images | Export-Csv "$EvidencePath/images.csv" -NoTypeInformation -Encoding utf8
$rows | Where-Object {$_.PrivateBytes -gt 0} | Format-Table Snapshot,Type,PrivateBytes,CommittedBytes
