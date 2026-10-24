# ModCenter task-17 helper: REAL cursor click at computed screen coords + SendKeys
# (physical typing probe for the in-game keyboard-gate test; T8's synthetic-focus
# attempts failed as environmental -- real Unity-side pointer routing needs a real
# OS click). Computes screen position from CSS coords: client origin via
# ClientToScreen, scale = clientWidth / page innerWidth (absorbs DPI/zoom).
# VERIFICATION TOOL ONLY - never ship in installer / publish artifacts (probe
# discipline, same as tools/_mc_* family). ASCII-only per repo rule (PS 5.1).
param([double]$CssX = 0, [double]$CssY = 0, [string]$Key = '8012', [double]$InnerW = 0)

$src = @'
using System;
using System.Runtime.InteropServices;
public static class W32 {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, int i);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L; public int T; public int R; public int B; }
}
'@
Add-Type -TypeDefinition $src

$p = Get-Process SurvivalLog -ErrorAction Stop
$h = $p.MainWindowHandle
$r = New-Object W32+RECT
[void][W32]::GetClientRect($h, [ref]$r)
$pt = New-Object W32+POINT
$pt.X = 0; $pt.Y = 0
[void][W32]::ClientToScreen($h, [ref]$pt)
$scale = 1.0
if ($InnerW -gt 0) { $scale = ($r.R - $r.L) / $InnerW }
$x = [int]($pt.X + $CssX * $scale)
$y = [int]($pt.Y + $CssY * $scale)

$ws = New-Object -ComObject WScript.Shell
$ok = $ws.AppActivate($p.Id)
Start-Sleep -Milliseconds 400
[void][W32]::SetCursorPos($x, $y)
Start-Sleep -Milliseconds 400
[W32]::mouse_event(2, 0, 0, 0, 0)
Start-Sleep -Milliseconds 100
[W32]::mouse_event(4, 0, 0, 0, 0)
Start-Sleep -Milliseconds 700
$ws.SendKeys($Key)
Write-Output ("activate=" + $ok + " clientOrigin=" + $pt.X + "," + $pt.Y + " clientSize=" + $r.R + "x" + $r.B + " scale=" + [math]::Round($scale, 4) + " click=" + $x + "," + $y + " sent=" + $Key)
