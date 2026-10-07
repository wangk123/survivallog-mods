$dir = 'E:\SteamLibrary\steamapps\common\Survival Log\SurvivalLog_Data\StreamingAssets\WebUI\UI\BackpackUI'
$html = [System.IO.File]::ReadAllText((Join-Path $dir 'BackpackUI.html'), [System.Text.Encoding]::UTF8)
"BackpackUI.html size: $($html.Length)"
"altKey=$($html.Contains('altKey')) ctrlKey=$($html.Contains('ctrlKey')) shiftKey=$($html.Contains('shiftKey'))"
# find click handlers and modifier contexts
$idx = 0; $n = 0
while ((($idx = $html.IndexOf('altKey', $idx)) -ge 0) -and ($n -lt 6)) {
    $start = [Math]::Max(0, $idx - 150)
    "ALT-CTX: ..." + $html.Substring($start, 300).Replace("`n", " ") + "..."
    $idx += 6; $n++
}
# also check how CLICK_ITEM is dispatched and what suppresses it
$ci = $html.IndexOf('CLICK_ITEM')
if ($ci -ge 0) {
    "CLICK_ITEM ctx: ..." + $html.Substring([Math]::Max(0, $ci - 400), 700).Replace("`n", " ") + "..."
}
