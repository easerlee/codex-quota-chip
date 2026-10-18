# 生成 src/CodexQuotaChip/app.ico —— 图标源是代码，改样式就改这里再跑一遍。
#
# 样子：深色圆角方块 + 两条圆头进度条（上琥珀 = 5 小时窗口，下绿色 = 周窗口），
# 也就是程序本体的缩小版：一眼看出是"额度条"，16px 下也还认得出。
#
# 用法：pwsh -File tools/make-icon.ps1
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\src\CodexQuotaChip\app.ico'),
    [int[]]$Sizes = @(16, 24, 32, 48, 64, 128, 256),
    # README 的头图：同一份画法，单独出一张 PNG（GitHub 渲染 .ico 不可靠）
    [string]$Png = (Join-Path $PSScriptRoot '..\docs\icon.png'),
    [int]$PngSize = 256
)

Add-Type -AssemblyName System.Drawing

$tileTop = [System.Drawing.Color]::FromArgb(255, 0x22, 0x28, 0x33)
$tileBottom = [System.Drawing.Color]::FromArgb(255, 0x10, 0x14, 0x1A)
$track = [System.Drawing.Color]::FromArgb(255, 0x39, 0x40, 0x4C)
$amber = [System.Drawing.Color]::FromArgb(255, 0xE8, 0xA3, 0x3D)
$green = [System.Drawing.Color]::FromArgb(255, 0x1F, 0xA4, 0x63)

function New-RoundedPath([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    if ($d -gt $w) { $d = $w }
    if ($d -gt $h) { $d = $h }
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size / 256.0
    # 方块本身留一点边，免得在文件列表/托盘里显得顶格
    $pad = [Math]::Max(1.0, 12 * $s)
    $side = $size - 2 * $pad
    $radius = [Math]::Max(1.5, 56 * $s)

    $tile = New-RoundedPath $pad $pad $side $side $radius
    $from = [System.Drawing.PointF]::new([float]0, [float]$pad)
    $to = [System.Drawing.PointF]::new([float]0, [float]($pad + $side))
    $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new($from, $to, $tileTop, $tileBottom)
    $g.FillPath($brush, $tile)

    # 两条进度条：几何按比例缩放，16px 时仍保留 2px 以上的可见高度，整体在方块里居中
    $barLeft = $pad + 34 * $s
    $barWidth = $side - 68 * $s
    $barHeight = [Math]::Max(2.0, 40 * $s)
    $barRadius = $barHeight / 2
    $gap = [Math]::Max(2.0, 34 * $s)
    $top = $pad + ($side - (2 * $barHeight + $gap)) / 2

    foreach ($bar in @(
            @{ y = $top; fill = $amber; fraction = 0.42 },
            @{ y = $top + $barHeight + $gap; fill = $green; fraction = 0.80 })) {
        $trackPath = New-RoundedPath $barLeft $bar.y $barWidth $barHeight $barRadius
        $g.FillPath([System.Drawing.SolidBrush]::new($track), $trackPath)
        $fillWidth = [Math]::Max($barHeight, $barWidth * $bar.fraction)
        $fillPath = New-RoundedPath $barLeft $bar.y $fillWidth $barHeight $barRadius
        $g.FillPath([System.Drawing.SolidBrush]::new($bar.fill), $fillPath)
    }

    $g.Dispose()
    return $bmp
}

# ICO 容器：每张图直接存 PNG（Vista 起各尺寸都支持，也就不用手写 DIB）
$pngs = @()
foreach ($size in $Sizes) {
    $bmp = New-IconBitmap $size
    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngs += , @{ size = $size; bytes = $stream.ToArray() }
    $stream.Dispose()
}

$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($out)
$writer.Write([uint16]0)                # reserved
$writer.Write([uint16]1)                # type: icon
$writer.Write([uint16]$pngs.Count)      # image count

$offset = 6 + 16 * $pngs.Count
foreach ($image in $pngs) {
    $dimension = if ($image.size -ge 256) { 0 } else { $image.size }   # 0 表示 256
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)              # palette colours
    $writer.Write([byte]0)              # reserved
    $writer.Write([uint16]1)            # colour planes
    $writer.Write([uint16]32)           # bits per pixel
    $writer.Write([uint32]$image.bytes.Length)
    $writer.Write([uint32]$offset)
    $offset += $image.bytes.Length
}
foreach ($image in $pngs) { $writer.Write($image.bytes) }
$writer.Flush()

$directory = Split-Path $Output -Parent
if (-not (Test-Path $directory)) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
[System.IO.File]::WriteAllBytes($Output, $out.ToArray())
$writer.Dispose()
$out.Dispose()

Write-Host ("wrote {0} ({1} bytes, sizes: {2})" -f $Output, (Get-Item $Output).Length, ($Sizes -join ', '))

if ($Png) {
    $bmp = New-IconBitmap $PngSize
    $pngDirectory = Split-Path $Png -Parent
    if (-not (Test-Path $pngDirectory)) { New-Item -ItemType Directory -Force -Path $pngDirectory | Out-Null }
    $bmp.Save($Png, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host ("wrote {0} ({1} bytes, {2}px)" -f $Png, (Get-Item $Png).Length, $PngSize)
}
