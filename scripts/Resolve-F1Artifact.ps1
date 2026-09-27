# Dot-sourceable artifact resolver for the F1 verification harness.
#
# Why this exists: the harness used to point at whatever publish folder a given phase happened to
# leave behind (artifacts/phase8/app/SystemCore.App.exe, artifacts/f1-ui/publish/F1.exe, ...). Those
# folders are gitignored build output, so they are absent on a clean checkout, stale after a phase
# moves on, and there is no way to tell which one matches the current source.
#
# Discovery order:
#   1. an explicit -AppPath, if the caller supplied one (still validated, never trusted blindly);
#   2. newest managed payload under artifacts/publish/App, artifacts/current or the app bin folder.
#      Historical phase folders are never searched. Production and validation builds are separated.
#
# A candidate is only accepted when ALL of these hold:
#   * the file exists and starts with the 'MZ' PE header;
#   * ProductName is exactly 'F1'  (the product is F1; a renamed copy is rejected, see below);
#   * ProductVersion starts with the repository <Version> from Directory.Build.props;
#   * the file is named F1.exe.
#
# The name check is not cosmetic. A copy of the published app named SystemCore.App.exe launches and
# reports a window titled 'F1', but its UI tree exposes no AutomationId at all (NavSettings is not
# found) because WinUI does not resolve its resource index under a renamed executable. That cost a
# full misdiagnosis of Test-ThemeLocalization, so the resolver refuses renamed copies outright.
#
# -RequireValidationMarker additionally requires the build to contain the string literal
# "Prepare synthetic fixtures", which is compiled in only under CLEANER_VALIDATION. It is the static
# counterpart of the runtime check 'Production excludes synthetic fixture controls'.
#
# The marker is searched as UTF-16 in the sibling F1.dll, not in F1.exe. F1.exe is only the native
# apphost stub and carries none of the managed code, and .NET string literals live in the metadata
# #US heap as UTF-16, so an ASCII scan of either file misses it. Verified: the literal is present in a
# CLEANER_VALIDATION build and absent from a production build of the same commit.

function Resolve-F1AppArtifact {
    [CmdletBinding()]
    param(
        [string]$Repo = (Split-Path -Parent $PSScriptRoot),
        [string]$AppPath,
        [switch]$RequireValidationMarker,
        [switch]$Quiet
    )

    $ErrorActionPreference = 'Stop'
    $validationMarker = 'Prepare synthetic fixtures'
    $validationMarkerUtf16 = [Text.Encoding]::Unicode.GetBytes($validationMarker)

    # Byte search for a UTF-16 needle. Decoding the whole 1.7 MB assembly to a string also works but
    # allocates a copy per candidate; this stays exact and cheap.
    function Test-ContainsBytes([byte[]]$haystack, [byte[]]$needle) {
        for ($i = 0; $i -le $haystack.Length - $needle.Length; $i++) {
            $hit = $true
            for ($j = 0; $j -lt $needle.Length; $j++) {
                if ($haystack[$i + $j] -ne $needle[$j]) { $hit = $false; break }
            }
            if ($hit) { return $true }
        }
        return $false
    }

    # The repository version is what ties a binary to the current source tree.
    $propsPath = Join-Path $Repo 'Directory.Build.props'
    $expectedVersion = $null
    if (Test-Path $propsPath) {
        $m = [regex]::Match([IO.File]::ReadAllText($propsPath), '<Version>([^<]+)</Version>')
        if ($m.Success) { $expectedVersion = $m.Groups[1].Value.Trim() }
    }

    function Test-Candidate([string]$path) {
        if (-not $path) { return $null }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
        $leaf = Split-Path $path -Leaf
        if ($leaf -ne 'F1.exe') { return $null }
        try {
            $head = [IO.File]::ReadAllBytes($path)[0..1]
            if ($head[0] -ne 0x4D -or $head[1] -ne 0x5A) { return $null }   # 'MZ'
            $info = [Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path $path).Path)
            if ($info.ProductName -ne 'F1') { return $null }
            if (!$info.ProductVersion -or ($expectedVersion -and
                ($info.ProductVersion.Split('+')[0] -ne $expectedVersion))) { return $null }
            $assembly = [IO.Path]::ChangeExtension($path, '.dll')
            foreach($required in @($assembly, [IO.Path]::ChangeExtension($path, '.pri'), [IO.Path]::ChangeExtension($path, '.runtimeconfig.json'))) {
                if (!(Test-Path -LiteralPath $required -PathType Leaf)) { return $null }
            }
            $isValidation = Test-ContainsBytes ([IO.File]::ReadAllBytes($assembly)) $validationMarkerUtf16
            if ($isValidation -ne [bool]$RequireValidationMarker) { return $null }
        } catch { return $null }
        [pscustomobject]@{
            Path = (Resolve-Path $path).Path
            ProductVersion = $info.ProductVersion
            LastWrite = (Get-Item $assembly).LastWriteTimeUtc
            Bytes = (Get-Item $path).Length
        }
    }

    if ($AppPath) {
        $explicit = Test-Candidate $AppPath
        if (-not $explicit) { throw "Explicit -AppPath is not a usable F1 build: $AppPath" }
        if (-not $Quiet) { Write-Host "  artifact (explicit): $($explicit.Path)" }
        return $explicit
    }

    # Rank by F1.dll timestamp, not the apphost timestamp (the stub can be reused across builds).
    $canonical = Join-Path $Repo 'artifacts\publish\App'
    $otherRoots = @(
        (Join-Path $Repo 'artifacts\current'),
        (Join-Path $Repo 'src\SystemCore.App\bin')
    )

    $roots = @($canonical) + $otherRoots
    $candidates = @()
    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }
        $candidates += Get-ChildItem -LiteralPath $root -Recurse -Filter 'F1.exe' -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\obj\\' }
    }
    $valid = @()
    foreach ($c in ($candidates | Sort-Object LastWriteTime -Descending)) {
        $v = Test-Candidate $c.FullName
        if ($v) { $valid += $v }
    }
    $valid = @($valid | Sort-Object @{Expression='LastWrite';Descending=$true}, Path)
    if ($valid.Count -eq 0) {
        throw ("No valid F1 build found. Searched: " + ($roots -join '; ') +
            " (expected version '$expectedVersion'" +
            $(if ($RequireValidationMarker) { ", validation marker required" } else { "" }) +
            "). Build or publish first, or pass -AppPath explicitly.")
    }
    if (-not $Quiet) {
        Write-Host "  artifact (discovered): $($valid[0].Path)"
        Write-Host "    version=$($valid[0].ProductVersion)  written=$($valid[0].LastWrite)  bytes=$($valid[0].Bytes)"
        if ($valid.Count -gt 1) { Write-Host "    ($($valid.Count - 1) other valid builds also present; newest payload wins)" }
    }
    return $valid[0]
}
