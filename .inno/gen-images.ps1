# ============================================================================
# PaDDY Inno Setup - Wizard Image Generator
# Generates wizard-sidebar.bmp (164x314) and wizard-small.bmp (55x55)
# matching the PaDDY white / green-accent installer theme.
#
# Run once (or whenever branding changes):
#   powershell -ExecutionPolicy Bypass -File .inno\gen-images.ps1
# ============================================================================
Add-Type -AssemblyName System.Drawing

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# -- PaDDY palette ------------------------------------------------------------
$clBg      = [System.Drawing.Color]::FromArgb(0xF7, 0xF8, 0xFA)  # #F7F8FA  window bg
$clPanel   = [System.Drawing.Color]::FromArgb(0xEC, 0xEF, 0xF3)  # #ECEFF3  panel lift
$clGreen   = [System.Drawing.Color]::FromArgb(0x4C, 0xAF, 0x50)  # #4CAF50  accent green
$clMuted   = [System.Drawing.Color]::FromArgb(0x2B, 0x39, 0x48)  # #2B3948  primary text
$clSubtle  = [System.Drawing.Color]::FromArgb(0x5D, 0x6B, 0x78)  # #5D6B78  secondary text
$clGreenDim= [System.Drawing.Color]::FromArgb(0xA8, 0xB3, 0xBF)  # #A8B3BF  separator

# ── Helper: create Graphics with anti-aliasing ───────────────────────────────
function New-Canvas($width, $height) {
    $bmp = New-Object System.Drawing.Bitmap($width, $height,
           [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode        = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint    = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $g.InterpolationMode    = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    return $bmp, $g
}

# ═══════════════════════════════════════════════════════════════════════════════
# 1.  wizard-sidebar.bmp   164 × 314
# ═══════════════════════════════════════════════════════════════════════════════
$W = 164; $H = 314
$bmp, $g = New-Canvas $W $H

# Background
$g.Clear($clBg)

# Thin green accent bar across the very top
$barBrush = New-Object System.Drawing.SolidBrush($clGreen)
$g.FillRectangle($barBrush, 0, 0, $W, 3)
$barBrush.Dispose()

# Gradient glow from top: semi-transparent green fading to background
$glowTop    = New-Object System.Drawing.PointF(0, 0)
$glowBot    = New-Object System.Drawing.PointF(0, 80)
$glowBrush  = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $glowTop, $glowBot,
    [System.Drawing.Color]::FromArgb(28, 0x4C, 0xAF, 0x50),
    [System.Drawing.Color]::FromArgb(0,  0xF7, 0xF8, 0xFA))
$g.FillRectangle($glowBrush, 0, 0, $W, 80)
$glowBrush.Dispose()

# Bottom gradient (subtle panel lift)
$bottomY    = $H - 60
$btTop      = New-Object System.Drawing.PointF(0, [float]$bottomY)
$btBot      = New-Object System.Drawing.PointF(0, [float]$H)
$bottomBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $btTop, $btBot,
    [System.Drawing.Color]::FromArgb(0,  0xF7, 0xF8, 0xFA),
    [System.Drawing.Color]::FromArgb(35, 0xEC, 0xEF, 0xF3))
$g.FillRectangle($bottomBrush, 0, $bottomY, $W, 60)
$bottomBrush.Dispose()

# ── Logo "PaDDY" ─────────────────────────────────────────────────────────────
$fLogo = New-Object System.Drawing.Font("Segoe UI", 30, [System.Drawing.FontStyle]::Bold,
         [System.Drawing.GraphicsUnit]::Point)

$brushMuted  = New-Object System.Drawing.SolidBrush($clMuted)
$brushGreen  = New-Object System.Drawing.SolidBrush($clGreen)
$logoText    = "PaDDY"

$sf = New-Object System.Drawing.StringFormat([System.Drawing.StringFormat]::GenericTypographic)
$sf.Trimming = [System.Drawing.StringTrimming]::None
$sf.FormatFlags = $sf.FormatFlags -bor [System.Drawing.StringFormatFlags]::MeasureTrailingSpaces

$ranges = [System.Drawing.CharacterRange[]]@(
    (New-Object System.Drawing.CharacterRange(0, 2)),
    (New-Object System.Drawing.CharacterRange(2, 3))
)
$sf.SetMeasurableCharacterRanges($ranges)

$logoSize = $g.MeasureString($logoText, $fLogo, [int]::MaxValue, $sf)
$logoX  = ($W - $logoSize.Width) / 2
$logoY  = [int](($H / 2) - ($logoSize.Height / 2) - 10)   # slightly above center

$layoutRect = New-Object System.Drawing.RectangleF([float]$logoX, [float]$logoY, 1000.0, 200.0)
$regions = $g.MeasureCharacterRanges($logoText, $fLogo, $layoutRect, $sf)
$paBounds = $regions[0].GetBounds($g)
$splitX = [float]$paBounds.Right

$g.DrawString($logoText, $fLogo, $brushMuted, [float]$logoX, [float]$logoY, $sf)
$savedState = $g.Save()
$clipRect = New-Object System.Drawing.RectangleF($splitX, 0.0, [float]($W - $splitX), [float]$H)
$g.SetClip($clipRect)
$g.DrawString($logoText, $fLogo, $brushGreen, [float]$logoX, [float]$logoY, $sf)
$g.Restore($savedState)

foreach ($region in $regions) { $region.Dispose() }
$fLogo.Dispose(); $brushMuted.Dispose(); $brushGreen.Dispose(); $sf.Dispose()

