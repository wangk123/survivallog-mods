param([string]$Out = 'C:\temp\game_screen.png')
$ErrorActionPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap($b.Width, $b.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"Wrote $Out ($($b.Width)x($b.Height))"
