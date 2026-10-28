# ============ Per-mod release pipeline (GitHub org channel) ============
# Usage: tools\release_mod.ps1 -Mod <Name> [-Yes] [-UseExistingZip]
#   -Mod            mod name as in version.json mods map (e.g. QuickAction)
#   -Yes            skip confirmation prompt
#   -UseExistingZip skip build+package, release release\SurvivalLog.X_v<ver>.zip as-is
#                   (backfill only; artifact must already exist and match HEAD)
# Version source: version.json -> mods.<Name> (author-defined ONLY).
# Gate: SurvivalLog.<Name>\CHANGELOG.md must contain a "## v<version>" section.
# Nexus Mods upload is NEVER automated here (separate author-confirmed step, docs/08).
param(
    [Parameter(Mandatory = $true)][string]$Mod,
    [switch]$Yes,
    [switch]$UseExistingZip
)
$ErrorActionPreference = 'Stop'
$projRoot = 'E:\Game\Mod\mod_stack'
$tools    = Join-Path $projRoot 'tools'
$ghExe    = Join-Path $tools 'gh\bin\gh.exe'
$relRoot  = Join-Path $projRoot 'release'
$repo     = 'survivallog-mods/' + $Mod

function Set-SrcText($path, $text) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $enc = New-Object System.Text.UTF8Encoding($hasBom)
    [IO.File]::WriteAllText($path, $text, $enc)
}

# ---- 1. version + changelog gate ----
$vj = Get-Content (Join-Path $projRoot 'version.json') -Raw | ConvertFrom-Json
$ver = $vj.mods.$Mod
if (-not $ver) { throw ('version.json mods has no entry: ' + $Mod) }
$modDir = Join-Path $projRoot ('SurvivalLog.' + $Mod)
$logPath = Join-Path $modDir 'CHANGELOG.md'
if (-not (Test-Path $logPath)) { throw ('missing: ' + $logPath) }
$logTxt = [IO.File]::ReadAllText($logPath)
if ($logTxt -notmatch (('##\s*v' + [regex]::Escape($ver)) + '\b')) {
    throw ($modDir + '\CHANGELOG.md has no v' + $ver + ' section. Add it first.')
}
$zip = Join-Path $relRoot ('SurvivalLog.' + $Mod + '_v' + $ver + '.zip')
if (-not $Yes) {
    $a = Read-Host ('Release ' + $Mod + ' v' + $ver + ' to ' + $repo + ' ? [Y/N]')
    if ($a -notmatch '^[Yy]') { throw 'cancelled' }
}

