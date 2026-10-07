$ErrorActionPreference = 'Stop'
$plug = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\plugins'

# 1) distinct cfgIds present in save
$cfgIds = @{}
Get-Content (Join-Path $plug '_diag_WorldItems.tsv') -Encoding UTF8 | Select-Object -Skip 1 | ForEach-Object {
    $c = $_ -split "`t"
    if ($c.Count -ge 3 -and $c[2] -match '^\d+$') { $cfgIds[[int]$c[2]] = $true }
}
Write-Host ("present config count: " + $cfgIds.Count)

# 2) matching config rows
$rows = @()
Get-Content (Join-Path $plug '_diag_Config_Item.tsv') -Encoding UTF8 | Select-Object -Skip 1 | ForEach-Object {
    $c = $_ -split "`t"
    if ($c.Count -ge 16 -and $c[0] -match '^\d+$' -and $cfgIds.ContainsKey([int]$c[0])) {
        $rows += [pscustomobject]@{
            ID=[int]$c[0]; Name=$c[2]; Cat=[int]$c[3]; Sub=[int]$c[4]; Stack=[int]$c[5]
            UseT=[int]$c[6]; Furn=[int]$c[7]; Weight=$c[8]; Price=$c[9]; Life=$c[10]
            Burnable=$c[11]; Plant=$c[12]; Trap=$c[13]; UseAction=$c[14]; CantUse=$c[15]
        }
    }
}
Write-Host ("matched rows: " + $rows.Count)
Write-Host ""
Write-Host "--- by category ---"
$rows | Group-Object Cat | Sort-Object { [int]$_.Name } | ForEach-Object {
    Write-Host ("Category {0}: {1}" -f $_.Name, $_.Count)
}
Write-Host ""
Write-Host "--- present items detail (ID Name Cat/Sub Stack UseTimes Furn UseAction) ---"
$rows | Sort-Object Cat, ID | ForEach-Object {
    Write-Host ("{0,6} {1,-16} Cat{2,-3}Sub{3,-3} Stack={4,-4} Use={5,-4} Furn={6,-5} Act={7}" -f $_.ID, $_.Name, $_.Cat, $_.Sub, $_.Stack, $_.UseT, $_.Furn, $_.UseAction)
}

# 3) full-table stackable categories
Write-Host ""
Write-Host "--- full table StackLimit>1 by category ---"
$stackable = @()
Get-Content (Join-Path $plug '_diag_Config_Item.tsv') -Encoding UTF8 | Select-Object -Skip 1 | ForEach-Object {
    $c = $_ -split "`t"
    if ($c.Count -ge 16 -and $c[5] -match '^\d+$' -and [int]$c[5] -gt 1) {
        $stackable += [pscustomobject]@{ID=[int]$c[0]; Name=$c[2]; Cat=[int]$c[3]; Stack=[int]$c[5]; Furn=[int]$c[7]}
    }
}
$stackable | Group-Object Cat | Sort-Object { [int]$_.Name } | ForEach-Object {
    Write-Host ("Category {0}: {1} stackable" -f $_.Name, $_.Count)
}
Write-Host ("stackable total: " + $stackable.Count)

# 4) full-table StackLimit value distribution
Write-Host ""
Write-Host "--- full table StackLimit value distribution (top 25) ---"
$all = @()
Get-Content (Join-Path $plug '_diag_Config_Item.tsv') -Encoding UTF8 | Select-Object -Skip 1 | ForEach-Object {
    $c = $_ -split "`t"
    if ($c.Count -ge 6 -and $c[5] -match '^-?\d+$') { $all += [int]$c[5] }
}
$all | Group-Object | Sort-Object Count -Descending | Select-Object -First 25 | ForEach-Object {
    Write-Host ("StackLimit={0}: {1}" -f $_.Name, $_.Count)
}
