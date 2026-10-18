# VM-side standard-user round: run inside F1User's console session (standard token).
# Each command records facts; verdicts are assigned in the matrix.
$out = 'C:\F1Audit\stduser-out'
New-Item -ItemType Directory -Force $out | Out-Null
$log = Join-Path $out 'std-round.log'
"" | Set-Content $log
$probe = 'C:\F1Audit\broker-probe\F1.exe'
function Step([string]$name) { "=== $name @ $(Get-Date -Format o) ===" | Add-Content $log -Encoding ASCII }
'whoami: ' + (whoami) | Add-Content $log -Encoding ASCII
$groups = whoami /groups | Select-String 'Mandatory Label|Administrators'
'integrity: ' + ($groups | Out-String) | Add-Content $log -Encoding ASCII

Step 'inventory (read-only)'
& $probe inventory --out "$out\std-inventory.json" 2>&1 | Out-String | Add-Content $log -Encoding ASCII
"exit=$LASTEXITCODE" | Add-Content $log -Encoding ASCII

Step 'startup-list (read-only)'
& $probe startup-list --out "$out\std-startup.json" 2>&1 | Out-String | Add-Content $log -Encoding ASCII
"exit=$LASTEXITCODE" | Add-Content $log -Encoding ASCII

Step 'clean-scan (read-only scan)'
& $probe clean-scan --out "$out\std-clean.json" 2>&1 | Out-String | Add-Content $log -Encoding ASCII
"exit=$LASTEXITCODE" | Add-Content $log -Encoding ASCII

Step 'broker-delete (elevated-root fixture; standard token)'
& $probe broker-delete --file 'C:\Program Files\F1BrokerFixture\broker-fixture2.txt' --out "$out\std-broker.json" 2>&1 | Out-String | Add-Content $log -Encoding ASCII
"exit=$LASTEXITCODE" | Add-Content $log -Encoding ASCII

Step 'uninstall attempt (per-machine MSI as standard user, quiet)'
& $probe uninstall-clean --name '7-Zip 24.09 (x64 edition)' --quiet --timeout-min 3 --out "$out\std-uninstall.json" 2>&1 | Out-String | Add-Content $log -Encoding ASCII
"exit=$LASTEXITCODE" | Add-Content $log -Encoding ASCII

"STD ROUND DONE @ $(Get-Date -Format o)" | Add-Content $log -Encoding ASCII
