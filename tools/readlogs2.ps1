$c = Get-Content 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\LogOutput.log' -Encoding UTF8
"bepinex lastwrite: $((Get-Item 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\LogOutput.log').LastWriteTime) lines: $($c.Count)"
"== version/clean/sweep lines =="
$c | Select-String -Pattern 'Loading \[StackLimit999|存档清洗|清扫|F7' | ForEach-Object { $_.Line }
"== last 8 lines =="
$c | Select-Object -Last 8
'--- Player.log ---'
$p = "$env:USERPROFILE\AppData\LocalLow\LLS\SLGame\Player.log"
"player lastwrite: $((Get-Item $p).LastWriteTime)"
$pl = Get-Content $p -Encoding UTF8
"dirty count: $(($pl | Where-Object { $_ -match 'dirty ItemData' }).Count)"
$pl | Where-Object { $_ -match '残留|重复的状态机|Exception' } | Select-Object -First 5
