$ErrorActionPreference='Stop'
$evidence='artifacts/phase9/9a/early-close'
New-Item -ItemType Directory -Force $evidence|Out-Null
$started=[DateTime]::UtcNow
$results=@()
for($cycle=1;$cycle -le 3;$cycle++){
 $p=Start-Process (Resolve-Path artifacts/phase9/9a/verified-app/SystemCore.App.exe) -PassThru -RedirectStandardError "$evidence/stderr-$cycle.txt"
 try{
  $clock=[Diagnostics.Stopwatch]::StartNew()
  do{Start-Sleep -Milliseconds 50;$p.Refresh()}while(!$p.HasExited -and $p.MainWindowHandle -eq [IntPtr]::Zero -and $clock.Elapsed.TotalSeconds -lt 30)
  if($p.HasExited -or $p.MainWindowHandle -eq [IntPtr]::Zero){throw 'Early-close launch failed'}
  Start-Sleep -Milliseconds 150
  $closed=$p.CloseMainWindow() -and $p.WaitForExit(30000)
  $results+= [pscustomobject]@{Cycle=$cycle;NormalClose=$closed;Seconds=$clock.Elapsed.TotalSeconds;Method='Native WM_CLOSE while startup providers may be active; no UIA or forced GC'}
  if(!$closed){throw 'Early close did not finish'}
 }finally{$p.Dispose()}
}
Get-ChildItem (Join-Path $env:LOCALAPPDATA 'SystemCore/ReliabilityAudit') -Filter 'audit-*.json'|Where-Object LastWriteTimeUtc -ge $started|ForEach-Object{Copy-Item $_.FullName $evidence}
$results|ConvertTo-Json|Set-Content "$evidence/result.json" -Encoding utf8
