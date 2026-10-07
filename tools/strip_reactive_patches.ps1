$ErrorActionPreference = 'Stop'
$f = 'C:\Users\wangk\.zcode\workspace\default\mod_stack\SurvivalLog.StackLimit999\StackPatches.cs'
$text = [System.IO.File]::ReadAllText($f, [System.Text.Encoding]::UTF8)

# 1) Remove RECIPE_CLICK capture block (comment line above + the if block)
$rx1 = '(?s)\r?\n *// [^\r\n]*\r?\n( *if \(ev == "RECIPE_CLICK"\)\r?\n.*?\r?\n        \}\r?\n)'
$m1 = [regex]::Matches($text, $rx1)
if ($m1.Count -ne 1) { throw ("capture block matches: " + $m1.Count) }
$text = [regex]::Replace($text, $rx1, "`r`n", 'Singleline')

# 2) Remove reactive patch block: from first StartProduction probe attribute to TrySplitSelected declaration
$startA = '    [HarmonyPatch(typeof(GameCore.HotUpdate.Battle.Logic.ToolTableManager), "StartProduction")]'
$endA = '    internal static void TrySplitSelected()'
$si = $text.IndexOf($startA)
$ei = $text.IndexOf($endA)
if ($si -lt 0 -or $ei -lt 0 -or $ei -le $si) { throw ("split block anchors: si=$si ei=$ei") }
$text = $text.Substring(0, $si) + $endA + $text.Substring($ei + $endA.Length)

# 3) Remove WorkbenchDiag class (from preceding summary to LossResterer preceding summary)
$wdDecl = 'internal static class WorkbenchDiag'
$lrDecl = 'internal static class LossRestorer'
$wd = $text.IndexOf($wdDecl)
if ($wd -lt 0) { throw 'WorkbenchDiag not found' }
$sumStart = $text.LastIndexOf('/// <summary>', $wd)
if ($sumStart -lt 0) { throw 'WorkbenchDiag summary not found' }
$lr = $text.IndexOf($lrDecl, $wd)
if ($lr -lt 0) { throw 'LossRestorer not found' }
$lrSum = $text.LastIndexOf('/// <summary>', $lr)
if ($lrSum -lt $sumStart) { throw 'LossRestorer summary not found' }
$text = $text.Substring(0, $sumStart) + $text.Substring($lrSum)

# 4) Remove WorkbenchDiag.Tick() call
$tickLine = "        WorkbenchDiag.Tick();`r`n"
if (-not $text.Contains($tickLine)) { $tickLine = "        WorkbenchDiag.Tick();`n" }
if (-not $text.Contains($tickLine)) { throw 'tick line not found' }
$text = $text.Replace($tickLine, '')

[System.IO.File]::WriteAllText($f, $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Host 'stripped OK'
