# Elevated wrapper: run the F1ParentTests parent-identity security probe and record results.
$log = 'G:\F1\artifacts\audit-baseline\f1parent-results.log'
"elevated F1Parent run $(Get-Date -Format o)" | Set-Content $log
$env:DOTNET_ROOT = 'C:\Users\Falcone\.dotnet'
$env:Path = "C:\Users\Falcone\.dotnet;$env:Path"
& 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -ExecutionPolicy Bypass -File 'G:\F1\scripts\audit\Run-F1ParentProbe.ps1' *>&1 | Out-String | Add-Content $log
