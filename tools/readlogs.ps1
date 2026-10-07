$c = Get-Content 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\LogOutput.log' -Encoding UTF8
"bepinex lastwrite: $((Get-Item 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\LogOutput.log').LastWriteTime) lines: $($c.Count)"
$c | Select-String -Pattern 'Loading \[StackLimit999|存档清洗|清扫' | Select-Object -Last 6 | ForEach-Object { $_.Line }
'--- Player.log ---'
$p = "$env:USERPROFILE\AppData\LocalLow\LLS\SLGame\Player.log"
"player lastwrite: $((Get-Item $p).LastWriteTime)"
$pl = Get-Content $p -Encoding UTF8
"dirty count: $(($pl | Where-Object { $_ -match 'dirty ItemData' }).Count)"
$pl | Where-Object { $_ -match '残留|重复的状态机|ResidualAgent' } | Select-Object -First 4
