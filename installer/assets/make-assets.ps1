# Draws the installer artwork from the same palette the application paints itself with, so the
# wizard reads as part of the product rather than as a stock setup dialog. Regenerate by running
# this script; the output is committed so building the installer needs nothing but Inno Setup.
#
# Inno wants 24 bit BMP for wizard images and an ICO for the setup icon.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$here = $PSScriptRoot

# Straight out of Views/Themes/EditorDark.xaml. If the theme moves, move these with it.
$window = [System.Drawing.ColorTranslator]::FromHtml('#1E1E1E')
$panel = [System.Drawing.ColorTranslator]::FromHtml('#252526')
$card = [System.Drawing.ColorTranslator]::FromHtml('#2D2D30')
$border = [System.Drawing.ColorTranslator]::FromHtml('#3E3E42')
$accent = [System.Drawing.ColorTranslator]::FromHtml('#007ACC')
$muted = [System.Drawing.ColorTranslator]::FromHtml('#858585')

$nodeColours = @(
    [System.Drawing.ColorTranslator]::FromHtml('#569CD6'),  # Prompt
    [System.Drawing.ColorTranslator]::FromHtml('#C586C0'),  # Model
    [System.Drawing.ColorTranslator]::FromHtml('#D16D9E'),  # Compiler check
    [System.Drawing.ColorTranslator]::FromHtml('#DCDCAA')   # Output
)

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear($window)
    return @($bmp, $g)
}

# A node as the canvas draws one: a neutral body with the type colour on the leading edge only.
function Draw-Node($g, [float]$x, [float]$y, [float]$w, [float]$h, $colour, [float]$scale) {
    $body = New-Object System.Drawing.SolidBrush($card)
    $g.FillRectangle($body, $x, $y, $w, $h)
    $accentBrush = New-Object System.Drawing.SolidBrush($colour)
    $g.FillRectangle($accentBrush, $x, $y, [Math]::Max(2.0, 3.0 * $scale), $h)
    $pen = New-Object System.Drawing.Pen($border, [Math]::Max(1.0, 1.0 * $scale))
    $g.DrawRectangle($pen, $x, $y, $w, $h)

    # Two title bars, so it reads as a node rather than a plain box.
    $line = New-Object System.Drawing.SolidBrush($muted)
    $pad = 7.0 * $scale
    $g.FillRectangle($line, $x + $pad, $y + $pad, $w - $pad * 2, 2.5 * $scale)
    $dim = New-Object System.Drawing.SolidBrush($border)
    $g.FillRectangle($dim, $x + $pad, $y + $pad + 6.0 * $scale, ($w - $pad * 2) * 0.6, 2.0 * $scale)

    $body.Dispose(); $accentBrush.Dispose(); $pen.Dispose(); $line.Dispose(); $dim.Dispose()
}

# The tall image on the welcome and finished pages: a graph running top to bottom.
function New-WizardLarge([int]$w, [int]$h, [string]$path) {
    $scale = $w / 164.0
    $pair = New-Canvas $w $h
    $bmp = $pair[0]; $g = $pair[1]

    $sidebar = New-Object System.Drawing.SolidBrush($panel)
    $g.FillRectangle($sidebar, 0, 0, $w, $h)
    $sidebar.Dispose()

    $nodeW = 92.0 * $scale
    $nodeH = 34.0 * $scale
    $x = ($w - $nodeW) / 2.0
    $gap = 26.0 * $scale
    $top = ($h - ($nodeH * 4 + $gap * 3)) / 2.0

    # Wires first, so the nodes sit on top of them.
    $wire = New-Object System.Drawing.Pen($accent, [Math]::Max(1.5, 2.0 * $scale))
    for ($i = 0; $i -lt 3; $i++) {
        $y1 = $top + $nodeH * ($i + 1) + $gap * $i
        $g.DrawLine($wire, $w / 2.0, $y1, $w / 2.0, $y1 + $gap)
    }
    $wire.Dispose()

    for ($i = 0; $i -lt 4; $i++) {
        $y = $top + ($nodeH + $gap) * $i
        Draw-Node $g $x $y $nodeW $nodeH $nodeColours[$i] $scale
    }

    # A hairline of accent down the leading edge, echoing the window chrome.
    $edge = New-Object System.Drawing.SolidBrush($accent)
    $g.FillRectangle($edge, 0, 0, [Math]::Max(2.0, 3.0 * $scale), $h)
    $edge.Dispose()

    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
    Write-Host "wrote $path (${w}x${h})"
}

