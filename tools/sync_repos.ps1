# ============ Snapshot sync: monorepo -> per-mod mirror repos under GitHub org ============
# Usage: tools\sync_repos.ps1 [-Mod <Name>] [-Create] [-ListOnly]
#   -Mod      sync one mod repo only (org profile repo not touched)
#   -Create   create missing repos first (one-time; requires gh login with repo scope)
#   -ListOnly show what would change; commit/push nothing
# Snapshot source = git HEAD of this monorepo (NEVER the working tree: author WIP stays out).
# Mirror repo layout: README.md, LICENSE, .gitignore, SurvivalLog.<Mod>\, ModShared\
# (csproj keeps its ..\ModShared\ relative links - both trees build identically).
# Design: docs\superpowers\specs\2026-10-28-repo-split-and-online-update-design.md
param(
    [string]$Mod = '',
    [switch]$Create,
    [switch]$ListOnly
)
$ErrorActionPreference = 'Stop'
$projRoot = 'E:\Game\Mod\mod_stack'
$tools    = Join-Path $projRoot 'tools'
$ghExe    = Join-Path $tools 'gh\bin\gh.exe'
$workRoot = Join-Path $env:TEMP 'slrepo_sync'

$man = Get-Content (Join-Path $tools 'repo_sync\manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$org = $man.org
if ($Mod -and -not (@($man.mods | Where-Object { $_.mod -eq $Mod }).Count)) {
    throw ('unknown mod in manifest: ' + $Mod)
}

function Ensure-Repo([string]$RepoName, [string]$Description) {
    & $ghExe api ('repos/' + $org + '/' + $RepoName) --silent
    if ($LASTEXITCODE -eq 0) { return }
    if (-not $Create) { throw ('repo missing, run once with -Create: ' + $org + '/' + $RepoName) }
    & $ghExe repo create ($org + '/' + $RepoName) --public
    if ($LASTEXITCODE -ne 0) { throw ('gh repo create failed: ' + $RepoName) }
    # description via API body file: keeps non-ASCII off the command line (PS5.1 + gh trap)
    $descJson = Join-Path $env:TEMP ('slrepo_desc_' + $RepoName + '.json')
    [IO.File]::WriteAllText($descJson, (ConvertTo-Json @{ description = $Description }), (New-Object Text.UTF8Encoding($false)))
    & $ghExe api ('repos/' + $org + '/' + $RepoName) -X PATCH --input $descJson
    if ($LASTEXITCODE -ne 0) { throw ('set description failed: ' + $RepoName) }
    & $ghExe repo edit ($org + '/' + $RepoName) --add-topic survival-log --add-topic bepinex --add-topic il2cpp
    if ($LASTEXITCODE -ne 0) { throw ('set topics failed: ' + $RepoName) }
}

function Sync-Repo($entry) {
    $repoName = $entry.repo
    $isMod = [bool]$entry.mod
    Ensure-Repo $repoName $entry.description

    # 1. stage snapshot from git HEAD (archive to zip file = binary-safe on PS5.1, no piping)
    $stage = Join-Path $workRoot ('stage_' + $repoName)
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item $stage -ItemType Directory -Force | Out-Null
    $archZip = Join-Path $workRoot ('arch_' + $repoName + '.zip')
    if ($isMod) {
        git -C $projRoot archive --format=zip --output=$archZip HEAD -- ('SurvivalLog.' + $entry.mod) ModShared
    } else {
        git -C $projRoot archive --format=zip --output=$archZip HEAD -- tools/repo_sync/profile
    }
    if ($LASTEXITCODE -ne 0) { throw ('git archive failed: ' + $repoName) }
    Expand-Archive $archZip -DestinationPath $stage
    if ($isMod) {
        Copy-Item (Join-Path $stage ('SurvivalLog.' + $entry.mod + '\README.md')) (Join-Path $stage 'README.md') -Force
        [IO.File]::WriteAllText((Join-Path $stage '.gitignore'), "bin/`r`nobj/`r`n", (New-Object Text.UTF8Encoding($false)))
    } else {
        # profile staging: flatten tools/repo_sync/profile/* to repo root
        $p = Join-Path $stage 'tools\repo_sync\profile'
        Get-ChildItem $p | Move-Item -Destination $stage -Force
        Remove-Item (Join-Path $stage 'tools') -Recurse -Force
    }
    Copy-Item (Join-Path $projRoot 'tools\repo_sync\LICENSE') (Join-Path $stage 'LICENSE') -Force

    # 2. local clone cache (byte-stable: autocrlf off)
    $clone = Join-Path $workRoot ('repo_' + $repoName)
    if (-not (Test-Path (Join-Path $clone '.git'))) {
        if (Test-Path $clone) { Remove-Item $clone -Recurse -Force }
        git clone ('https://github.com/' + $org + '/' + $repoName + '.git') $clone
        if ($LASTEXITCODE -ne 0) { throw ('git clone failed: ' + $repoName) }
        git -C $clone config core.autocrlf false
        $br = (git -C $clone symbolic-ref --short HEAD) | Select-Object -First 1
        if ("$br" -ne 'main') {
            git -C $clone branch -m main
            if ($LASTEXITCODE -ne 0) { throw ('branch rename to main failed: ' + $repoName) }
        }
    }

    # 3. apply snapshot into clone (.git protected by /XD)
    robocopy $stage $clone /MIR /XD .git /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw ('robocopy failed: ' + $repoName) }
    $dirty = @(git -C $clone status --porcelain)
    if ($dirty.Count -eq 0) { Write-Host ('  ' + $repoName + ': up to date'); return }
    if ($ListOnly) {
        Write-Host ('  ' + $repoName + ' WOULD SYNC:')
        $dirty | ForEach-Object { Write-Host ('    ' + $_) }
        return
    }
    $sha = (git -C $projRoot rev-parse --short HEAD) | Select-Object -First 1
    git -C $clone add -A
    git -C $clone commit -m ('sync from mod_stack @' + $sha)
    if ($LASTEXITCODE -ne 0) { throw ('commit failed: ' + $repoName) }
    git -C $clone push origin HEAD
    if ($LASTEXITCODE -ne 0) { throw ('push failed: ' + $repoName) }
    Write-Host ('  ' + $repoName + ': pushed (sync from mod_stack @' + $sha + ')')
}

New-Item $workRoot -ItemType Directory -Force | Out-Null
if ($Mod) {
    Sync-Repo ($man.mods | Where-Object { $_.mod -eq $Mod })
} else {
    foreach ($e in $man.mods) { Sync-Repo $e }
    Sync-Repo $man.profile
}
Write-Host 'done.'
