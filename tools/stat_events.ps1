$c = Get-Content 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\LogOutput.log' -Encoding UTF8
$evs = @{}
foreach ($line in $c) {
    if ($line -match '\[事件\] (\S+) json=(.{0,110})') {
        $name = $Matches[1]
        if (-not $evs.ContainsKey($name)) { $evs[$name] = @() }
        if ($evs[$name].Count -lt 3) { $evs[$name] += $Matches[2] }
    }
}
foreach ($k in $evs.Keys) {
    "$($k)"
    foreach ($s in $evs[$k]) { "    $s" }
}