if ($UseExistingZip) {
    if (-not (Test-Path $zip)) { throw ('existing zip missing: ' + $zip) }
    Write-Host ('using existing artifact: ' + $zip)
} else {
    # ---- 1b. clean-tree gate: released source must be committed ----
    # allowed dirty: CHANGELOG.md + version.json (release-prep files); everything else blocks.
    $dirty = @(git -C $projRoot status --porcelain -- version.json ('SurvivalLog.' + $Mod) ModShared)
    $blocking = @($dirty | Where-Object { $_ -notmatch 'CHANGELOG\.md\s*$' -and $_ -notmatch 'version\.json\s*$' })
    if ($blocking.Count -gt 0) {
        $blocking | ForEach-Object { Write-Host ('  ' + $_) }
        throw ('uncommitted changes in SurvivalLog.' + $Mod + ' or ModShared - commit or stash first. Never release from a dirty tree.')
    }

    # ---- 2. sync version constants from version.json.mods ----
    $csproj = Join-Path $modDir ('SurvivalLog.' + $Mod + '.csproj')
    $t  = [IO.File]::ReadAllText($csproj)
    $t2 = [regex]::Replace($t, '(<Version>)[\d.]+(</Version>)', ('${1}' + $ver + '${2}'))
    if ($t2 -ne $t) { Set-SrcText $csproj $t2; Write-Host ('csproj version -> ' + $ver) }
    $plug = Get-ChildItem $modDir -Filter '*Plugin.cs' | Select-Object -First 1
    $c = $null; $c2 = $null
    if ($plug) {
        $c  = [IO.File]::ReadAllText($plug.FullName)
        $c2 = [regex]::Replace($c, '(public const string Version\s*=\s*")[\d.]+(")', ('${1}' + $ver + '${2}'))
        if ($c2 -ne $c) { Set-SrcText $plug.FullName $c2; Write-Host ($plug.Name + ' -> ' + $ver) }
    }

    # ---- 2b. surgical release commit: version stamps + release-prep files only ----
    $relFiles = @()
    if ($t2 -ne $t) { $relFiles += ('SurvivalLog.' + $Mod + '\SurvivalLog.' + $Mod + '.csproj') }
    if ($plug -and $c2 -ne $c) { $relFiles += ('SurvivalLog.' + $Mod + '\' + $plug.Name) }
    foreach ($l in $dirty) {
        $p = ($l -replace '^..\s+', '')
        if ($p -match 'CHANGELOG\.md$' -or $p -eq 'version.json') { $relFiles += $p }
    }
    $relFiles = @($relFiles | Select-Object -Unique)
    if ($relFiles.Count -gt 0) {
        git -C $projRoot add -- $relFiles
        if ($LASTEXITCODE -ne 0) { throw 'git add release files failed' }
        git -C $projRoot commit -m ('release(' + $Mod + '): v' + $ver) -- $relFiles
        if ($LASTEXITCODE -ne 0) { throw 'git commit release failed' }
        Write-Host ('release commit: ' + ($relFiles -join ', '))
    }

    # ---- 3. build ----
    $dotnetExe = Join-Path $projRoot 'sdk\dotnet\dotnet.exe'
    if (-not (Test-Path $dotnetExe)) { $dotnetExe = 'dotnet' }
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    Push-Location $modDir
    try { & $dotnetExe build -c Release --nologo | Out-Host }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw ('build failed: SurvivalLog.' + $Mod) }

    # ---- 4. package (reuses existing packaging incl. cfg normalization) ----
    if ($Mod -eq 'QuickAction') {
        & (Join-Path $tools 'make_quickaction_release.ps1')
    } else {
        & (Join-Path $tools 'make_mods_release.ps1') -Mod $Mod
    }
    if (-not (Test-Path $zip)) { throw ('zip missing after packaging: ' + $zip) }
}

# ---- 5. update.json contract asset (fixed asset name; spec clause 6) ----
$md5 = (Get-FileHash $zip -Algorithm MD5).Hash.ToLower()
$gk = Get-ChildItem (Join-Path $projRoot 'gamekb') -Directory |
    Where-Object { $_.Name -match '^\d{4}-\d{2}-\d{2}_\d+$' } |
    Sort-Object Name -Descending | Select-Object -First 1
if (-not $gk) { throw 'no gamekb snapshot dir found (expected name like 2026-10-23_26157732)' }
$updatePath = Join-Path $env:TEMP 'update.json'
$uj = @{ mod = $Mod; version = $ver; asset = (Split-Path $zip -Leaf); md5 = $md5; gamekb = $gk.Name } | ConvertTo-Json
[IO.File]::WriteAllText($updatePath, $uj, (New-Object Text.UTF8Encoding($false)))
Write-Host $uj

# ---- 6. snapshot sync this mod's repo (source at HEAD) ----
& (Join-Path $tools 'sync_repos.ps1') -Mod $Mod

# ---- 7. release notes from mod CHANGELOG ----
$notes = [regex]::Match($logTxt, ('(?s)##\s*v' + [regex]::Escape($ver) + '\b.*?(?=\r?\n(---|#{1,2}\s)|\z)')).Value.Trim()
$notesFile = Join-Path $env:TEMP ('relnotes_' + $Mod + '_v' + $ver + '.md')
[IO.File]::WriteAllText($notesFile, $notes, (New-Object Text.UTF8Encoding($false)))

# ---- 8. tag + GitHub release with both assets ----
& $ghExe release create ('v' + $ver) --repo $repo --title ('v' + $ver) --notes-file $notesFile $zip $updatePath
if ($LASTEXITCODE -ne 0) { throw ('gh release create failed: ' + $repo) }
Write-Host ('=== ' + $Mod + ' v' + $ver + ' released: https://github.com/' + $repo + '/releases/tag/v' + $ver + ' ===')
Write-Host 'Nexus Mods (if desired) stays a separate author-confirmed step (docs/08). Not automated.'
