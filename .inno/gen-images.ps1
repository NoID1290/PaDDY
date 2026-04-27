# ============================================================================
# PaDDY Inno Setup — Wizard Image Generator
# Generates wizard-sidebar.bmp (164x314) and wizard-small.bmp (55x55)
# matching the PaDDY dark-navy / green-accent theme.
#
# Run once (or whenever branding changes):
#   powershell -ExecutionPolicy Bypass -File .inno\gen-images.ps1
# ============================================================================
Add-Type -AssemblyName System.Drawing

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# ── PaDDY palette ────────────────────────────────────────────────────────────
$clBg      = [System.Drawing.Color]::FromArgb(0x0D, 0x0D, 0x14)  # #0D0D14  window bg
$clPanel   = [System.Drawing.Color]::FromArgb(0x1A, 0x1A, 0x28)  # #1A1A28  card bg
$clGreen   = [System.Drawing.Color]::FromArgb(0x4C, 0xAF, 0x50)  # #4CAF50  accent green
$clMuted   = [System.Drawing.Color]::FromArgb(0x70, 0x70, 0xA8)  # #7070A8  muted purple
$clSubtle  = [System.Drawing.Color]::FromArgb(0x50, 0x50, 0x80)  # #505080  subtle
$clGreenDim= [System.Drawing.Color]::FromArgb(0x28, 0x28, 0x40)  # #282840  separator

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

# Gradient glow from top: semi-transparent green fading to transparent
$glowTop    = New-Object System.Drawing.PointF(0, 0)
$glowBot    = New-Object System.Drawing.PointF(0, 80)
$glowBrush  = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $glowTop, $glowBot,
    [System.Drawing.Color]::FromArgb(22, 0x4C, 0xAF, 0x50),
    [System.Drawing.Color]::FromArgb(0,  0x0D, 0x0D, 0x14))
$g.FillRectangle($glowBrush, 0, 0, $W, 80)
$glowBrush.Dispose()

# Bottom gradient (subtle panel lift)
$bottomY    = $H - 60
$btTop      = New-Object System.Drawing.PointF(0, [float]$bottomY)
$btBot      = New-Object System.Drawing.PointF(0, [float]$H)
$bottomBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $btTop, $btBot,
    [System.Drawing.Color]::FromArgb(0,  0x0D, 0x0D, 0x14),
    [System.Drawing.Color]::FromArgb(30, 0x1A, 0x1A, 0x28))
$g.FillRectangle($bottomBrush, 0, $bottomY, $W, 60)
$bottomBrush.Dispose()

# ── Logo "PaDDY" ─────────────────────────────────────────────────────────────
$fLogo = New-Object System.Drawing.Font("Segoe UI", 30, [System.Drawing.FontStyle]::Bold,
         [System.Drawing.GraphicsUnit]::Point)

$brushMuted  = New-Object System.Drawing.SolidBrush($clMuted)
$brushGreen  = New-Object System.Drawing.SolidBrush($clGreen)

$szPa  = $g.MeasureString("Pa",  $fLogo)
$szDDY = $g.MeasureString("DDY", $fLogo)

# MeasureString adds padding; compensate with StringFormat
$sf = New-Object System.Drawing.StringFormat
$sf.Trimming  = [System.Drawing.StringTrimming]::None
$sf.FormatFlags = [System.Drawing.StringFormatFlags]::MeasureTrailingSpaces

$szPa  = $g.MeasureString("Pa",  $fLogo, [int]::MaxValue, $sf)
$szDDY = $g.MeasureString("DDY", $fLogo, [int]::MaxValue, $sf)

$totalW = $szPa.Width + $szDDY.Width
$logoX  = ($W - $totalW) / 2
$logoY  = [int](($H / 2) - ($szPa.Height / 2) - 10)   # slightly above center

$g.DrawString("Pa",  $fLogo, $brushMuted,  [float]$logoX,               [float]$logoY, $sf)
$g.DrawString("DDY", $fLogo, $brushGreen,  [float]($logoX + $szPa.Width), [float]$logoY, $sf)

