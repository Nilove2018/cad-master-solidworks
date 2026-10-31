# Makes every CAD Master icon from "CAD Master.png" and the Tabler glyphs in ui\js\icons.js:
#   icons\toolbar_<size>.png   ribbon/menu strip, 17 commands (order = SwAddin.Commands)
#   icons\main_<size>.png      command group icon (owl)
#   icons\taskpane_<size>.png  task pane tab icon (owl)
#   icons\addin_20.png         Add-ins dialog icon
#   ui\img\logo.png            panel logo (256 px)
# Sizes 20, 32, 40, 64, 96, 128 are the six SOLIDWORKS uses (100 % to 400 % scaling).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot
$tools = $PSScriptRoot
$icons = Join-Path $root 'icons'
New-Item -ItemType Directory -Force $icons, (Join-Path $root 'ui\img') | Out-Null

function Resize([System.Drawing.Image]$src, [System.Drawing.Rectangle]$from, [int]$size, [string]$out) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'HighQuality'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $size, $size), $from, 'Pixel')
    $g.Dispose(); $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}

# The logo has a white background: flood-fill it from the edges to transparent.
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System.Collections.Generic; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices;
public static class Knockout {
    public static void White(string path, int threshold) {
        Bitmap bmp;
        using (var src = new Bitmap(path)) bmp = new Bitmap(src);
        var r = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var px = new byte[data.Stride * bmp.Height];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        int w = bmp.Width, h = bmp.Height;
        var seen = new bool[w * h];
        var q = new Queue<int>();
        for (int x = 0; x < w; x++) { q.Enqueue(x); q.Enqueue((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { q.Enqueue(y * w); q.Enqueue(y * w + w - 1); }
        while (q.Count > 0) {
            int p = q.Dequeue();
            if (seen[p]) continue;
            seen[p] = true;
            int i = (p / w) * data.Stride + (p % w) * 4;
            if (px[i] < threshold || px[i + 1] < threshold || px[i + 2] < threshold) continue;
            px[i + 3] = 0;
            int x0 = p % w, y0 = p / w;
            if (x0 > 0) q.Enqueue(p - 1); if (x0 < w - 1) q.Enqueue(p + 1);
            if (y0 > 0) q.Enqueue(p - w); if (y0 < h - 1) q.Enqueue(p + w);
        }
        Marshal.Copy(px, 0, data.Scan0, px.Length);
        bmp.UnlockBits(data);
        bmp.Save(path, ImageFormat.Png);
        bmp.Dispose();
    }
}
'@
$logo = [System.Drawing.Bitmap]::FromFile((Join-Path $root 'CAD Master.png'))
$full = New-Object System.Drawing.Rectangle 0, 0, $logo.Width, $logo.Height
# Small sizes: the owl's face and the SW cube read better than the whole badge.
$crop = New-Object System.Drawing.Rectangle 330, 170, 1390, 1390
Resize $logo $full 512 (Join-Path $tools 'logo_large.png')
Resize $logo $crop 512 (Join-Path $tools 'logo_small.png')
Resize $logo $full 256 (Join-Path $root 'ui\img\logo.png')
$logo.Dispose()
[Knockout]::White((Join-Path $tools 'logo_large.png'), 236)
[Knockout]::White((Join-Path $root 'ui\img\logo.png'), 236)

# Render the icon sheet with headless Edge on a transparent background.
$edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
$sheet = Join-Path $env:TEMP 'cadmaster_icon_sheet.png'
$profile = Join-Path $env:TEMP 'cadmaster_edge_profile'
$url = 'file:///' + ((Join-Path $tools 'icon_sheet.html') -replace '\\', '/' -replace ' ', '%20')
if (Test-Path $sheet) { Remove-Item $sheet }
$a = "--headless=new --disable-gpu --hide-scrollbars --no-first-run --default-background-color=00000000 --user-data-dir=`"$profile`" --window-size=2200,420 --force-device-scale-factor=1 --virtual-time-budget=5000 --screenshot=`"$sheet`" `"$url`""
Start-Process -FilePath $edge -ArgumentList $a -Wait -WindowStyle Hidden
if (-not (Test-Path $sheet)) { throw 'Edge did not render the icon sheet' }

$img = [System.Drawing.Bitmap]::FromFile($sheet)
$y = 0
foreach ($s in 20, 32, 40, 64, 96, 128) {
    $strip = $img.Clone((New-Object System.Drawing.Rectangle 0, $y, (17 * $s), $s), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $strip.Save((Join-Path $icons "toolbar_$s.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $one = $img.Clone((New-Object System.Drawing.Rectangle 0, $y, $s, $s), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $one.Save((Join-Path $icons "main_$s.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $one.Save((Join-Path $icons "taskpane_$s.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    if ($s -eq 20) { $one.Save((Join-Path $icons 'addin_20.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $strip.Dispose(); $one.Dispose()
    $y += $s + 8
}
$img.Dispose()
Remove-Item (Join-Path $tools 'logo_large.png'), (Join-Path $tools 'logo_small.png')
Get-ChildItem $icons | ForEach-Object { '{0,-20} {1}' -f $_.Name, ([System.Drawing.Image]::FromFile($_.FullName) | ForEach-Object { "$($_.Width)x$($_.Height)"; $_.Dispose() }) }
