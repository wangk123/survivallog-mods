param(
    [int]$Vk,          # virtual key code, e.g. 0x45 = E, 0x46 = F
    [int]$HoldMs = 90
)
$ErrorActionPreference = 'SilentlyContinue'
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class GK2 { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo); }'
$game = Get-Process -Name SurvivalLog -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $game) { Write-Output 'NO-GAME-PROCESS'; exit 1 }
[GK2]::SetForegroundWindow($game.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 300
$scan = [System.Windows.Forms.Keys]$Vk  # no - simpler: map vk to scancode via MapVirtualKey
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class GK3 { [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint uCode, uint uMapType); }'
$sc = [GK3]::MapVirtualKey([uint32]$Vk, 0)
[GK2]::keybd_event([byte]$Vk, [byte]$sc, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds $HoldMs
[GK2]::keybd_event([byte]$Vk, [byte]$sc, 2, [UIntPtr]::Zero)
Write-Output ("key 0x{0:X2} sent" -f $Vk)
