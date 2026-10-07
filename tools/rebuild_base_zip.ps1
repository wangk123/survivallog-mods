$ErrorActionPreference = 'Stop'
$projRoot = 'E:\Game\Mod\mod_stack'
# 版本号唯一来源：根目录 version.json（仅作者定义，脚本只读）
$ver = (Get-Content (Join-Path $projRoot 'version.json') -Raw | ConvertFrom-Json).suite
$dir = Join-Path $projRoot ('release\SurvivalLog.Base_v' + $ver)
$zipPath = Join-Path $projRoot ('release\SurvivalLog.Base_v' + $ver + '.zip')

$nSrc = (Get-ChildItem $dir -Recurse -File).Count
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $dir '*') -DestinationPath $zipPath -CompressionLevel Optimal

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
$files = $zip.Entries | Where-Object { $_.Name -ne '' }
Write-Host ("源文件数 $nSrc  zip 文件条目 " + $files.Count)

# cfg 补丁验证
$e = $zip.Entries | Where-Object { $_.FullName.Replace('\','/') -eq 'BepInEx/config/BepInEx.cfg' } | Select-Object -First 1
$sr = New-Object IO.StreamReader($e.Open()); $txt = $sr.ReadToEnd(); $sr.Close()
$disk = [regex]::Match($txt, '(?s)\[Logging\.Disk\].*?(?=\[|$)').Value
Write-Host ('Logging.Disk Enabled = ' + ([regex]::Match($disk, 'Enabled\s*=\s*(\w+)').Groups[1].Value))

# README 验证
$e2 = $zip.Entries | Where-Object { $_.FullName.Replace('\','/') -eq 'README.md' } | Select-Object -First 1
$sr2 = New-Object IO.StreamReader($e2.Open()); $rd = $sr2.ReadToEnd(); $sr2.Close()
Write-Host ('README 含杀毒提示: ' + $rd.Contains('杀毒') + '   含 dotnet 提示: ' + $rd.Contains('dotnet'))

# 关键入口文件
foreach ($k in @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'dotnet/coreclr.dll', 'BepInEx/core/BepInEx.Core.dll')) {
    $found = $zip.Entries | Where-Object { $_.FullName.Replace('\','/') -eq $k }
    Write-Host ((('OK  ') + $k) + $(if (-not $found) { '  MISSING!' }))
}
$zip.Dispose()
Write-Host ('ZIP: ' + [math]::Round((Get-Item $zipPath).Length / 1MB, 2) + ' MB')
