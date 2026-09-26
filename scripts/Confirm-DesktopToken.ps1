$ErrorActionPreference='Stop'
$elevated=& ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path scripts/Get-ProcessElevation.ps1)))) -ProcessId $PID
if($elevated){throw 'Expected the interactive desktop non-elevated token'}
[pscustomobject]@{Elevated=$elevated;ProcessId=$PID;Purpose='Diagnostics launcher token and native exit-code validation'}|ConvertTo-Json
