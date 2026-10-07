$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Get-ChildItem 'E:\Game\Mod\mod_stack\release\*.zip' | ForEach-Object {
    $z = [IO.Compression.ZipFile]::OpenRead($_.FullName)
    $plugins = $z.Entries |
        Where-Object { $_.FullName.Replace('\', '/') -match 'plugins/' } |
        ForEach-Object { Split-Path $_.FullName -Leaf }
    $stack = $plugins | Where-Object { $_ -match 'StackLimit999' }
    $mark = if ($stack) { '  !!!含StackLimit999!!!' } else { '' }
    Write-Host ($_.Name + '  ->  [' + ($plugins -join ', ') + ']' + $mark)
    $z.Dispose()
}
