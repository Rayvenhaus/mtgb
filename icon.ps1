Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName "System.Drawing.Common"

Add-Type -ReferencedAssemblies @(
    [System.Drawing.Graphics].Assembly.Location,
    [System.Drawing.Drawing2D.GraphicsPath].Assembly.Location
) -Language CSharp @"
using System.Drawing;
using System.Drawing.Drawing2D;
public static class GfxExt {
    public static void RoundRect(
        this GraphicsPath path, int x, int y, int w, int h, int r) {
        int d = r * 2;
        path.AddArc(x,     y,     d, d, 180, 90);
        path.AddArc(x+w-d, y,     d, d, 270, 90);
        path.AddArc(x+w-d, y+h-d, d, d,   0, 90);
        path.AddArc(x,     y+h-d, d, d,  90, 90);
        path.CloseFigure();
    }
}
"@

$assetsDir = "E:\GitHub\mtgb\src\MTGB\Assets\"

$colBody   = [System.Drawing.Color]::FromArgb(255,  30,  59, 138)
$colScreen = [System.Drawing.Color]::FromArgb(255,  10,  21,  48)
$colBorder = [System.Drawing.Color]::FromArgb(255,  60, 131, 246)
$colStand  = [System.Drawing.Color]::FromArgb(255,  44,  77, 186)
$colGold   = [System.Drawing.Color]::FromArgb(255, 251, 189,  35)

function Draw-Icon([int]$sz) {
    $bmp = New-Object System.Drawing.Bitmap($sz, $sz)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $sc = $sz / 256.0

    # ── Monitor shell ─────────────────────────────────────────
    $mX = [int](24  * $sc); $mY = [int](20  * $sc)
    $mW = [int](208 * $sc); $mH = [int](160 * $sc)
    $mR = [Math]::Max(2, [int](12 * $sc))

    $bodyBrush = New-Object System.Drawing.SolidBrush($colBody)
    $borderPen = New-Object System.Drawing.Pen($colBorder, [Math]::Max(1.0, 4.0 * $sc))
    $borderPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $shellPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    [GfxExt]::RoundRect($shellPath, $mX, $mY, $mW, $mH, $mR)
    $g.FillPath($bodyBrush, $shellPath)
    $g.DrawPath($borderPen, $shellPath)

    # ── Accent bar top ────────────────────────────────────────
    if ($sz -ge 32) {
        $abH = [Math]::Max(2, [int](10 * $sc))
        $abBrush = New-Object System.Drawing.SolidBrush($colBorder)
        $abPath  = New-Object System.Drawing.Drawing2D.GraphicsPath
        [GfxExt]::RoundRect($abPath, $mX, $mY, $mW, $abH, $mR)
        $g.FillPath($abBrush, $abPath)
        $abBrush.Dispose(); $abPath.Dispose()
    }

    # ── Screen ────────────────────────────────────────────────
    $sX = $mX + [int](12 * $sc); $sY = $mY + [int](18 * $sc)
    $sW = $mW - [int](24 * $sc); $sH = $mH - [int](36 * $sc)
    $sR = [Math]::Max(1, [int](4 * $sc))

    $screenBrush = New-Object System.Drawing.SolidBrush($colScreen)
    $screenPath  = New-Object System.Drawing.Drawing2D.GraphicsPath
    [GfxExt]::RoundRect($screenPath, $sX, $sY, $sW, $sH, $sR)
    $g.FillPath($screenBrush, $screenPath)

    # ── Pulse line ────────────────────────────────────────────
    $midY    = $sY + $sH / 2.0
    $pw      = [float]$sW
    $pulsePen = New-Object System.Drawing.Pen($colGold, [Math]::Max(1.5, 2.5 * $sc))
    $pulsePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pulsePen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $pulsePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $p1x = $sX + $pw * 0.05
    $p2x = $sX + $pw * 0.25
    $p3x = $sX + $pw * 0.38
    $p4x = $sX + $pw * 0.52
    $p5x = $sX + $pw * 0.62
    $p6x = $sX + $pw * 0.72   # bing peak
    $p7x = $sX + $pw * 0.80
    $p8x = $sX + $pw * 0.95

    $rH = $sH * 0.38
    $fH = $sH * 0.38
    $bH = $sH * 0.30

    $pts = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($p1x, $midY),
        [System.Drawing.PointF]::new($p2x, $midY),
        [System.Drawing.PointF]::new($p3x, $midY - $rH),
        [System.Drawing.PointF]::new($p4x, $midY + $fH),
        [System.Drawing.PointF]::new($p5x, $midY),
        [System.Drawing.PointF]::new($p6x, $midY - $bH),
        [System.Drawing.PointF]::new($p7x, $midY),
        [System.Drawing.PointF]::new($p8x, $midY)
    )
    $g.DrawLines($pulsePen, $pts)

    # ── Bing dot ──────────────────────────────────────────────
    $dotR     = [Math]::Max(2.0, 8.0 * $sc)
    $dotBrush = New-Object System.Drawing.SolidBrush($colGold)
    $dotX     = [float]$p6x - $dotR
    $dotY     = ($midY - $bH) - $dotR
    $g.FillEllipse($dotBrush, $dotX, $dotY, $dotR * 2, $dotR * 2)

    # ── Stand (48px and above) ────────────────────────────────
    if ($sz -ge 48) {
        $standBrush = New-Object System.Drawing.SolidBrush($colStand)
        $standPen   = New-Object System.Drawing.Pen($colBorder, [Math]::Max(1.0, 1.5 * $sc))

        $nkW = [int](28 * $sc); $nkH = [int](18 * $sc)
        $nkX = $mX + [int]($mW / 2) - [int]($nkW / 2)
        $nkY = $mY + $mH
        $g.FillRectangle($standBrush, $nkX, $nkY, $nkW, $nkH)
        $g.DrawRectangle($standPen,   $nkX, $nkY, $nkW, $nkH)

        $bsW = [int](72 * $sc); $bsH = [int](14 * $sc)
        $bsX = $mX + [int]($mW / 2) - [int]($bsW / 2)
        $bsY = $nkY + $nkH
        $bsR = [Math]::Max(1, [int](6 * $sc))
        $bsPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        [GfxExt]::RoundRect($bsPath, $bsX, $bsY, $bsW, $bsH, $bsR)
        $g.FillPath($standBrush, $bsPath)
        $g.DrawPath($standPen,   $bsPath)

        $standBrush.Dispose(); $standPen.Dispose(); $bsPath.Dispose()
    }

    # ── Corner brackets (64px and above) ─────────────────────
    if ($sz -ge 64) {
        $bLen = [int](16 * $sc)
        $bPen = New-Object System.Drawing.Pen($colGold, [Math]::Max(1.0, 2.0 * $sc))
        $bx1  = $mX + [int](4 * $sc); $bx2 = $mX + $mW - [int](4 * $sc)
        $by1  = $mY + [int](4 * $sc); $by2 = $mY + $mH - [int](4 * $sc)

        $g.DrawLine($bPen, $bx1, $by1, $bx1 + $bLen, $by1)
        $g.DrawLine($bPen, $bx1, $by1, $bx1, $by1 + $bLen)
        $g.DrawLine($bPen, $bx2, $by1, $bx2 - $bLen, $by1)
        $g.DrawLine($bPen, $bx2, $by1, $bx2, $by1 + $bLen)
        $g.DrawLine($bPen, $bx1, $by2, $bx1 + $bLen, $by2)
        $g.DrawLine($bPen, $bx1, $by2, $bx1, $by2 - $bLen)
        $g.DrawLine($bPen, $bx2, $by2, $bx2 - $bLen, $by2)
        $g.DrawLine($bPen, $bx2, $by2, $bx2, $by2 - $bLen)
        $bPen.Dispose()
    }

    $bodyBrush.Dispose(); $borderPen.Dispose()
    $screenBrush.Dispose(); $screenPath.Dispose()
    $pulsePen.Dispose(); $dotBrush.Dispose()
    $shellPath.Dispose(); $g.Dispose()

    return $bmp
}

