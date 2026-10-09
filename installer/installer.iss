; 生存日志 MOD 安装器（Inno Setup 7）
; 基座必装（fixed），5 个 MOD 自选，代码强制至少选一个。
; 文件源全部指向 release\ 下的正式版本目录。

#define MyAppName "生存日志 MOD"
; 版本号由编译参数注入（tools\build_installer.ps1 传 /DSuiteVer=x.y.z），单独编译时用默认值
#ifndef SuiteVer
#define SuiteVer "1.3.1"
#endif
#define MyAppVer SuiteVer
#define RelRoot "..\release"

[Setup]
AppId={{7B1E6A54-C93E-4F0A-9D62-7C4E2F5A8D10}
AppName={#MyAppName}
AppVersion={#MyAppVer}
AppPublisher=免费分享，仅供学习交流
DefaultDirName={code:GetGameDir}
AppendDefaultDirName=no
UsePreviousAppDir=no
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
OutputDir={#RelRoot}
OutputBaseFilename=生存日志MOD安装器_v{#MyAppVer}
UninstallDisplayName={#MyAppName}（卸载/修复）
Uninstallable=yes

[Languages]
Name: "chs"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Types]
; recommended 必须排第一：Inno 默认选中 [Types] 首项。
; "推荐安装"只含默认开的组件（4 扩容 + 吃/交互/维护提速），家具功能/娱乐锻炼默认不勾；
; 下拉可切"全部安装"一键全勾。custom 实测会镜像前一个所选类型，不能用作默认不勾的载体。
Name: "recommended"; Description: "推荐安装（4 个扩容 + 吃/交互/维护提速）"
Name: "full"; Description: "全部安装（基座 + 全部 MOD）"
Name: "custom"; Description: "自选安装"; Flags: iscustom

[Components]
Name: "base"; Description: "MOD 框架基座（必装，不装它任何 MOD 都不生效）"; Types: recommended full custom
Name: "mod_backpack"; Description: "背包扩容：格子宽高各×1.5（约2.2倍面积），负重×2"; Types: recommended full custom
Name: "mod_cabinet"; Description: "柜子扩容：工具柜/储物柜/金属柜格子宽高各×1.5"; Types: recommended full custom
Name: "mod_fridge"; Description: "冰箱扩容：全部可获得冰箱格子宽高各×1.5"; Types: recommended full custom
Name: "mod_chill"; Description: "冰箱保鲜：保鲜5倍→10倍"; Types: recommended full custom
Name: "mod_quick"; Description: "动作提速：按游戏官方分类统一提速，周期结算收益的动作自动保护"; Types: recommended full custom
Name: "mod_quick\qa_item"; Description: "物品使用：吃/喝/品尝/吞咽/药品 → 0.5秒（约2750条）"; Types: recommended full custom
Name: "mod_quick\qa_misc"; Description: "杂项交互：搜查/撬锁/翻找/开关电器/拾取/家务 → 0.5秒（约135条）"; Types: recommended full custom
Name: "mod_quick\qa_maint"; Description: "房屋维护：布置/安装/移动/拆除陷阱、安装家具 → 0.5秒（约78条）"; Types: recommended full custom
Name: "mod_quick\qa_furn"; Description: "家具功能：拆封包裹/种植/烹饪/升级大门/改装 → 0.5秒（约900条）"; Types: recommended full custom
Name: "mod_quick\qa_fun"; Description: "娱乐锻炼：看书/听音乐/按摩/运动/洗澡/空调吹风 → 0.5秒（约173条）"; Types: recommended full custom

[InstallDelete]
; 安装前预清理：先移除本项目全部插件（无论本次是否勾选），再安装所选组件——
; 保证最终落地状态与勾选严格一致（解决"取消勾选不移除"）。只删本项目的固定文件名：
; 不碰第三方插件、不碰游戏原生文件、不碰玩家 cfg（未再勾选的 mod 只留 1 个惰性 cfg）。
Type: files; Name: "{app}\BepInEx\plugins\SurvivalLog.BackpackExpand.dll"
Type: files; Name: "{app}\BepInEx\plugins\SurvivalLog.CabinetExpand.dll"
Type: files; Name: "{app}\BepInEx\plugins\SurvivalLog.FridgeExpand.dll"
Type: files; Name: "{app}\BepInEx\plugins\SurvivalLog.FridgeChill.dll"
Type: files; Name: "{app}\BepInEx\plugins\SurvivalLog.QuickAction.dll"
Type: files; Name: "{app}\BepInEx\config\quickaction.boot.ini"
Type: files; Name: "{app}\MOD说明\背包扩容.md"
Type: files; Name: "{app}\MOD说明\柜子扩容.md"
Type: files; Name: "{app}\MOD说明\冰箱扩容.md"
Type: files; Name: "{app}\MOD说明\冰箱保鲜.md"
Type: files; Name: "{app}\MOD说明\动作提速.md"
Type: dirifempty; Name: "{app}\MOD说明"

[Files]
; ---- 基座（必装）----
Components: base; Source: "{#RelRoot}\SurvivalLog.Base_v{#SuiteVer}\winhttp.dll"; DestDir: "{app}"; Flags: ignoreversion
Components: base; Source: "{#RelRoot}\SurvivalLog.Base_v{#SuiteVer}\doorstop_config.ini"; DestDir: "{app}"; Flags: ignoreversion
Components: base; Source: "{#RelRoot}\SurvivalLog.Base_v{#SuiteVer}\.doorstop_version"; DestDir: "{app}"; Flags: ignoreversion
Components: base; Source: "{#RelRoot}\SurvivalLog.Base_v{#SuiteVer}\dotnet\*"; DestDir: "{app}\dotnet"; Flags: ignoreversion recursesubdirs createallsubdirs
Components: base; Source: "{#RelRoot}\SurvivalLog.Base_v{#SuiteVer}\BepInEx\core\*"; DestDir: "{app}\BepInEx\core"; Flags: ignoreversion recursesubdirs createallsubdirs
; cfg 不覆盖玩家已有配置（重装/换组件时保留调好的数值）
Components: base; Source: "{#RelRoot}\SurvivalLog.Base_v{#SuiteVer}\BepInEx\config\BepInEx.cfg"; DestDir: "{app}\BepInEx\config"; Flags: onlyifdoesntexist
; ---- 背包扩容 ----
Components: mod_backpack; Source: "{#RelRoot}\SurvivalLog.BackpackExpand_v{#SuiteVer}\BepInEx\plugins\SurvivalLog.BackpackExpand.dll"; DestDir: "{app}\BepInEx\plugins"; Flags: ignoreversion
Components: mod_backpack; Source: "{#RelRoot}\SurvivalLog.BackpackExpand_v{#SuiteVer}\BepInEx\config\com.local.survivallog.backpackexpand.cfg"; DestDir: "{app}\BepInEx\config"; Flags: onlyifdoesntexist
Components: mod_backpack; Source: "{#RelRoot}\SurvivalLog.BackpackExpand_v{#SuiteVer}\README.md"; DestDir: "{app}\MOD说明"; DestName: "背包扩容.md"; Flags: ignoreversion
; ---- 柜子扩容 ----
Components: mod_cabinet; Source: "{#RelRoot}\SurvivalLog.CabinetExpand_v{#SuiteVer}\BepInEx\plugins\SurvivalLog.CabinetExpand.dll"; DestDir: "{app}\BepInEx\plugins"; Flags: ignoreversion
Components: mod_cabinet; Source: "{#RelRoot}\SurvivalLog.CabinetExpand_v{#SuiteVer}\BepInEx\config\com.local.survivallog.cabinetexpand.cfg"; DestDir: "{app}\BepInEx\config"; Flags: onlyifdoesntexist
Components: mod_cabinet; Source: "{#RelRoot}\SurvivalLog.CabinetExpand_v{#SuiteVer}\README.md"; DestDir: "{app}\MOD说明"; DestName: "柜子扩容.md"; Flags: ignoreversion
; ---- 冰箱扩容 ----
Components: mod_fridge; Source: "{#RelRoot}\SurvivalLog.FridgeExpand_v{#SuiteVer}\BepInEx\plugins\SurvivalLog.FridgeExpand.dll"; DestDir: "{app}\BepInEx\plugins"; Flags: ignoreversion
Components: mod_fridge; Source: "{#RelRoot}\SurvivalLog.FridgeExpand_v{#SuiteVer}\BepInEx\config\com.local.survivallog.fridgeexpand.cfg"; DestDir: "{app}\BepInEx\config"; Flags: onlyifdoesntexist
Components: mod_fridge; Source: "{#RelRoot}\SurvivalLog.FridgeExpand_v{#SuiteVer}\README.md"; DestDir: "{app}\MOD说明"; DestName: "冰箱扩容.md"; Flags: ignoreversion
; ---- 冰箱保鲜 ----
Components: mod_chill; Source: "{#RelRoot}\SurvivalLog.FridgeChill_v{#SuiteVer}\BepInEx\plugins\SurvivalLog.FridgeChill.dll"; DestDir: "{app}\BepInEx\plugins"; Flags: ignoreversion
Components: mod_chill; Source: "{#RelRoot}\SurvivalLog.FridgeChill_v{#SuiteVer}\BepInEx\config\com.local.survivallog.fridgechill.cfg"; DestDir: "{app}\BepInEx\config"; Flags: onlyifdoesntexist
Components: mod_chill; Source: "{#RelRoot}\SurvivalLog.FridgeChill_v{#SuiteVer}\README.md"; DestDir: "{app}\MOD说明"; DestName: "冰箱保鲜.md"; Flags: ignoreversion
; ---- 动作提速 ----
Components: mod_quick; Source: "{#RelRoot}\SurvivalLog.QuickAction_v{#SuiteVer}\BepInEx\plugins\SurvivalLog.QuickAction.dll"; DestDir: "{app}\BepInEx\plugins"; Flags: ignoreversion
Components: mod_quick; Source: "{#RelRoot}\SurvivalLog.QuickAction_v{#SuiteVer}\BepInEx\config\com.local.survivallog.quickaction.cfg"; DestDir: "{app}\BepInEx\config"; Flags: onlyifdoesntexist
Components: mod_quick; Source: "{#RelRoot}\SurvivalLog.QuickAction_v{#SuiteVer}\README.md"; DestDir: "{app}\MOD说明"; DestName: "动作提速.md"; Flags: ignoreversion

[Run]
Filename: "steam://rungameid/4164790"; Description: "通过 Steam 启动游戏"; Flags: postinstall shellexec skipifsilent unchecked

[Code]
const
  GameAppId = '4164790';
  GameDirSuffix = 'steamapps\common\Survival Log';

// 游戏进程检测：dll 被占用时预清理会静默失败，必须在动手前拦下。
// 精确匹配：只有当运行中的 SurvivalLog.exe 的路径位于本次安装目标目录下才拦截
// （沙箱/其他目录的测试安装不受干扰）。
function IsGameRunningAt(const appDir: string): Boolean;
var
  ResultCode: Integer;
  tmpFile: string;
  lines: TArrayOfString;
  i: Integer;
begin
  Result := False;
  tmpFile := ExpandConstant('{tmp}') + '\slproc.txt';
  Exec(ExpandConstant('{cmd}'),
    '/C powershell -NoProfile -Command "(Get-Process SurvivalLog -ErrorAction SilentlyContinue).Path" > "' + tmpFile + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if FileExists(tmpFile) and LoadStringsFromFile(tmpFile, lines) then
    for i := 0 to GetArrayLength(lines) - 1 do
      if (lines[i] <> '') and (Pos(Uppercase(appDir), Uppercase(lines[i])) = 1) then
      begin
        Result := True;
        exit;
      end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if IsGameRunningAt(ExpandConstant('{app}')) then
    Result := '检测到游戏正在运行（SurvivalLog.exe）。' + #13#10 +
              '请先完全退出游戏（含后台进程），再重新运行安装程序，' + #13#10 +
              '否则插件文件被占用无法替换。';
end;

// 目录里存在游戏 exe 才算命中
function TryDir(dir: string): string;
begin
  Result := '';
  if dir <> '' then
    if FileExists(AddBackslash(dir) + 'SurvivalLog.exe') then
      Result := dir;
end;

// 自动探测 Steam 游戏目录：HKCU SteamPath → libraryfolders.vdf 逐库找 appmanifest
function GetGameDir(Param: string): string;
var
  steam, vdf, lib, s: string;
  lines: TArrayOfString;
  i, j: integer;
begin
  Result := '';
  steam := '';
  RegQueryStringValue(HKEY_CURRENT_USER, 'Software\Valve\Steam', 'SteamPath', steam);
  if steam = '' then
    RegQueryStringValue(HKEY_LOCAL_MACHINE, 'SOFTWARE\WOW6432Node\Valve\Steam', 'InstallPath', steam);
  if steam <> '' then
  begin
    StringChangeEx(steam, '/', '\', True);
    Result := TryDir(AddBackslash(steam) + GameDirSuffix);
    if Result <> '' then exit;
    vdf := AddBackslash(steam) + 'steamapps\libraryfolders.vdf';
    if LoadStringsFromFile(vdf, lines) then
      for i := 0 to GetArrayLength(lines) - 1 do
      begin
        s := lines[i];
        j := Pos('"path"', s);
        if j > 0 then
        begin
          Delete(s, 1, j + 5);
          j := Pos('"', s);
          if j > 0 then
          begin
            Delete(s, 1, j);
            j := Pos('"', s);
            if j > 0 then
            begin
              lib := Copy(s, 1, j - 1);
              StringChangeEx(lib, '\\', '\', True);
              Result := TryDir(AddBackslash(lib) + GameDirSuffix);
              if Result <> '' then exit;
            end;
          end;
        end;
      end;
  end;
  // 探测失败：给一个经典默认路径，目录页会提示玩家自行浏览
  Result := 'C:\Program Files (x86)\Steam\steamapps\common\Survival Log';
end;

// 组件勾选 → 数值（勾=500ms，不勾=0）
function CompMs(const comp: string): string;
begin
  if WizardIsComponentSelected(comp) then Result := '500' else Result := '0';
end;

// 把动作提速分类勾选写成 ASCII 引导文件（mod 首次启动读取作为 cfg 默认值后自删；
// 玩家已有 cfg 键永远优先，重装不会覆盖调好的数值）
procedure WriteQuickActionBoot();
var
  boot: string;
  s: string;
begin
  boot := ExpandConstant('{app}\BepInEx\config\quickaction.boot.ini');
  if WizardIsComponentSelected('mod_quick') then
  begin
    s := 'itemMs='  + CompMs('mod_quick\qa_item')  + #13#10 +
         'furnMs='  + CompMs('mod_quick\qa_furn')  + #13#10 +
         'maintMs=' + CompMs('mod_quick\qa_maint') + #13#10 +
         'miscMs='  + CompMs('mod_quick\qa_misc')  + #13#10 +
         'funMs='   + CompMs('mod_quick\qa_fun')   + #13#10;
    SaveStringToFile(boot, s, False);
  end
  else
  begin
    // 本次未装动作提速：清掉可能残留的旧引导文件
    if FileExists(boot) then DeleteFile(boot);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    WriteQuickActionBoot();
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  // 静默安装（/VERYSILENT、/SILENT）时跳过一切交互校验，否则弹窗无人应答会挂死
  if WizardSilent() then exit;
  // 目录页：没有游戏 exe 时二次确认，防止装错地方
  if CurPageID = wpSelectDir then
  begin
    if not FileExists(AddBackslash(WizardDirValue()) + 'SurvivalLog.exe') then
      Result := MsgBox(
        '这个目录里没有找到 SurvivalLog.exe。' + #13#10 +
        '正确位置一般是 Steam\steamapps\common\Survival Log' + #13#10#13#10 +
        '仍要安装到这个目录吗？', mbConfirmation, MB_OKCANCEL) = IDOK;
  end;
  // 组件页：基座之外至少选一个 MOD
  if CurPageID = wpSelectComponents then
    if not WizardIsComponentSelected('base') then
    begin
      MsgBox('MOD 框架基座是所有 MOD 的运行前提，必须勾选。', mbError, MB_OK);
      Result := False;
      exit;
    end;
  begin
    if not (WizardIsComponentSelected('mod_backpack') or
            WizardIsComponentSelected('mod_cabinet') or
            WizardIsComponentSelected('mod_fridge') or
            WizardIsComponentSelected('mod_chill') or
            WizardIsComponentSelected('mod_quick')) then
    begin
      MsgBox('基座之外至少需要选择一个 MOD（也可以全选）。', mbError, MB_OK);
      Result := False;
    end;
  end;
end;
