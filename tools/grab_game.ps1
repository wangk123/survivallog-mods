param([string]$Out = 'C:\temp\game_screen.png')
$ErrorActionPreference = 'SilentlyContinue'
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class FG { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); public struct RECT { public int L; public int T; public int R; public int B; } }'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$game = Get-Process -Name SurvivalLog -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $game) { 'NO-GAME'; exit 1 }
[FG]::SetForegroundWindow($game.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 900
$r = New-Object FG+RECT
[FG]::GetWindowRect($game.MainWindowHandle, [ref]$r) | Out-Null
$w = $r.R - $r.L; $h = $r.B - $r.T
if ($w -le 0 -or $h -le 0) { $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds; $r.L = $b.X; $r.T = $b.Y; $w = $b.Width; $h = $b.Height }
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, [System.Drawing.Point]::Empty, (New-Object System.Drawing.Size($w, $h)))
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"window rect: L=$($r.L) T=$($r.T) W=$w H=$h -> $Out"
