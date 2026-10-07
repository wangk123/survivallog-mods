param(
    [int]$Rx, [int]$Ry,
    [int]$HoldMs = 130
)
# Hold real Alt + real click at raster coords (Alt+Left-click split test)
$ErrorActionPreference = 'SilentlyContinue'
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class AC { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e); [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo); }'
$game = Get-Process -Name SurvivalLog -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $game) { Write-Output 'NO-GAME-PROCESS'; exit 1 }
[AC]::SetForegroundWindow($game.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 300
$tx = $Rx * 2; $ty = $Ry * 2
1..8 | ForEach-Object { [AC]::SetCursorPos([int](1280 + ($tx-1280)*$_/8), [int](720 + ($ty-720)*$_/8)) | Out-Null; Start-Sleep -Milliseconds 8 }
Start-Sleep -Milliseconds 150
[AC]::keybd_event(0x12, 0x38, 0, [UIntPtr]::Zero)   # LMENU down
Start-Sleep -Milliseconds 60
[AC]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Milliseconds 45
[AC]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Milliseconds $HoldMs
[AC]::keybd_event(0x12, 0x38, 2, [UIntPtr]::Zero)   # LMENU up
Write-Output ("alt+click raster({0},{1})" -f $Rx,$Ry)
