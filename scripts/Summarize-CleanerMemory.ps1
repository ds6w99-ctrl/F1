param([Parameter(Mandatory=$true)][string]$EvidencePath)
$ErrorActionPreference='Stop'
$rows=Get-Content "$EvidencePath/observations.json" -Raw -Encoding utf8 | ConvertFrom-Json
$metrics=foreach($row in $rows){
 $values=[ordered]@{Cycle=$row.Cycle;Seconds=$row.Seconds;PrivateBytes=$row.PrivateBytes;WorkingSet=$row.WorkingSet;Handles=$row.Handles;Threads=$row.Threads}
 foreach($match in [regex]::Matches($row.Diagnostics,'(?:^|;)([A-Za-z0-9]+):(\d+)(?=;|$)')){$values[$match.Groups[1].Value]=[long]$match.Groups[2].Value}
 [pscustomobject]$values
}
$metrics | Export-Csv "$EvidencePath/memory.csv" -NoTypeInformation -Encoding UTF8
$midSeconds=[double]($metrics[-1].Seconds) / 2.0
$lastHalf=@($metrics | Where-Object {$_.Seconds -ge $midSeconds})
$xMean=($lastHalf | Measure-Object Seconds -Average).Average
$yMean=($lastHalf | Measure-Object PrivateBytes -Average).Average
$numerator=0.0;$denominator=0.0
foreach($row in $lastHalf){$x=$row.Seconds-$xMean;$numerator+=$x*($row.PrivateBytes-$yMean);$denominator+=$x*$x}
$slope=if($denominator -gt 0){$numerator/$denominator*60/1MB}else{$null}
$summary=[ordered]@{Samples=$metrics.Count;First=$metrics[0];AfterFirstCycle=$metrics[1];Midpoint=($metrics | Sort-Object { [math]::Abs($_.Seconds - $midSeconds) } | Select-Object -First 1);Last=$metrics[-1];LastHalfSamples=$lastHalf.Count;LastHalfPrivateMiBPerMinute=$slope;LastHalfPrivateMin=($lastHalf | Measure-Object PrivateBytes -Minimum).Minimum;LastHalfPrivateMax=($lastHalf | Measure-Object PrivateBytes -Maximum).Maximum}
foreach($key in @('PrivateBytes','WorkingSet','ManagedBytes','HeapCommitted','LOHAfterLastGC','Handles','Threads','ObservedLiveSessions','ObservedLiveItems')){$summary["Peak$key"]=($metrics | Measure-Object $key -Maximum).Maximum}
$summary | ConvertTo-Json -Depth 4 | Set-Content "$EvidencePath/memory-summary.json" -Encoding UTF8
$summary | ConvertTo-Json -Depth 4
