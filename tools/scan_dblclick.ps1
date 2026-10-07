$dir = 'E:\SteamLibrary\steamapps\common\Survival Log\SurvivalLog_Data\StreamingAssets\WebUI\UI\BackpackUI'
$html = [System.IO.File]::ReadAllText((Join-Path $dir 'BackpackUI.html'), [System.Text.Encoding]::UTF8)
"dblclick refs: $(([regex]::Matches($html, 'dblclick')).Count)"
"doubleClick refs: $(([regex]::Matches($html, 'doubleClick|DoubleClick')).Count)"
$idx = 0; $n = 0
while ((($idx = $html.IndexOf('dblclick', $idx)) -ge 0) -and ($n -lt 4)) {
    $start = [Math]::Max(0, $idx - 200)
    "CTX[$n]: ..." + $html.Substring($start, 400).Replace("`n", " ") + "..."
    $idx += 8; $n++
}

# also check the ToolTable page for dblclick + what its item click does
$tt = [System.IO.File]::ReadAllText('E:\SteamLibrary\steamapps\common\Survival Log\SurvivalLog_Data\StreamingAssets\WebUI\UI\ToolTable\ToolTable.html', [System.Text.Encoding]::UTF8)
"ToolTable dblclick refs: $(([regex]::Matches($tt, 'dblclick')).Count)"
"ToolTable altKey: $($tt.Contains('altKey')) ctrlKey: $($tt.Contains('ctrlKey')) shiftKey: $($tt.Contains('shiftKey'))"
$ci = $tt.IndexOf('ACTION_ITEM_CLICK')
if ($ci -ge 0) {
    $s2 = [Math]::Max(0, $ci - 300)
    "TT CLICK ctx: ..." + $tt.Substring($s2, 500).Replace("`n", " ") + "..."
}
