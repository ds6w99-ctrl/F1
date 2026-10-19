# Elevated: check disk space and list the retail ISO's editions.
$log = 'G:\F1\artifacts\audit-baseline\iso-info.log'
"iso-info $(Get-Date -Format o)" | Set-Content $log
Get-PSDrive G | Select-Object Used,Free | Out-String | Add-Content $log
$img = Mount-DiskImage -ImagePath 'G:\ISO\Win11_Client_x64_en-us_26300.iso' -PassThru
$letter = $null
for ($i = 0; $i -lt 30 -and -not $letter; $i++) { Start-Sleep -Milliseconds 500; $v = Get-Volume -DiskImage $img -ErrorAction SilentlyContinue; if ($v) { $letter = $v.DriveLetter } }
"iso letter: $letter" | Add-Content $log
if ($letter) {
    $wim = "${letter}:\sources\install.wim"
    "install.wim: $(Test-Path $wim)" | Add-Content $log
    "install.esd: $(Test-Path "${letter}:\sources\install.esd")" | Add-Content $log
    if (Test-Path $wim) {
        & dism.exe /Get-WimInfo /WimFile:$wim 2>&1 | Out-String | Add-Content $log
    }
}
Dismount-DiskImage -ImagePath 'G:\ISO\Win11_Client_x64_en-us_26300.iso' | Out-Null
'done' | Add-Content $log
