$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\Users\wangk\.zcode\workspace\default\mod_stack\tools\Cecil\Mono.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll')
$mod = $asm.MainModule

"===== ReduxUISystem fields (find WebUIVm ref) ====="
$t = $mod.GetType('GameCore.HotUpdate.ReduxUI.ReduxUISystem')
foreach ($f in $t.Fields) { if ($f.Name -match 'vm|Vm|web|Web|bridge|Bridge|logic|Logic') { "  $($f.FieldType.FullName) $($f.Name)" } }

"`n===== types matching Bridge ====="
foreach ($ty in $mod.Types) { if ($ty.Name -match 'WebViewBridge|WebBridge|JsBridge') { "TYPE: $($ty.FullName)" } }

"`n===== WebUIVm static-instance-like members ====="
$t2 = $mod.GetType('GameCore.HotUpdate.ReduxUI.WebUIVm')
foreach ($m in $t2.Methods) { if ($m.Name -match 'get_' ) { "  $($m.Name) -> $($m.ReturnType.Name)" } }