$fLogo.Dispose(); $brushMuted.Dispose(); $brushGreen.Dispose(); $sf.Dispose()

# ── Tagline ───────────────────────────────────────────────────────────────────
$fTag      = New-Object System.Drawing.Font("Segoe UI", 8.5, [System.Drawing.FontStyle]::Regular,
             [System.Drawing.GraphicsUnit]::Point)
$brushSub  = New-Object System.Drawing.SolidBrush($clSubtle)
$tagText   = "audio pad recorder"
$szTag     = $g.MeasureString($tagText, $fTag)
$tagX      = ($W - $szTag.Width) / 2
$tagY      = $logoY + $szPa.Height + 4
$g.DrawString($tagText, $fTag, $brushSub, [float]$tagX, [float]$tagY)
$fTag.Dispose(); $brushSub.Dispose()

# ── Thin separator under tagline ─────────────────────────────────────────────
$sepY  = [int]($tagY + $szTag.Height + 10)
$sepPen = New-Object System.Drawing.Pen(
    [System.Drawing.Color]::FromArgb(45, 0x70, 0x70, 0xA8), 1)
$g.DrawLine($sepPen, 24, $sepY, $W - 24, $sepY)
$sepPen.Dispose()

# ── Small version text at bottom ─────────────────────────────────────────────
$fVer     = New-Object System.Drawing.Font("Segoe UI", 7.5, [System.Drawing.FontStyle]::Regular,
            [System.Drawing.GraphicsUnit]::Point)
$brushVer = New-Object System.Drawing.SolidBrush(
            [System.Drawing.Color]::FromArgb(0x38, 0x38, 0x58))
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
$bmp2, $g2 = New-Canvas $SW $SH

$g2.Clear($clBg)

# Green accent bar on the left edge
$barB2 = New-Object System.Drawing.SolidBrush($clGreen)
$g2.FillRectangle($barB2, 0, 0, 3, $SH)
$barB2.Dispose()

# Thin top bar
$barB3 = New-Object System.Drawing.SolidBrush($clGreen)
$g2.FillRectangle($barB3, 0, 0, $SW, 2)
$barB3.Dispose()

# "Pa" + "DDY" two-tone — compact, inside the small square
$fSmLogo = New-Object System.Drawing.Font("Segoe UI", 13, [System.Drawing.FontStyle]::Bold,
           [System.Drawing.GraphicsUnit]::Point)
$sfSm    = New-Object System.Drawing.StringFormat
$sfSm.FormatFlags = [System.Drawing.StringFormatFlags]::MeasureTrailingSpaces

$bMuted2  = New-Object System.Drawing.SolidBrush($clMuted)
$bGreen2  = New-Object System.Drawing.SolidBrush($clGreen)

$szPa2    = $g2.MeasureString("Pa",  $fSmLogo, [int]::MaxValue, $sfSm)
$szDDY2   = $g2.MeasureString("DDY", $fSmLogo, [int]::MaxValue, $sfSm)
$totW2    = $szPa2.Width + $szDDY2.Width
$lx2      = ($SW - $totW2) / 2 + 3   # +3 for left bar offset
$ly2      = ($SH - $szPa2.Height) / 2

$g2.DrawString("Pa",  $fSmLogo, $bMuted2, [float]$lx2, [float]$ly2, $sfSm)
$g2.DrawString("DDY", $fSmLogo, $bGreen2, [float]($lx2 + $szPa2.Width), [float]$ly2, $sfSm)

$fSmLogo.Dispose(); $bMuted2.Dispose(); $bGreen2.Dispose(); $sfSm.Dispose()
$g2.Dispose()

$smallPath = Join-Path $scriptDir "wizard-small.bmp"
$bmp2.Save($smallPath, [System.Drawing.Imaging.ImageFormat]::Bmp)
$bmp2.Dispose()
Write-Host "Saved: $smallPath"

Write-Host ""
Write-Host "Done. Both wizard images generated successfully."
