$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false

function ScanAsm($path, $label) {
    Write-Host "===== $label ====="
    try { $asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path, $rp) } catch { Write-Host "  load failed: $_"; return }
    foreach ($mod in $asm.Modules) {
        foreach ($t in $mod.GetTypes()) {
            # find config manager types
            if ($t.FullName -match 'ConfigManager|DataManager|TableManager|ConfigData|DataManager') {
                Write-Host ("[ManagerType] {0}" -f $t.FullName)
            }
        }
    }
    # find methods/fields referencing Config_Item
    foreach ($mod in $asm.Modules) {
        foreach ($t in $mod.GetTypes()) {
            foreach ($m in $t.Methods) {
                try {
                    foreach ($ins in $m.Body.Instructions) {
                        if ($ins.Operand -is [Mono.Cecil.TypeReference] -and $ins.Operand.FullName -eq 'GameCore.HotUpdate.Config_Item') {
                            Write-Host ("  {0}::{1}  IL_{2} {3} {4}" -f $t.FullName, $m.Name, $ins.Offset.ToString('X4'), $ins.OpCode.Name, $ins.Operand.FullName)
                            break
                        }
                    }
                } catch {}
                foreach ($p in $m.Parameters) {
                    if ($p.ParameterType.FullName -eq 'GameCore.HotUpdate.Config_Item') {
                        Write-Host ("  [param] {0}::{1}({2})" -f $t.FullName, $m.Name, (($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','))
                        break
                    }
                }
            }
            foreach ($f in $t.Fields) {
                if ($f.FieldType.FullName -eq 'GameCore.HotUpdate.Config_Item' -or ($f.FieldType -is [Mono.Cecil.GenericInstanceType] -and $f.FieldType.FullName -match 'Config_Item')) {
                    Write-Host ("  [field] {0} :: {1} : {2}" -f $t.FullName, $f.Name, $f.FieldType.FullName)
                }
            }
        }
    }
    $asm.Dispose()
}

ScanAsm 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\plugins\xmod\SurvivalLog.dll' 'xmod SurvivalLog.dll'
ScanAsm 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\plugins\xmod\ModFramework.dll' 'xmod ModFramework.dll'
