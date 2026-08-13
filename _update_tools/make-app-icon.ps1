# Builds the real multi-resolution SO.ico for candidate N.
#
# Every size is DRAWN at that size, not scaled down from one large image. That is
# the lesson the tray icons taught this project: fixed art resampled by the shell
# looks soft, and the shell asks for 16/20/24/32 depending on DPI and where the
# icon appears.
#
# Layout is the conventional one: 32-bit BMP entries for everything up to 128, and
# a PNG entry for 256. PNG-in-ICO is supported from Vista onwards, but BMP for the
# small sizes is what every shell path has always understood.
Add-Type -AssemblyName System.Drawing

$Deep  = [System.Drawing.Color]::FromArgb(0x1B,0x4F,0x91)
$Amber = [System.Drawing.Color]::FromArgb(0xE8,0xB7,0x50)
$White = [System.Drawing.Color]::White

function New-Rounded([single]$x,[single]$y,[single]$w,[single]$h,[single]$r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  if ($r*2 -gt $h) { $r = $h/2 }
  if ($r*2 -gt $w) { $r = $w/2 }
  $d = $r*2
  $p.AddArc($x,$y,$d,$d,180,90); $p.AddArc($x+$w-$d,$y,$d,$d,270,90)
  $p.AddArc($x+$w-$d,$y+$h-$d,$d,$d,0,90); $p.AddArc($x,$y+$h-$d,$d,$d,90,90)
  $p.CloseFigure(); return $p
}

function New-Icon([int]$s) {
  $bmp = New-Object System.Drawing.Bitmap($s,$s,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.Clear([System.Drawing.Color]::Transparent)

  $tile = New-Rounded 0.5 0.5 ($s-1) ($s-1) ($s*0.235)
  $tb = New-Object System.Drawing.SolidBrush($Deep)
  $g.FillPath($tb,$tile)

  $h = $s*0.145
  $b1 = New-Object System.Drawing.SolidBrush($White)
  $b2 = New-Object System.Drawing.SolidBrush($Amber)
  $p1 = New-Rounded ($s*0.19) ($s*0.31) ($s*0.62) $h ($h/2)
  $p2 = New-Rounded ($s*0.19) ($s*0.55) ($s*0.36) $h ($h/2)
  $g.FillPath($b1,$p1); $g.FillPath($b2,$p2)

  $tb.Dispose(); $b1.Dispose(); $b2.Dispose()
  $tile.Dispose(); $p1.Dispose(); $p2.Dispose(); $g.Dispose()
  return $bmp
}

# --- ICO assembly -----------------------------------------------------------
function Get-BmpEntry([System.Drawing.Bitmap]$bmp) {
  $w = $bmp.Width; $h = $bmp.Height
  $ms = New-Object System.IO.MemoryStream
  $bw = New-Object System.IO.BinaryWriter($ms)

  # BITMAPINFOHEADER. Height is DOUBLED: an ICO's BMP holds the colour bitmap and
  # the AND mask stacked, and the header describes both.
  $bw.Write([uint32]40); $bw.Write([int32]$w); $bw.Write([int32]($h*2))
  $bw.Write([uint16]1);  $bw.Write([uint16]32)
  $bw.Write([uint32]0);  $bw.Write([uint32]($w*$h*4))
  $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)

  # Colour data, BOTTOM-UP, BGRA.
  for ($y = $h-1; $y -ge 0; $y--) {
    for ($x = 0; $x -lt $w; $x++) {
      $c = $bmp.GetPixel($x,$y)
      $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
    }
  }
  # AND mask: all zero (the alpha channel does the work), rows padded to 4 bytes.
  $rowBytes = [math]::Floor(($w + 31) / 32) * 4
  for ($y = 0; $y -lt $h; $y++) { $bw.Write((New-Object byte[] $rowBytes)) }

  $bw.Flush()
  $bytes = $ms.ToArray()
  $bw.Dispose(); $ms.Dispose()
  # Comma, or PowerShell unrolls the array on output and the caller gets an
  # object[] of bytes - which BinaryWriter has no overload for, and the file comes
  # out as a header with no image data in it.
  return ,$bytes
}

function Get-PngEntry([System.Drawing.Bitmap]$bmp) {
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $bytes = $ms.ToArray(); $ms.Dispose(); return ,$bytes
}

$sizes = 16,20,24,32,48,64,128,256
$images = @()
foreach ($s in $sizes) {
  $bmp = New-Icon $s
  $data = if ($s -ge 256) { Get-PngEntry $bmp } else { Get-BmpEntry $bmp }
  $images += ,@{ Size = $s; Data = $data }
  $bmp.Dispose()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)   # ICONDIR

$offset = 6 + (16 * $images.Count)
foreach ($img in $images) {
  $s = $img.Size
  $w.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))   # 0 means 256
  $w.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
  $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]$img.Data.Length)
  $w.Write([uint32]$offset)
  $offset += $img.Data.Length
}
foreach ($img in $images) { $w.Write([byte[]]$img.Data) }
$w.Flush()

$target = 'A:\SO_Claude\src\SO.ico'
[System.IO.File]::WriteAllBytes($target, $out.ToArray())
$w.Dispose(); $out.Dispose()

$len = (Get-Item $target).Length
"written: $target  ($([math]::Round($len/1KB,1)) KB, $($images.Count) sizes: $($sizes -join ', '))"

# It has to be a header PLUS data. The first attempt wrote 134 bytes - a perfectly
# valid, perfectly empty icon - and reported success.
$expected = 6 + (16*$images.Count)
if ($len -le $expected + 1024) { throw "ICO is only $len bytes: the image data did not make it in." }

# And it has to LOAD, at the sizes the shell will ask for.
foreach ($s in 16,32,48,256) {
  $ico = New-Object System.Drawing.Icon($target, $s, $s)
  "  loads at ${s}px -> actual $($ico.Width)x$($ico.Height)"
  $ico.Dispose()
}
