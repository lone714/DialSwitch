# 產生程式圖示 app.ico（多尺寸 PNG 封裝）。圖案全部用向量繪製，不依賴字型。
# 用法：powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 [-Preview 預覽.png]
param([string]$Preview)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\src\DialSwitch\Assets\app.ico'

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'

    # 背景：圓角方塊，藍 → 紫漸層
    $bg = RoundRect 0 0 $s $s ($s * 0.22)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $s, $s), ([System.Drawing.Color]::FromArgb(255, 56, 132, 255)), ([System.Drawing.Color]::FromArgb(255, 124, 77, 255))
    $g.FillPath($grad, $bg)

    $white = [System.Drawing.Color]::White
    $c = $s / 2.0

    if ($s -ge 32) {
        # 旋鈕外圈：淡白色整圈 + 亮白弧線表示「轉動」
        $r = $s * 0.36
        $w = [Math]::Max(1.5, $s * 0.055)
        $faint = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(70, 255, 255, 255)), $w
        $g.DrawEllipse($faint, $c - $r, $c - $r, $r * 2, $r * 2)
        $arc = New-Object System.Drawing.Pen $white, $w
        $arc.StartCap = 'Round'; $arc.EndCap = 'Round'
        $g.DrawArc($arc, $c - $r, $c - $r, $r * 2, $r * 2, 200, 110)
        # 弧線末端的指示點
        $a = (200 + 110) * [Math]::PI / 180
        $dot = $w * 1.1
        $g.FillEllipse((New-Object System.Drawing.SolidBrush $white), $c + $r * [Math]::Cos($a) - $dot, $c + $r * [Math]::Sin($a) - $dot, $dot * 2, $dot * 2)
        $scale = 0.40
    } else {
        $scale = 0.62   # 小尺寸只畫耳機，保持清楚
    }

    # 耳機：頭帶弧線 + 兩個耳罩
    $hw = $s * $scale            # 耳機整體寬
    $band = [Math]::Max(1.5, $hw * 0.13)
    $cupW = $hw * 0.24; $cupH = $hw * 0.42
    $top = $c - $hw * 0.42
    $pen = New-Object System.Drawing.Pen $white, $band
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $arcR = ($hw - $cupW) / 2
    $g.DrawArc($pen, $c - $arcR, $top, $arcR * 2, $arcR * 2, 180, 180)
    $cupY = $top + $arcR
    $brush = New-Object System.Drawing.SolidBrush $white
    $g.FillPath($brush, (RoundRect ($c - $hw / 2) $cupY $cupW $cupH ($cupW * 0.35)))
    $g.FillPath($brush, (RoundRect ($c + $hw / 2 - $cupW) $cupY $cupW $cupH ($cupW * 0.35)))
    # 頭帶延伸到耳罩
    $g.DrawLine($pen, $c - $arcR, $top + $arcR, $c - $arcR, $cupY + $cupH * 0.3)
    $g.DrawLine($pen, $c + $arcR, $top + $arcR, $c + $arcR, $cupY + $cupH * 0.3)

    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$pngs = foreach ($s in $sizes) {
    $bmp = Draw $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

# ICO 格式：6 bytes 檔頭 + 每張 16 bytes 目錄 + PNG 資料
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Dispose()
Write-Host "已產生 $out"

if ($Preview) {
    # 預覽圖：各尺寸並排在深色與淺色底上
    $sheet = New-Object System.Drawing.Bitmap 760, 600
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(32, 32, 32))), 0, 0, 760, 300)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(243, 243, 243))), 0, 300, 760, 300)
    foreach ($row in 0, 300) {
        $x = 20
        foreach ($s in $sizes) {
            $b = Draw $s
            $g.DrawImage($b, $x, $row + 20 + (256 - [Math]::Min($s, 256)) / 2, $s, $s)
            $x += $s + 16
            $b.Dispose()
        }
    }
    $g.Dispose(); $sheet.Save($Preview); $sheet.Dispose()
    Write-Host "預覽 $Preview"
}
