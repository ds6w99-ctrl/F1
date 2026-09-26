param([string]$Screenshot='artifacts/f1-ui/screenshots/ar-Dark-Dashboard-reference.png')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @'
using System;using System.Drawing;using System.Drawing.Imaging;
public static class F1Comparison {
  public static void Make(string reference,string actual,string destination) {
  using(var r=Image.FromFile(reference))using(var raw=new Bitmap(actual))
  // The 96-DPI capture includes 8px invisible resize gutters on both sides and
  // below. Crop only those gutters, preserving the full visible frame/content.
  using(var a=raw.Clone(new Rectangle(8,0,raw.Width-16,raw.Height-8),PixelFormat.Format32bppArgb))
  using(var referenceCrop=new Bitmap(a.Width,a.Height))using(var side=new Bitmap(a.Width*2,a.Height))
  using(var overlay=new Bitmap(a.Width,a.Height))using(var difference=new Bitmap(a.Width,a.Height)) {
   using(var g=Graphics.FromImage(referenceCrop)){g.DrawImage(r,new Rectangle(0,0,a.Width,a.Height),new Rectangle(44,49,1360,1000),GraphicsUnit.Pixel);}
    referenceCrop.Save(destination+"/reference-frame-normalized.png",ImageFormat.Png);
    a.Save(destination+"/runtime-frame-cropped.png",ImageFormat.Png);
   using(var g=Graphics.FromImage(side)){g.DrawImageUnscaled(referenceCrop,0,0);g.DrawImageUnscaled(a,a.Width,0);}
   side.Save(destination+"/side-by-side.png",ImageFormat.Png);
   using(var actualBitmap=new Bitmap(a)){
    for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++){
     var p=referenceCrop.GetPixel(x,y);var q=actualBitmap.GetPixel(x,y);
     overlay.SetPixel(x,y,Color.FromArgb((p.R+q.R)/2,(p.G+q.G)/2,(p.B+q.B)/2));
     difference.SetPixel(x,y,Color.FromArgb(Math.Abs(p.R-q.R),Math.Abs(p.G-q.G),Math.Abs(p.B-q.B)));
    }
   }
   overlay.Save(destination+"/overlay-50-percent.png",ImageFormat.Png);
   difference.Save(destination+"/difference.png",ImageFormat.Png);
  }
 }
}
'@
$destination=[IO.Path]::GetFullPath('artifacts/f1-ui/comparisons');[IO.Directory]::CreateDirectory($destination)|Out-Null
$actual=[Drawing.Image]::FromFile((Resolve-Path $Screenshot))
try{if($actual.Width -ne 1360 -or $actual.Height -ne 1000){throw 'Comparison crop requires the documented 1360x1000 reference-size capture'}}finally{$actual.Dispose()}
[F1Comparison]::Make((Resolve-Path docs/design/reference/f1-approved-reference.png).Path,(Resolve-Path $Screenshot).Path,$destination)
[pscustomobject]@{ReferenceCrop=@{X=44;Y=49;Width=1360;Height=1000};ReferenceSha256=(Get-FileHash docs/design/reference/f1-approved-reference.png).Hash;Runtime=$Screenshot;RuntimeSha256=(Get-FileHash $Screenshot).Hash;RuntimeCrop=@{X=8;Y=0;Width=1344;Height=992};OverlayAlpha=0.5;Masks='None';Normalization='Reference frame resized to the runtime visible frame. Only invisible Windows resize gutters removed; no controls, spacing, text, cards or assets masked. Raw screenshots and client/DPI metadata retained.';Metric='No similarity score claimed; changing values/fonts/assets remain visible in the diff.'}|ConvertTo-Json -Depth 4|Set-Content "$destination/method.json" -Encoding utf8
