$p = "$env:USERPROFILE\AppData\LocalLow\LLS\SLGame\Player.log"
$pl = Get-Content $p -Encoding UTF8
$dirty = $pl | Where-Object { $_ -match 'dirty ItemData' }
"dirty total: $($dirty.Count)"
$dirty | Select-Object -First 15
"...(patterns)"
# summarize: ownerId ranges and cfg ids
$owners = @{}
$cfgs = @{}
foreach ($d in $dirty) {
    if ($d -match 'ownerId=(\d+).*ItemConfigId=(\d+)') {
        $o = [int]$Matches[1]; $c = $Matches[2]
        if (-not $owners.ContainsKey($o)) { $owners[$o] = 0 }
        $owners[$o]++
        if (-not $cfgs.ContainsKey($c)) { $cfgs[$c] = 0 }
        $cfgs[$c]++
    }
}
"distinct owners: $($owners.Count)  distinct cfgs: $($cfgs.Count)"
"cfg distribution: $($cfgs.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 8 | ForEach-Object { "$($_.Key)x$($_.Value)" } -join ', ')"
