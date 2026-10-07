param(
    [int]$Rx,   # raster X (0..1280) as seen in 1280x720 screenshots
    [int]$Ry,   # raster Y (0..720)
    [int]$HoverMs = 250,
    [switch]$NoClick   # only hover
)
$ErrorActionPreference = 'SilentlyContinue'
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class GK { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e); }'
$game = Get-Process -Name SurvivalLog -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $game) { Write-Output 'NO-GAME-PROCESS'; exit 1 }
[GK]::SetForegroundWindow($game.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 350
# raster 1280x720 -> SetCursorPos space = raster * 2 (game logical 2560x1440)
$tx = $Rx * 2; $ty = $Ry * 2
# step-move from screen center for realistic mousemove stream
$sx = 1280; $sy = 720
1..12 | ForEach-Object {
    $x = [int]($sx + ($tx - $sx) * $_ / 12)
    $y = [int]($sy + ($ty - $sy) * $_ / 12)
    [GK]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 8
}
Start-Sleep -Milliseconds $HoverMs
if (-not $NoClick) {
    [GK]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 50
    [GK]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
}
Write-Output ("done raster({0},{1}) cursor({2},{3})" -f $Rx,$Ry,$tx,$ty)
