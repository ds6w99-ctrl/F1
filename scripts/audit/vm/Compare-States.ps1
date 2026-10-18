# VM-side independent verification: diff two Capture-State snapshots for the audit apps.
# Reports facts only (present before/after, changed values); PASS/FAIL verdicts are assigned
# in the matrix by the auditor. Never modifies the system.
param(
    [string]$Before = 'C:\F1Audit\states\clean.json',
    [string]$After = 'C:\F1Audit\states\after.json',
    [string]$Out = 'C:\F1Audit\compare.json',
    [string[]]$Keywords = @()
)
$ErrorActionPreference = 'Stop'
$before = Get-Content $Before -Raw | ConvertFrom-Json
$after = Get-Content $After -Raw | ConvertFrom-Json

function Like-Any([string]$value) {
    foreach ($k in $Keywords) { if ($value -and $value -like "*$k*") { return $true } }
    return $false
}

$sections = [ordered]@{}
function Add([string]$section, [string]$name, [bool]$inBefore, [bool]$inAfter, [string]$detail = '') {
    if (-not $sections.Contains($section)) { $sections[$section] = New-Object System.Collections.Generic.List[object] }
    $sections[$section].Add([ordered]@{ Name = $name; InBefore = $inBefore; InAfter = $inAfter; Detail = $detail })
}

# --- uninstall entries ---
$bu = @($before.Uninstall | Where-Object { Like-Any $_.DisplayName })
$au = @($after.Uninstall | Where-Object { Like-Any $_.DisplayName })
$keys = @($bu | ForEach-Object { $_.Key }) + @($au | ForEach-Object { $_.Key }) | Sort-Object -Unique
foreach ($key in $keys) {
    $b = @($bu | Where-Object Key -eq $key)
    $a = @($au | Where-Object Key -eq $key)
    $detail = ''
    if ($b.Count -eq 1 -and $a.Count -eq 1) {
        $diff = @()
        foreach ($prop in 'DisplayName','Publisher','Version','UninstallString','QuietUninstallString','InstallLocation') {
            if ([string]$b[0].$prop -ne [string]$a[0].$prop) { $diff += ("{0}: '{1}' -> '{2}'" -f $prop, $b[0].$prop, $a[0].$prop) }
        }
        if ($diff.Count) { $detail = 'changed: ' + ($diff -join '; ') }
    }
    Add 'Uninstall' $key ($b.Count -gt 0) ($a.Count -gt 0) $detail
}

# --- services ---
$bs = @($before.Services | Where-Object { Like-Any $_.DisplayName -or (Like-Any $_.Name) })
$as = @($after.Services | Where-Object { Like-Any $_.DisplayName -or (Like-Any $_.Name) })
$names = @($bs | ForEach-Object { $_.Name }) + @($as | ForEach-Object { $_.Name }) | Sort-Object -Unique
foreach ($n in $names) {
    $b = @($bs | Where-Object Name -eq $n); $a = @($as | Where-Object Name -eq $n)
    Add 'Services' $n ($b.Count -gt 0) ($a.Count -gt 0) $(if ($b.Count -and $a.Count) { "startmode $($b[0].StartMode) -> $($a[0].StartMode)" } else { '' })
}

# --- scheduled tasks ---
$bt = @($before.Tasks | Where-Object { Like-Any $_.Path })
$at = @($after.Tasks | Where-Object { Like-Any $_.Path })
$paths = @($bt | ForEach-Object { $_.Path }) + @($at | ForEach-Object { $_.Path }) | Sort-Object -Unique
foreach ($p in $paths) { Add 'Tasks' $p (@($bt | Where-Object Path -eq $p).Count -gt 0) (@($at | Where-Object Path -eq $p).Count -gt 0) }