# ── Tagline ───────────────────────────────────────────────────────────────────
$fTag      = New-Object System.Drawing.Font("Segoe UI", 8.5, [System.Drawing.FontStyle]::Regular,
             [System.Drawing.GraphicsUnit]::Point)
$brushSub  = New-Object System.Drawing.SolidBrush($clSubtle)
$tagText   = "audio pad recorder"
$szTag     = $g.MeasureString($tagText, $fTag)
$tagX      = ($W - $szTag.Width) / 2
$tagY      = $logoY + $logoSize.Height + 4
$g.DrawString($tagText, $fTag, $brushSub, [float]$tagX, [float]$tagY)
$fTag.Dispose(); $brushSub.Dispose()

# -- Thin separator under tagline ---------------------------------------------
$sepY  = [int]($tagY + $szTag.Height + 10)
$sepPen = New-Object System.Drawing.Pen(
    [System.Drawing.Color]::FromArgb(70, 0xA8, 0xB3, 0xBF), 1)
$g.DrawLine($sepPen, 24, $sepY, $W - 24, $sepY)
$sepPen.Dispose()

# -- Small version text at bottom ---------------------------------------------
$fVer     = New-Object System.Drawing.Font("Segoe UI", 7.5, [System.Drawing.FontStyle]::Regular,
            [System.Drawing.GraphicsUnit]::Point)
$brushVer = New-Object System.Drawing.SolidBrush(
            [System.Drawing.Color]::FromArgb(0x75, 0x84, 0x90))
$verText  = "NoID Softwork"
$szVer    = $g.MeasureString($verText, $fVer)
$g.DrawString($verText, $fVer, $brushVer,
    [float](($W - $szVer.Width) / 2), [float]($H - $szVer.Height - 12))
$fVer.Dispose(); $brushVer.Dispose()

# Save
$g.Dispose()
$sidebarPath = Join-Path $scriptDir "wizard-sidebar.bmp"
$bmp.Save($sidebarPath, [System.Drawing.Imaging.ImageFormat]::Bmp)
$bmp.Dispose()
Write-Host "Saved: $sidebarPath"

# ═══════════════════════════════════════════════════════════════════════════════
# 2.  wizard-small.bmp   55 × 55
# ═══════════════════════════════════════════════════════════════════════════════
$SW = 55; $SH = 55
$smallLeftBarWidth = 3
$smallTopBarHeight = 2
$bmp2, $g2 = New-Canvas $SW $SH

$g2.Clear($clBg)

# Green accent bar on the left edge
$barB2 = New-Object System.Drawing.SolidBrush($clGreen)
$g2.FillRectangle($barB2, 0, 0, $smallLeftBarWidth, $SH)
$barB2.Dispose()

# Thin top bar
$barB3 = New-Object System.Drawing.SolidBrush($clGreen)
$g2.FillRectangle($barB3, 0, 0, $SW, $smallTopBarHeight)
$barB3.Dispose()

# "PaDDY" two-tone logo — compact, inside the small square
$fSmLogo = New-Object System.Drawing.Font("Segoe UI", 13, [System.Drawing.FontStyle]::Bold,
           [System.Drawing.GraphicsUnit]::Point)
$sfSm    = New-Object System.Drawing.StringFormat([System.Drawing.StringFormat]::GenericTypographic)
$sfSm.Trimming = [System.Drawing.StringTrimming]::None
$sfSm.FormatFlags = $sfSm.FormatFlags -bor [System.Drawing.StringFormatFlags]::MeasureTrailingSpaces

$bMuted2  = New-Object System.Drawing.SolidBrush($clMuted)
$bGreen2  = New-Object System.Drawing.SolidBrush($clGreen)

$logoText2 = "PaDDY"
$ranges2 = [System.Drawing.CharacterRange[]]@(
    (New-Object System.Drawing.CharacterRange(0, 2)),
    (New-Object System.Drawing.CharacterRange(2, 3))
)
$sfSm.SetMeasurableCharacterRanges($ranges2)

$logoSize2 = $g2.MeasureString($logoText2, $fSmLogo, [int]::MaxValue, $sfSm)
$lx2      = $smallLeftBarWidth + (($SW - $smallLeftBarWidth) - $logoSize2.Width) / 2
$ly2      = $smallTopBarHeight + (($SH - $smallTopBarHeight) - $logoSize2.Height) / 2

$layoutRect2 = New-Object System.Drawing.RectangleF([float]$lx2, [float]$ly2, 300.0, 120.0)
$regions2 = $g2.MeasureCharacterRanges($logoText2, $fSmLogo, $layoutRect2, $sfSm)
$paBounds2 = $regions2[0].GetBounds($g2)
$splitX2 = [float]$paBounds2.Right

$g2.DrawString($logoText2, $fSmLogo, $bMuted2, [float]$lx2, [float]$ly2, $sfSm)
$savedState2 = $g2.Save()
$clipRect2 = New-Object System.Drawing.RectangleF($splitX2, 0.0, [float]($SW - $splitX2), [float]$SH)
$g2.SetClip($clipRect2)
$g2.DrawString($logoText2, $fSmLogo, $bGreen2, [float]$lx2, [float]$ly2, $sfSm)
$g2.Restore($savedState2)

foreach ($region in $regions2) { $region.Dispose() }

$fSmLogo.Dispose(); $bMuted2.Dispose(); $bGreen2.Dispose(); $sfSm.Dispose()
$g2.Dispose()

$smallPath = Join-Path $scriptDir "wizard-small.bmp"
$bmp2.Save($smallPath, [System.Drawing.Imaging.ImageFormat]::Bmp)
$bmp2.Dispose()
Write-Host "Saved: $smallPath"

Write-Host ""
Write-Host "Done. Both wizard images generated successfully."
