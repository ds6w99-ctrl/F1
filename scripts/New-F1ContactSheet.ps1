param([switch]$InitialStates)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$pages=@('Dashboard','SystemInfo','Performance','MemoryTools','Cleaner','Uninstaller','Startup','Tools','Settings','About','Confirmation','ThemePopup')
foreach($variant in @('ar-Dark','en-Light')){
 $populated=!$InitialStates -and (Test-Path 'artifacts/f1-ui/screenshots-populated/metadata.json')
 $sheet=[Drawing.Bitmap]::new(1440,1536);$g=[Drawing.Graphics]::FromImage($sheet);$font=[Drawing.Font]::new('Segoe UI',14)
 try{
  $g.Clear([Drawing.Color]::FromArgb(3,11,21))
  for($i=0;$i -lt $pages.Count;$i++){
   $file="artifacts/f1-ui/screenshots/$variant-$($pages[$i]).png"
   if($populated){
    $suffix=switch($pages[$i]){'Cleaner'{'scanned'} 'Uninstaller'{'loaded'} 'Startup'{'loaded'} 'Performance'{'history'} 'MemoryTools'{'history'} default{''}}
    if($suffix){$file="artifacts/f1-ui/screenshots-populated/$variant-$($pages[$i])-$suffix.png"}
   }
   $image=[Drawing.Image]::FromFile((Resolve-Path $file));$x=($i%3)*480;$y=[Math]::Floor($i/3)*384
   try{$g.DrawString("$variant / $($pages[$i])",$font,[Drawing.Brushes]::White,$x+8,$y+4);$g.DrawImage($image,[Drawing.Rectangle]::new($x,$y+30,480,353))}finally{$image.Dispose()}
  }
  $suffix=if($populated){'-populated'}else{''}
  $sheet.Save([IO.Path]::GetFullPath("artifacts/f1-ui/comparisons/contact-$variant$suffix.png"),[Drawing.Imaging.ImageFormat]::Png)
 }finally{$font.Dispose();$g.Dispose();$sheet.Dispose()}
}
