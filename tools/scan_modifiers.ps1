$dir = 'E:\SteamLibrary\steamapps\common\Survival Log\SurvivalLog_Data\StreamingAssets\WebUI'
foreach ($n in @('webui-bag.js','webui-core.js','webui-tabhint.js')) {
    $s = [System.IO.File]::ReadAllText((Join-Path $dir $n), [System.Text.Encoding]::UTF8)
    "== $n : altKey=$($s.Contains('altKey')) ctrlKey=$($s.Contains('ctrlKey')) shiftKey=$($s.Contains('shiftKey')) metaKey=$($s.Contains('metaKey'))"
}
# show altKey usage context in webui-bag.js (the bag UI logic)
$bag = [System.IO.File]::ReadAllText((Join-Path $dir 'webui-bag.js'), [System.Text.Encoding]::UTF8)
$idx = 0
while (($idx = $bag.IndexOf('altKey', $idx)) -ge 0) {
    $start = [Math]::Max(0, $idx - 120)
    $len = [Math]::Min(240, $bag.Length - $start)
    "ALT-CTX: ..." + $bag.Substring($start, $len).Replace("`n", " ").Replace("`r", "") + "..."
    $idx += 6
    $global:altCount++
    if ($global:altCount -ge 5) { break }
}
