$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

foreach ($t in $types) {
    if ($t.Name -in @('BaseEntity','BaseManager')) {
        Write-Host ("##### {0} : base {1}" -f $t.FullName, $t.BaseType.FullName)
        foreach ($m in $t.Methods) {
            if ($m.Name -match '^get_|^set_') { Write-Host ("  M {0} -> {1}" -f $m.Name, $m.ReturnType.Name) }
        }
    }
}

$im = $types | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.Battle.Logic.ItemManager' }
foreach ($f in $im.Fields) {
    if ($f.Name -in @('NativeFieldInfoPtr_Cache','NativeFieldInfoPtr_OwnerCache')) {
        Write-Host ("  {0} type: {1}" -f $f.Name, $f.FieldType.FullName)
    }
}
# find actual Cache property type
foreach ($m in $im.Methods) {
    if ($m.Name -eq 'get_Cache' -or $m.Name -eq 'get_OwnerCache') {
        $rt = $m.ReturnType
        $detail = $rt.FullName
        if ($rt -is [Mono.Cecil.GenericInstanceType]) {
            $detail = $rt.Name + '<' + (($rt.GenericArguments | ForEach-Object { $_.FullName }) -join ',') + '>'
        }
        Write-Host ("  PROP {0} -> {1}" -f $m.Name, $detail)
    }
}
$asm.Dispose()