# ── Generate PNGs ─────────────────────────────────────────────
foreach ($sz in @(16, 32, 48, 128, 256)) {
    $bmp  = Draw-Icon $sz
    $path = "${assetsDir}MTGB_${sz}.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Generated MTGB_${sz}.png"
}

# ── Generate .ico ─────────────────────────────────────────────
$icoSizes = @(16, 32, 48)
$icoPath  = "${assetsDir}mtgb.ico"

$pngBytes = @()
foreach ($sz in $icoSizes) {
    $bmp = Draw-Icon $sz
    $ms  = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes += , $ms.ToArray()
    $ms.Dispose(); $bmp.Dispose()
}

$fs     = [System.IO.File]::OpenWrite($icoPath)
$writer = New-Object System.IO.BinaryWriter($fs)

$count  = $icoSizes.Count
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$count)

$offset = 6 + ($count * 16)
for ($i = 0; $i -lt $count; $i++) {
    $sz = $icoSizes[$i]
    $writer.Write([byte]$sz)
    $writer.Write([byte]$sz)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$pngBytes[$i].Length)
    $writer.Write([uint32]$offset)
    $offset += $pngBytes[$i].Length
}
foreach ($bytes in $pngBytes) { $writer.Write($bytes) }

# ── Generate 1024px source for tile assets ────────────────────
$bmp  = Draw-Icon 1024
$path = "${assetsDir}MTGB_1024.png"
$bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "Generated MTGB_1024.png"


$writer.Dispose(); $fs.Dispose()
Write-Host "Generated mtgb.ico"

Write-Host ""
Write-Host "============================================"
Write-Host " MTGB has a new face. It goes BING."
Write-Host "============================================"