# The small header image, shown on every page after the first.
function New-WizardSmall([int]$size, [string]$path) {
    $scale = $size / 55.0
    $pair = New-Canvas $size $size
    $bmp = $pair[0]; $g = $pair[1]

    $nodeW = 34.0 * $scale
    $nodeH = 13.0 * $scale
    $x = ($size - $nodeW) / 2.0
    $gap = 8.0 * $scale
    $top = ($size - ($nodeH * 3 + $gap * 2)) / 2.0

    $wire = New-Object System.Drawing.Pen($accent, [Math]::Max(1.0, 1.5 * $scale))
    for ($i = 0; $i -lt 2; $i++) {
        $y1 = $top + $nodeH * ($i + 1) + $gap * $i
        $g.DrawLine($wire, $size / 2.0, $y1, $size / 2.0, $y1 + $gap)
    }
    $wire.Dispose()

    for ($i = 0; $i -lt 3; $i++) {
        $y = $top + ($nodeH + $gap) * $i
        Draw-Node $g $x $y $nodeW $nodeH $nodeColours[$i] ($scale * 0.55)
    }

    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
    Write-Host "wrote $path (${size}x${size})"
}

# Inno picks the closest size for the display scaling, so several are supplied.
New-WizardLarge 164 314 (Join-Path $here 'wizard-large.bmp')
New-WizardLarge 205 393 (Join-Path $here 'wizard-large-125.bmp')
New-WizardLarge 246 471 (Join-Path $here 'wizard-large-150.bmp')
New-WizardLarge 328 628 (Join-Path $here 'wizard-large-200.bmp')

New-WizardSmall 55 (Join-Path $here 'wizard-small.bmp')
New-WizardSmall 69 (Join-Path $here 'wizard-small-125.bmp')
New-WizardSmall 83 (Join-Path $here 'wizard-small-150.bmp')
New-WizardSmall 110 (Join-Path $here 'wizard-small-200.bmp')

# The setup icon. Written by hand because System.Drawing cannot save a multi size ICO: the
# format is a small header followed by one PNG per size, which Windows has accepted since Vista.
function New-Icon([string]$path, [int[]]$sizes) {
    $frames = @()
    foreach ($size in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)

        $scale = $size / 256.0
        $round = New-Object System.Drawing.Drawing2D.GraphicsPath
        $r = 48.0 * $scale
        $d = $r * 2
        $round.AddArc(0, 0, $d, $d, 180, 90)
        $round.AddArc($size - $d, 0, $d, $d, 270, 90)
        $round.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
        $round.AddArc(0, $size - $d, $d, $d, 90, 90)
        $round.CloseFigure()
        $bg = New-Object System.Drawing.SolidBrush($window)
        $g.FillPath($bg, $round)
        $bg.Dispose(); $round.Dispose()

        # Three nodes on a wire, the smallest thing that still reads as a graph at 16 pixels.
        $nodeW = 150.0 * $scale
        $nodeH = 44.0 * $scale
        $x = ($size - $nodeW) / 2.0
        $gap = 26.0 * $scale
        $top = ($size - ($nodeH * 3 + $gap * 2)) / 2.0

        $wire = New-Object System.Drawing.Pen($accent, [Math]::Max(1.0, 8.0 * $scale))
        for ($i = 0; $i -lt 2; $i++) {
            $y1 = $top + $nodeH * ($i + 1) + $gap * $i
            $g.DrawLine($wire, $size / 2.0, $y1, $size / 2.0, $y1 + $gap)
        }
        $wire.Dispose()

        for ($i = 0; $i -lt 3; $i++) {
            $y = $top + ($nodeH + $gap) * $i
            $body = New-Object System.Drawing.SolidBrush($card)
            $g.FillRectangle($body, $x, $y, $nodeW, $nodeH)
            $bar = New-Object System.Drawing.SolidBrush($nodeColours[$i])
            $g.FillRectangle($bar, $x, $y, [Math]::Max(2.0, 14.0 * $scale), $nodeH)
            $body.Dispose(); $bar.Dispose()
        }

        $g.Dispose()

        $stream = New-Object System.IO.MemoryStream
        $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $frames += , @{ Size = $size; Bytes = $stream.ToArray() }
        $stream.Dispose()
    }

    $out = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($out)
    $writer.Write([UInt16]0)                    # reserved
    $writer.Write([UInt16]1)                    # type: icon
    $writer.Write([UInt16]$frames.Count)

    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dim = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
        $writer.Write([Byte]$dim)               # width, 0 meaning 256
        $writer.Write([Byte]$dim)               # height
        $writer.Write([Byte]0)                  # palette size
        $writer.Write([Byte]0)                  # reserved
        $writer.Write([UInt16]1)                # colour planes
        $writer.Write([UInt16]32)               # bits per pixel
        $writer.Write([UInt32]$frame.Bytes.Length)
        $writer.Write([UInt32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write($frame.Bytes) }

    $writer.Flush()
    [System.IO.File]::WriteAllBytes($path, $out.ToArray())
    $writer.Dispose(); $out.Dispose()
    Write-Host "wrote $path ($($frames.Count) sizes)"
}

New-Icon (Join-Path $here 'LocalNEXUS.ico') @(16, 24, 32, 48, 64, 128, 256)

Write-Host "Done."
