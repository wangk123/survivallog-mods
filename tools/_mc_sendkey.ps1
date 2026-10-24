# ModCenter task-7 helper: focus the game window and send ESC (open pause menu).
# VERIFICATION TOOL ONLY - never ship in installer / publish artifacts (probe discipline,
# same as SurvivalLog.ActionProbe; publish.ps1 $suiteMods and installer.iss exclude tools/_mc*).
# ASCII-only per repo rule (PowerShell 5.1 encoding).
 param([string]$Key = '{ESC}')
$p = Get-Process SurvivalLog -ErrorAction Stop
$ws = New-Object -ComObject WScript.Shell
$ok = $ws.AppActivate($p.Id)
Start-Sleep -Milliseconds 500
$ws.SendKeys($Key)
Write-Output ("activate=" + $ok + " sent=" + $Key)
