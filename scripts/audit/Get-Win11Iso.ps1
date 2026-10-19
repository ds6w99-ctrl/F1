# Host-side: download an official Windows 11 (retail, English x64) ISO from microsoft.com.
# Uses the documented software-download API flow; the ISO comes from *.prss.microsoft.com (official).
param([string]$OutDir = 'G:\ISO')
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$log = Join-Path $OutDir 'win11-iso-download.log'
"download attempt $(Get-Date -Format o)" | Set-Content $log

$page = & curl.exe -s -L -A 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126 Safari/537.36' 'https://www.microsoft.com/en-us/software-download/windows11'
"page bytes: $($page.Length)" | Add-Content $log

# Extract sku ids from the product-edition select
$skuIds = [regex]::Matches(($page -join "`n"), 'id="product-edition"[^>]*>.*?</select>', 'Singleline') |
    ForEach-Object { [regex]::Matches($_.Value, '<option value="([^"]+)"') } | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
"sku ids: $($skuIds -join ',')" | Add-Content $log
if (-not $skuIds) { 'no sku ids found' | Add-Content $log; exit 1 }

# Extract the pageId for the contentinclude API
$pageIdMatch = [regex]::Match(($page -join "`n"), 'pageId=([0-9a-fA-F-]{36})')
$pageId = if ($pageIdMatch.Success) { $pageIdMatch.Groups[1].Value } else { 'cfa9e580-2cf8-41b0-b6b7-570903bfa548' }
"pageId: $pageId" | Add-Content $log

$sessionId = [guid]::NewGuid().ToString()
$json = $null
foreach ($sku in $skuIds) {
    $api = "https://www.microsoft.com/en-us/api/controls/contentinclude/html?pageId=$pageId&host=www.microsoft.com&segments=software-download,windows11&query=&action=getskuinformationbyproductedition&sdVersion=2"
    $body = "productEditionId=$sku&sessionId=$sessionId"
    $resp = & curl.exe -s -X POST -A 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126 Safari/537.36' -H 'Content-Type: application/x-www-form-urlencoded' --data $body $api
    "sku $sku response bytes: $($resp.Length)" | Add-Content $log
    if ($resp -match '"uri"\s*:\s*"http') { $json = $resp | ConvertFrom-Json; break }
}
if (-not $json) { 'no JSON with uris' | Add-Content $log; $resp | Select-Object -First 1 | Add-Content $log; exit 1 }

$uri = $null
foreach ($sku in $json.skus) {
    foreach ($opt in $sku.productDownloadOptions) {
        foreach ($lang in $opt.languages) {
            "candidate: sku=$($sku.id) lang=$($lang.language) uri=$($opt.uri)" | Add-Content $log
            if ($lang.language -eq 'English' -and $opt.uri -match 'x64.*\.iso') { $uri = $opt.uri }
        }
    }
}
if (-not $uri) { $uri = $json.skus[0].productDownloadOptions[0].uri; "fallback uri" | Add-Content $log }
"chosen uri: $uri" | Add-Content $log
$outFile = Join-Path $OutDir 'Win11_24H2_English_x64.iso'
& curl.exe -L --retry 5 --retry-delay 5 -A 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126 Safari/537.36' -o $outFile $uri 2>&1 | Out-String | Add-Content $log
$item = Get-Item $outFile -ErrorAction SilentlyContinue
if ($item) { "downloaded: $($item.Length) bytes" | Add-Content $log; $h = Get-FileHash $outFile -Algorithm SHA256; "sha256: $($h.Hash)" | Add-Content $log } else { 'download produced no file' | Add-Content $log }
