$ErrorActionPreference='Stop'
$evidence='artifacts/f1-ui/test-results/helper-identity'
$fixture='artifacts/f1-ui/helper-identity-fixture'
$env:DOTNET_ROOT='C:\Users\Falcone\AppData\Local\Temp\opencode\systemcore-sdk'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$elevated=& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Get-ProcessElevation.ps1)))) -ProcessId $PID
if(!$elevated){throw 'Already elevated controller required; no UAC requested by this probe'}
New-Item -ItemType Directory -Force $evidence|Out-Null
& "$env:DOTNET_ROOT/dotnet.exe" publish tests/SystemCore.F1ParentTests -c Release -r win-x64 --self-contained false -o $fixture | Out-File "$evidence/build.txt" -Encoding utf8
if($LASTEXITCODE){throw 'Parent fixture build failed'}
Copy-Item artifacts/f1-ui/publish/ElevatedHost "$fixture/ElevatedHost" -Recurse
Copy-Item "$fixture/F1ParentProbe.exe" "$fixture/F1.exe"
Copy-Item "$fixture/F1ParentProbe.exe" "$fixture/SystemCore.App.exe"
New-Item -ItemType Directory -Force "$fixture/elsewhere"|Out-Null
Get-ChildItem $fixture -File|Copy-Item -Destination "$fixture/elsewhere"
$helper=(Resolve-Path "$fixture/ElevatedHost/SystemCore.ElevatedHost.exe").Path
$checks=[Collections.Generic.List[object]]::new()
foreach($parent in @('F1.exe','SystemCore.App.exe','F1ParentProbe.exe','elsewhere/F1.exe')){
 foreach($endpoint in @('cleaner','startup')){
  $mode=if($parent -eq 'F1.exe'){'accept-parent'}else{'reject-parent'}
  $output=& (Resolve-Path "$fixture/$parent").Path $helper $endpoint $mode
  $exit=$LASTEXITCODE
  $output|Set-Content "$evidence/$($parent.Replace('/','-'))-$endpoint.json" -Encoding utf8
  $checks.Add(($output|ConvertFrom-Json))
  if($exit){$checks|ConvertTo-Json -Depth 6|Set-Content "$evidence/checks.json" -Encoding utf8;throw 'Helper parent-path validation failed'}
 }
}
$checks|ConvertTo-Json -Depth 6|Set-Content "$evidence/checks.json" -Encoding utf8
$matching=@('SystemCore.ElevatedHost.exe','SystemCore.ElevatedHost.dll','SystemCore.Infrastructure.dll','SystemCore.Core.dll')|ForEach-Object{
 $original=(Get-FileHash "artifacts/f1-ui/publish/ElevatedHost/$_").Hash;$copy=(Get-FileHash "$fixture/ElevatedHost/$_").Hash
 if($original -ne $copy){throw 'Helper fixture bytes differ from publication'}
 [pscustomobject]@{File=$_;Sha256=$original}
}
@{Result='PASS';Checks=$checks.Count;Payload='Always invalid Version 0; no target operation';HelperHashes=$matching;Scope='Actual published helper and real OS parent-path/pipe peer checks in isolated fixture. Already elevated token; not an interactive UAC or alternate-account test.'}|ConvertTo-Json -Depth 5|Set-Content "$evidence/result.json" -Encoding utf8
