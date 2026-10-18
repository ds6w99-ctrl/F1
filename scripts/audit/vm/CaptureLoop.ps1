# Runs INSIDE the VM interactive session: captures the whole screen every N seconds.
# Used to photograph the UAC consent dialog while the broker probe waits for elevation.
param([string]$OutDir = 'C:\F1Audit\broker-shots', [int]$Seconds = 120, [int]$Interval = 8)
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
New-Item -ItemType Directory -Force $OutDir | Out-Null
$end = (Get-Date).AddSeconds($Seconds)
$i = 0
while ((Get-Date) -lt $end) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object Drawing.Bitmap $bounds.Width, $bounds.Height
    $g = [Drawing.Graphics]::FromImage($bmp)
    try { $g.CopyFromScreen($bounds.Location, [Drawing.Point]::Empty, $bounds.Size) } catch { }
    $file = Join-Path $OutDir ('screen-{0:d3}.png' -f $i)
    $bmp.Save($file, [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    $i++
    Start-Sleep $Interval
}
"captured $i frames" | Set-Content (Join-Path $OutDir 'done.txt')
