$dir = 'E:\SteamLibrary\steamapps\common\Survival Log\SurvivalLog_Data\StreamingAssets\WebUI\UI\BackpackUI'
$html = [System.IO.File]::ReadAllText((Join-Path $dir 'BackpackUI.html'), [System.Text.Encoding]::UTF8)

# shiftKey contexts
$idx = 0; $n = 0
while ((($idx = $html.IndexOf('shiftKey', $idx)) -ge 0) -and ($n -lt 8)) {
    $start = [Math]::Max(0, $idx - 200)
    "SHIFT-CTX[$n]: ..." + $html.Substring($start, 380).Replace("`n", " ") + "..."
    $idx += 9; $n++
}
"`n=== Detail references ==="
foreach ($kw in @('ItemDetail', 'DETAIL', 'detail', '详情')) {
    $i = $html.IndexOf($kw)
    if ($i -ge 0) {
        $s2 = [Math]::Max(0, $i - 150)
        "$kw @${i}: ..." + $html.Substring($s2, 250).Replace("`n", " ") + "..."
    }
}
