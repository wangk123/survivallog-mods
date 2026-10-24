# ModCenter task-8 helper: focus the game window and send plain digits (typing probe
# for the keyboard-gate test: panel open + ovr ID number input focused).
# VERIFICATION TOOL ONLY - never ship in installer / publish artifacts (probe discipline).
# ASCII-only per repo rule (PowerShell 5.1 encoding).
param([string]$Key = '8012')
$p = Get-Process SurvivalLog -ErrorAction Stop
$ws = New-Object -ComObject WScript.Shell
$ok = $ws.AppActivate($p.Id)
Start-Sleep -Milliseconds 600
$ws.SendKeys($Key)
Write-Output ("activate=" + $ok + " sent=" + $Key)
