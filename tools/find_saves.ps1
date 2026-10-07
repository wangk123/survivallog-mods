$ErrorActionPreference = 'SilentlyContinue'
$roots = @(
    "$env:USERPROFILE\AppData\LocalLow",
    "$env:USERPROFILE\AppData\Local",
    "$env:USERPROFILE\AppData\Roaming"
)
foreach ($r in $roots) {
    Get-ChildItem $r -Directory | Where-Object { $_.Name -match 'Survival|Log|Indie|Whale|survivallog' } | ForEach-Object {
        "DIR: $($_.FullName)"
    }
}
"`n=== search for save-ish files modified today ==="
foreach ($r in $roots) {
    Get-ChildItem $r -Recurse -Depth 3 -File -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -gt (Get-Date).AddHours(-12) -and ($_.Name -match 'save|Save|\.sav|slot') } |
        Select-Object -First 25 FullName, LastWriteTime, Length |
        ForEach-Object { "$($_.LastWriteTime)  $([math]::Round($_.Length/1024,1))KB  $($_.FullName)" }
}
