# 生成 Inno 安装器用的抖音名片 BMP（TBitmapImage 只认 BMP；按显示尺寸约 2 倍出图保证扫码清晰）
# 源图：assets\banners\douyin\douyin_night_card.jpg（1125x1680）
# 产物：installer\assets\douyin_night_card.bmp（宽 400）
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$src = Join-Path $root 'assets\banners\douyin\douyin_night_card.jpg'
$dst = Join-Path $root 'installer\assets\douyin_night_card.bmp'
$targetW = 400

$img = [System.Drawing.Image]::FromFile($src)
$targetH = [int]([Math]::Round($img.Height * $targetW / $img.Width))
$bmp = New-Object System.Drawing.Bitmap($targetW, $targetH, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.DrawImage($img, (New-Object System.Drawing.Rectangle(0, 0, $targetW, $targetH)))
$g.Dispose()
$img.Dispose()
$bmp.Save($dst, [System.Drawing.Imaging.ImageFormat]::Bmp)
$bmp.Dispose()
Write-Host ("OK -> {0} ({1}x{2})" -f $dst, $targetW, $targetH)