# --- run keys ---
$br = @($before.Run | Where-Object { Like-Any $_.Name -or Like-Any $_.Command })
$ar = @($after.Run | Where-Object { Like-Any $_.Name -or Like-Any $_.Command })
$rkeys = @($br | ForEach-Object { "$($_.Root)|$($_.Name)" }) + @($ar | ForEach-Object { "$($_.Root)|$($_.Name)" }) | Sort-Object -Unique
foreach ($rk in $rkeys) { Add 'Run' $rk (@($br | Where-Object { "$($_.Root)|$($_.Name)" -eq $rk }).Count -gt 0) (@($ar | Where-Object { "$($_.Root)|$($_.Name)" -eq $rk }).Count -gt 0) }

# --- shortcuts ---
$bl = @($before.Shortcuts | Where-Object { Like-Any $_.Name -or Like-Any $_.Target })
$al = @($after.Shortcuts | Where-Object { Like-Any $_.Name -or Like-Any $_.Target })
$lkeys = @($bl | ForEach-Object { "$($_.Dir)|$($_.Name)" }) + @($al | ForEach-Object { "$($_.Dir)|$($_.Name)" }) | Sort-Object -Unique
foreach ($lk in $lkeys) {
    $b = @($bl | Where-Object { "$($_.Dir)|$($_.Name)" -eq $lk }); $a = @($al | Where-Object { "$($_.Dir)|$($_.Name)" -eq $lk })
    Add 'Shortcuts' $lk ($b.Count -gt 0) ($a.Count -gt 0) $(if ($b.Count) { "target=$($b[0].Target)" } else { "target=$($a[0].Target)" })
}

# --- top-level directories ---
$bd = @($before.TopDirs | Where-Object { Like-Any $_.Name })
$ad = @($after.TopDirs | Where-Object { Like-Any $_.Name })
$dkeys = @($bd | ForEach-Object { "$($_.Base)|$($_.Name)" }) + @($ad | ForEach-Object { "$($_.Base)|$($_.Name)" }) | Sort-Object -Unique
foreach ($dk in $dkeys) { Add 'TopDirs' $dk (@($bd | Where-Object { "$($_.Base)|$($_.Name)" -eq $dk }).Count -gt 0) (@($ad | Where-Object { "$($_.Base)|$($_.Name)" -eq $dk }).Count -gt 0) }

# --- app registry keys ---
$bk = @($before.AppKeys | Where-Object { Like-Any $_.Path })
$ak = @($after.AppKeys | Where-Object { Like-Any $_.Path })
$kkeys = @($bk | ForEach-Object { "$($_.Root)|$($_.Path)" }) + @($ak | ForEach-Object { "$($_.Root)|$($_.Path)" }) | Sort-Object -Unique
foreach ($kk in $kkeys) { Add 'AppKeys' $kk (@($bk | Where-Object { "$($_.Root)|$($_.Path)" -eq $kk }).Count -gt 0) (@($ak | Where-Object { "$($_.Root)|$($_.Path)" -eq $kk }).Count -gt 0) }

# --- appx packages ---
$bp = @($before.Appx | Where-Object { Like-Any $_.Name })
$ap = @($after.Appx | Where-Object { Like-Any $_.Name })
$pnames = @($bp | ForEach-Object { $_.Name }) + @($ap | ForEach-Object { $_.Name }) | Sort-Object -Unique
foreach ($pn in $pnames) { Add 'Appx' $pn (@($bp | Where-Object Name -eq $pn).Count -gt 0) (@($ap | Where-Object Name -eq $pn).Count -gt 0) }

$result = [ordered]@{
    ComparedAt = (Get-Date -Format o); Keywords = $Keywords
    Before = $Before; After = $After
    Sections = $sections
}
$result | ConvertTo-Json -Depth 6 | Set-Content $Out -Encoding UTF8
"compare written -> $Out"
foreach ($section in $sections.Keys) {
    $onlyBefore = @($sections[$section] | Where-Object { $_.InBefore -and -not $_.InAfter }).Count
    $onlyAfter = @($sections[$section] | Where-Object { -not $_.InBefore -and $_.InAfter }).Count
    "$section : removed=$onlyBefore added=$onlyAfter total=$($sections[$section].Count)"
}
