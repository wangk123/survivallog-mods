$exe = 'E:\Game\Mod\mod_stack\tools\il2cppdumper\Il2CppDumper.exe'
$ga  = 'E:\SteamLibrary\steamapps\common\Survival Log\GameAssembly.dll'
$md  = 'E:\SteamLibrary\steamapps\common\Survival Log\SurvivalLog_Data\il2cpp_data\Metadata\global-metadata.dat'
$out = 'E:\Game\Mod\mod_stack\tools\il2cpp_out'
$cmd = '/c ""' + $exe + '" "' + $ga + '" "' + $md + '" /O"' + $out + '"" < nul'
Start-Process -FilePath 'cmd.exe' -ArgumentList $cmd -Wait -NoNewWindow
Get-ChildItem $out | Select-Object Name, Length | Format-Table -AutoSize
