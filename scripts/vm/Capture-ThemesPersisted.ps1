param([string]$AppPath = 'G:\F1\artifacts\publish\App\F1.exe', [string]$OutDir = 'G:\F1\artifacts\f1-ui\local-validation\backgrounds')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public static class WC { [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Ri,B; }
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); }
"@
New-Item -ItemType Directory -Force $OutDir | Out-Null
function Find($win, $id) { $win.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, $id))) }
function Snap($proc, [string]$name) {
    $null = [WC]::SetForegroundWindow($proc.MainWindowHandle); Start-Sleep -Milliseconds 900
    $r = New-Object WC+R; $null = [WC]::GetWindowRect($proc.MainWindowHandle, [ref]$r)
    $bmp = New-Object Drawing.Bitmap(($r.Ri - $r.L), ($r.B - $r.T))
    $g = [Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
    try { $null = [WC]::PrintWindow($proc.MainWindowHandle, $dc, 2) } finally { $g.ReleaseHdc($dc) }
    if ([WC]::GetForegroundWindow() -eq $proc.MainWindowHandle) { $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size) }
    $bmp.Save((Join-Path $OutDir "$name.png"), [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
}
$log = Join-Path $OutDir 'themes.txt'; Remove-Item $log -ErrorAction SilentlyContinue
foreach ($theme in @('Light', 'Dark')) {
    # Persist the theme before launch: the app reads it at startup and applies it in Apply().
    $stateFile = Join-Path $env:LOCALAPPDATA 'SystemCore\preferences.json'
    if (Test-Path $stateFile) {
        $json = Get-Content $stateFile -Raw | ConvertFrom-Json
        $prop = $json.PSObject.Properties.Name | Where-Object { $_ -match 'Theme' } | Select-Object -First 1
        if ($prop) { $json.$prop = $theme; $json | ConvertTo-Json -Depth 6 | Set-Content $stateFile -Encoding utf8; "theme property '$prop' set to $theme" | Out-File $log -Append }
        else { "no theme property; keys: $($json.PSObject.Properties.Name -join ',')" | Out-File $log -Append }
    }
    $p = Start-Process $AppPath -PassThru
    $sw = [Diagnostics.Stopwatch]::StartNew()
    do { Start-Sleep -Milliseconds 400; $p.Refresh() } while ($p.MainWindowHandle -eq [IntPtr]::Zero -and $sw.Elapsed.TotalSeconds -lt 40)
    Start-Sleep -Seconds 5
    Snap $p ("final-" + $theme.ToLower())
    "captured theme $theme" | Out-File $log -Append
    $null = $p.CloseMainWindow(); $null = $p.WaitForExit(10000); if (-not $p.HasExited) { $p.Kill() }
}
Get-Content $log
