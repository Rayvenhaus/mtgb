Add-Type -AssemblyName System.Drawing

$source    = "E:\GitHub\mtgb\src\MTGB\Assets\MTGB_1024.png"
$assetsDir = "E:\GitHub\mtgb\src\MTGB\Assets\"
$img       = [System.Drawing.Image]::FromFile($source)

# ── Navy & Gold palette ───────────────────────────────────────
$bgColor     = [System.Drawing.Color]::FromArgb(255,  15,  31,  74)  # #0f1f4a
$borderColor = [System.Drawing.Color]::FromArgb(255,  60, 131, 246)  # #3c83f6
$goldColor   = [System.Drawing.Color]::FromArgb(255, 251, 189,  35)  # #fbbd23
$mutedColor  = [System.Drawing.Color]::FromArgb(255, 161, 174, 192)  # #a1aec0

# ── Square sizes ──────────────────────────────────────────────
$squares = @(
    @{w=44;  h=44;  name="MTGB_44x44.png"},
    @{w=50;  h=50;  name="MTGB_50x50.png"},
    @{w=71;  h=71;  name="MTGB_71x71.png"},
    @{w=150; h=150; name="MTGB_150x150.png"},
    @{w=310; h=310; name="MTGB_310x310.png"}
)

foreach ($s in $squares) {
    $bmp = New-Object System.Drawing.Bitmap($s.w, $s.h)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear($bgColor)

    $pen = New-Object System.Drawing.Pen($borderColor, 2)
    $g.DrawRectangle($pen, 1, 1, $s.w - 3, $s.h - 3)
    $pen.Dispose()

    $padding  = [int]($s.w * 0.12)
    $logoSize = $s.w - ($padding * 2)
    $g.DrawImage($img, $padding, $padding, $logoSize, $logoSize)

    $g.Dispose()
    $bmp.Save($assetsDir + $s.name, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Generated $($s.name)"
}

# ── Wide tiles ────────────────────────────────────────────────
$wides = @(
    @{w=310; h=150; name="MTGB_310x150.png"},
    @{w=620; h=300; name="MTGB_620x300.png"}
)

foreach ($s in $wides) {
    $bmp = New-Object System.Drawing.Bitmap($s.w, $s.h)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $g.Clear($bgColor)

    $pen = New-Object System.Drawing.Pen($borderColor, 2)
    $g.DrawRectangle($pen, 1, 1, $s.w - 3, $s.h - 3)
    $pen.Dispose()

    $padding  = [int]($s.h * 0.10)
    $logoSize = [int]($s.h * 0.70)
    $logoY    = [int](($s.h - $logoSize) / 2)

    if ($s.w -eq 310) {
        # 310x150 — logo centred only
        $logoX = [int](($s.w - $logoSize) / 2)
        $g.DrawImage($img, $logoX, $logoY, $logoSize, $logoSize)
    } else {
        # 620x300 — logo left, text right
        $g.DrawImage($img, $padding, $logoY, $logoSize, $logoSize)

        $textAreaX = $padding + $logoSize + $padding
        $textAreaW = $s.w - $textAreaX - $padding

        $titleSize = 24
        $subSize   = 13

        $fontTitle = New-Object System.Drawing.Font(
            "Segoe UI", $titleSize, [System.Drawing.FontStyle]::Bold)
        $fontSub   = New-Object System.Drawing.Font(
            "Segoe UI", $subSize, [System.Drawing.FontStyle]::Regular)
        $brushGold = New-Object System.Drawing.SolidBrush($goldColor)
        $brushMuted = New-Object System.Drawing.SolidBrush($mutedColor)

        $titleHeight = $g.MeasureString("M · T · G · B", $fontTitle).Height
        $subHeight   = $g.MeasureString("The Monitor That Goes Bing", $fontSub).Height
        $totalTextH  = $titleHeight + $subHeight + [int]($s.h * 0.06)
        $textStartY  = [int](($s.h - $totalTextH) / 2)

        $sf = New-Object System.Drawing.StringFormat
        $sf.Trimming    = [System.Drawing.StringTrimming]::None
        $sf.FormatFlags = [System.Drawing.StringFormatFlags]::NoWrap

        $titleRect = [System.Drawing.RectangleF]::new(
            $textAreaX, $textStartY, $textAreaW, $titleHeight + 4)
        $subRect   = [System.Drawing.RectangleF]::new(
            $textAreaX,
            $textStartY + $titleHeight + [int]($s.h * 0.06),
            $textAreaW,
            $subHeight + 4)

        $g.DrawString("M · T · G · B",
            $fontTitle, $brushGold, $titleRect, $sf)
        $g.DrawString("The Monitor That Goes Bing",
            $fontSub, $brushMuted, $subRect, $sf)

        $fontTitle.Dispose()
        $fontSub.Dispose()
        $brushGold.Dispose()
        $brushMuted.Dispose()
        $sf.Dispose()
    }

    $g.Dispose()
    $bmp.Save($assetsDir + $s.name, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Generated $($s.name)"
}

$img.Dispose()
Write-Host "All done. The Ministry has its tiles."