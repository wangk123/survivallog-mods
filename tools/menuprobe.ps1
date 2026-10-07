# Probe the main-menu left column: click a vertical line of points,
# then report which y values PENETRATED (CLICK_SCENE). Missing y = DOM button.
$ErrorActionPreference = 'SilentlyContinue'
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class MP { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e); }'
$log = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\LogOutput.log'
$game = Get-Process -Name SurvivalLog -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $game) { 'NO-GAME'; exit 1 }
$baseline = (Get-Content $log -Encoding UTF8 | Measure-Object -Line).Lines

[MP]::SetForegroundWindow($game.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 400
$x = 180
foreach ($y in 130..280) {
    if (($y % 10) -ne 0) { continue }
    $tx = $x * 2; $ty = $y * 2
    1..6 | ForEach-Object { [MP]::SetCursorPos([int](1280 + ($tx-1280)*$_/6), [int](720 + ($ty-720)*$_/6)) | Out-Null; Start-Sleep -Milliseconds 6 }
    Start-Sleep -Milliseconds 120
    [MP]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 40; [MP]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 350
}
Start-Sleep -Seconds 2
$new = Get-Content $log -Encoding UTF8 | Select-Object -Skip $baseline
$hitY = @()
foreach ($line in $new) {
    if ($line -match 'CLICK_SCENE json=\{"x":([0-9.]+),"y":([0-9.]+)\}') {
        $px = [double]$Matches[1] * 1280
        $py = (1 - [double]$Matches[2]) * 720
        $hitY += [math]::Round($py)
    }
}
"penetrated y: $($hitY -join ',')"
$missing = @(130..280 | Where-Object { ($_ % 10) -eq 0 -and $hitY -notcontains $_ })
"consumed(DOM) y: $($missing -join ',')"
