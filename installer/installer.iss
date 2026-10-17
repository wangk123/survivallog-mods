; 生存日志 MOD 安装器（Inno Setup 7）
; 基座必装（fixed），7 个 MOD 自选（柜子标签筛选已并入储藏柜直达），代码强制至少选一个。
; 文件源全部指向 release\ 下的正式版本目录。

#define MyAppName "生存日志 MOD"
; 版本号由编译参数注入（tools\build_installer.ps1 传 /DSuiteVer=x.y.z），单独编译时用默认值
#ifndef SuiteVer
#define SuiteVer "1.3.1"
#endif
#define MyAppVer SuiteVer
; 文件源根（release\ 正式版本目录）；编译参数可覆盖（本地验证用 /DRelRoot 指向现成 release 源）
#ifndef RelRoot
#define RelRoot "..\release"
#endif
; 作者署名/防盗引流（2026-10-14）：每页底部署名条（TNewStaticText）+ 欢迎页二维码名片，均点击直达作者抖音主页
#define DouyinUrl "https://v.douyin.com/z99n5ZQfkyw/"

[Setup]
AppId={{7B1E6A54-C93E-4F0A-9D62-7C4E2F5A8D10}
AppName={#MyAppName}
AppVersion={#MyAppVer}
AppPublisher=Night（抖音号 166660371）· 免费分享，谨防盗版收费
AppPublisherURL={#DouyinUrl}
DefaultDirName={code:GetGameDir}
AppendDefaultDirName=no
UsePreviousAppDir=no
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
; Inno 7 默认自动跳过欢迎页 → 显式保留（欢迎页要展示抖音名片）
DisableWelcomePage=no
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
Name: "recommended"; Description: "推荐安装（背包/柜子含冰箱/保鲜扩容 + 柜子直达（含场景视图/标签筛选）与负重 + 吃/交互/维护提速 + 机器人提速）"
Name: "full"; Description: "全部安装（基座 + 全部 MOD）"
Name: "custom"; Description: "自选安装"; Flags: iscustom

[Components]
Name: "base"; Description: "MOD 框架基座（必装，不装它任何 MOD 都不生效）"; Types: recommended full custom
Name: "mod_backpack"; Description: "背包扩容：格子宽高各×1.5（约2.2倍面积），负重×2"; Types: recommended full custom
Name: "mod_cabinet"; Description: "柜子扩容：家里全部柜子（置物架/柜子/金属柜/工具柜/床头柜/衣柜等）+冰箱格子宽高各×1.5，新增柜子/冰箱自动跟上（原冰箱扩容已并入）"; Types: recommended full custom
Name: "mod_chill"; Description: "冰箱保鲜：保鲜5倍→10倍"; Types: recommended full custom
Name: "mod_cabaccess"; Description: "储藏柜直达（直达+智能视图+筛选）：储物柜/橱柜/金属柜进工作台/无人机交易/烹饪等界面，制作自动取料；按界面场景视图（食物/材料）自动收窄页签，或下拉按标签筛选柜子页签（原「柜子标签筛选」mod 已并入本组件）"; Types: recommended full custom
Name: "mod_cabburden"; Description: "柜子负重解放：橱柜/床头柜/储物柜等全部储物家具负重×1000，格子没满就能继续放"; Types: recommended full custom
Name: "mod_robotspeed"; Description: "机器人提速：修理/整理/种植机器人不再比手动慢（动作对齐手速+移速/充电/续航翻倍+三台货舱扩容；不含插槽与模块）"; Types: recommended full custom
Name: "mod_quick"; Description: "动作提速：按游戏官方分类统一提速，周期结算收益的动作自动保护"; Types: recommended full custom
Name: "mod_quick\qa_item"; Description: "物品使用：吃/喝/品尝/吞咽/药品 → 0.5秒（约2750条）"; Types: recommended full custom
Name: "mod_quick\qa_misc"; Description: "杂项交互：搜查/撬锁/翻找/开关电器/拾取/家务 → 0.5秒（约135条）"; Types: recommended full custom
Name: "mod_quick\qa_maint"; Description: "房屋维护：布置/安装/移动/拆除陷阱、安装家具 → 0.5秒（约78条）"; Types: recommended full custom
Name: "mod_quick\qa_furn"; Description: "家具功能：拆封包裹/种植/烹饪/升级大门/改装 → 0.5秒（约900条）"; Types: recommended full custom
Name: "mod_quick\qa_fun"; Description: "娱乐锻炼：看书/听音乐/按摩/运动/洗澡/空调吹风 → 0.5秒（约173条）"; Types: recommended full custom

[InstallDelete]
; 安装前预清理：先移除本项目全部插件（无论本次是否勾选），再安装所选组件——
; 保证最终落地状态与勾选严格一致（解决"取消勾选不移除"）。只删本项目的固定文件名：
; 不碰第三方插件、不碰游戏原生文件、不碰玩家 cfg（未再勾选的 mod 只留 1 个惰性 cfg；
; 唯一例外=旧柜子标签筛选 cfg——该 mod 已并入储藏柜直达，cfg 永久失效，见下）。
Type: files; Name: "{app}\BepInEx\plugins\SurvivalLog.BackpackExpand.dll"
Type: files; Name: "{app}\BepInEx\plugins\SurvivalLog.CabinetExpand.dll"
Type: files; Name: "{app}\BepInEx\plugins\SurvivalLog.FridgeExpand.dll"
Type: files; Name: "{app}\BepInEx\plugins\SurvivalLog.FridgeChill.dll"
Type: files; Name: "{app}\BepInEx\plugins\SurvivalLog.QuickAction.dll"
Type: files; Name: "{app}\BepInEx\plugins\SurvivalLog.CabinetEverywhere.dll"
Type: files; Name: "{app}\BepInEx\plugins\SurvivalLog.CabinetBurden.dll"
Type: files; Name: "{app}\BepInEx\plugins\SurvivalLog.RobotSpeed.dll"
Type: files; Name: "{app}\BepInEx\plugins\SurvivalLog.CabinetTagFilter.dll"
Type: files; Name: "{app}\BepInEx\config\quickaction.boot.ini"
; 旧独立筛选 mod 的 cfg（合并后插件已不存在，此 cfg 永久失效——清掉防误导；
; 其开关由储藏柜直达 cfg 的 [Filter] Enable 接管，默认 true=筛选重新开启）
Type: files; Name: "{app}\BepInEx\config\com.local.survivallog.cabinettagfilter.cfg"
Type: files; Name: "{app}\MOD说明\背包扩容.md"
Type: files; Name: "{app}\MOD说明\柜子扩容.md"
Type: files; Name: "{app}\MOD说明\冰箱扩容.md"
Type: files; Name: "{app}\MOD说明\冰箱保鲜.md"
Type: files; Name: "{app}\MOD说明\动作提速.md"
Type: files; Name: "{app}\MOD说明\储藏柜直达.md"
Type: files; Name: "{app}\MOD说明\柜子负重解放.md"
Type: files; Name: "{app}\MOD说明\机器人提速.md"
Type: files; Name: "{app}\MOD说明\柜子标签筛选.md"
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
; ---- 冰箱扩容已并入柜子扩容（2026-10-13）：此处只保留升级清理（InstallDelete 区的旧 dll/说明文件） ----
; ---- 冰箱保鲜 ----
Components: mod_chill; Source: "{#RelRoot}\SurvivalLog.FridgeChill_v{#SuiteVer}\BepInEx\plugins\SurvivalLog.FridgeChill.dll"; DestDir: "{app}\BepInEx\plugins"; Flags: ignoreversion
Components: mod_chill; Source: "{#RelRoot}\SurvivalLog.FridgeChill_v{#SuiteVer}\BepInEx\config\com.local.survivallog.fridgechill.cfg"; DestDir: "{app}\BepInEx\config"; Flags: onlyifdoesntexist
Components: mod_chill; Source: "{#RelRoot}\SurvivalLog.FridgeChill_v{#SuiteVer}\README.md"; DestDir: "{app}\MOD说明"; DestName: "冰箱保鲜.md"; Flags: ignoreversion
; ---- 储藏柜直达 ----
Components: mod_cabaccess; Source: "{#RelRoot}\SurvivalLog.CabinetEverywhere_v{#SuiteVer}\BepInEx\plugins\SurvivalLog.CabinetEverywhere.dll"; DestDir: "{app}\BepInEx\plugins"; Flags: ignoreversion
Components: mod_cabaccess; Source: "{#RelRoot}\SurvivalLog.CabinetEverywhere_v{#SuiteVer}\BepInEx\config\com.local.survivallog.cabineteverywhere.cfg"; DestDir: "{app}\BepInEx\config"; Flags: onlyifdoesntexist
Components: mod_cabaccess; Source: "{#RelRoot}\SurvivalLog.CabinetEverywhere_v{#SuiteVer}\README.md"; DestDir: "{app}\MOD说明"; DestName: "储藏柜直达.md"; Flags: ignoreversion
; ---- 柜子负重解放 ----
Components: mod_cabburden; Source: "{#RelRoot}\SurvivalLog.CabinetBurden_v{#SuiteVer}\BepInEx\plugins\SurvivalLog.CabinetBurden.dll"; DestDir: "{app}\BepInEx\plugins"; Flags: ignoreversion
Components: mod_cabburden; Source: "{#RelRoot}\SurvivalLog.CabinetBurden_v{#SuiteVer}\BepInEx\config\com.local.survivallog.cabinetburden.cfg"; DestDir: "{app}\BepInEx\config"; Flags: onlyifdoesntexist
Components: mod_cabburden; Source: "{#RelRoot}\SurvivalLog.CabinetBurden_v{#SuiteVer}\README.md"; DestDir: "{app}\MOD说明"; DestName: "柜子负重解放.md"; Flags: ignoreversion
; ---- 机器人提速（cfg 不预置：首跑按代码默认生成完整中文注释配置） ----
Components: mod_robotspeed; Source: "{#RelRoot}\SurvivalLog.RobotSpeed_v{#SuiteVer}\BepInEx\plugins\SurvivalLog.RobotSpeed.dll"; DestDir: "{app}\BepInEx\plugins"; Flags: ignoreversion
Components: mod_robotspeed; Source: "{#RelRoot}\SurvivalLog.RobotSpeed_v{#SuiteVer}\README.md"; DestDir: "{app}\MOD说明"; DestName: "机器人提速.md"; Flags: ignoreversion
; ---- 动作提速 ----
Components: mod_quick; Source: "{#RelRoot}\SurvivalLog.QuickAction_v{#SuiteVer}\BepInEx\plugins\SurvivalLog.QuickAction.dll"; DestDir: "{app}\BepInEx\plugins"; Flags: ignoreversion
Components: mod_quick; Source: "{#RelRoot}\SurvivalLog.QuickAction_v{#SuiteVer}\BepInEx\config\com.local.survivallog.quickaction.cfg"; DestDir: "{app}\BepInEx\config"; Flags: onlyifdoesntexist
Components: mod_quick; Source: "{#RelRoot}\SurvivalLog.QuickAction_v{#SuiteVer}\README.md"; DestDir: "{app}\MOD说明"; DestName: "动作提速.md"; Flags: ignoreversion

; 抖音名片（仅向导期署名展示用，dontcopy 不落地安装目录）
Source: "assets\douyin_night_card.bmp"; DestDir: "{tmp}"; Flags: dontcopy
; QuickAction cfg 按勾选直写脚本（仅安装期 ApplyQuickActionCfg 提取用，不落地安装目录）
Source: "fix_quickaction_cfg.ps1"; DestDir: "{tmp}"; Flags: dontcopy

[Run]
Filename: "steam://rungameid/4164790"; Description: "通过 Steam 启动游戏"; Flags: postinstall shellexec skipifsilent unchecked

[Code]
const
  GameAppId = '4164790';
  GameDirSuffix = 'steamapps\common\Survival Log';
  AuthorDouyinUrl = '{#DouyinUrl}';

var
  SigWelcome: TNewStaticText;
  SigInner: TNewStaticText;
  SigFinish: TNewStaticText;
  DouyinCardHost: TPanel;
  DouyinCard: TBitmapImage;

// 点击打开作者抖音主页（署名条与二维码名片共用）
procedure OpenDouyinPage(Sender: TObject);
var
  ErrorCode: Integer;
begin
  ShellExec('open', AuthorDouyinUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

// 每页底部署名条：TNewStaticText（窗口控件）。不能用 TLabel——
// 2026-10-14 渲染对照实测：Inno7 皮肤绘制会盖住 TLabel（图形控件），TNewStaticText 正常显示可点击。
function PutSignature(host: TNewNotebookPage): TNewStaticText;
begin
  Result := TNewStaticText.Create(host);
  Result.Parent := host;
  Result.Caption := 'MOD免费分享·请支持正版 | 作者 Night · 抖音号 166660371（点击打开主页）';
  Result.Color := clWhite;
  Result.Font.Color := clNavy;
  Result.Font.Size := 9;
  Result.Alignment := taCenter;
  Result.Cursor := crHand;
  Result.OnClick := @OpenDouyinPage;
  Result.SetBounds(ScaleX(12), host.Height - ScaleY(34), host.Width - ScaleX(24), ScaleY(22));
end;

// 每个向导界面常驻底部署名条（欢迎页/内页=目录与组件与安装中/完成页三个宿主）+
// 欢迎页右下角抖音二维码名片（裸 TBitmapImage，实测可显示可点击）。
// 仅创建控件、无交互弹窗，/VERYSILENT 静默安装同样安全。
procedure InitializeWizard();
begin
  // 组件页列表本来顶到页底，压低 34 让署名条零遮挡（其余页面底部本来就空）
  try
    WizardForm.ComponentsList.Height := WizardForm.ComponentsList.Height - ScaleY(34);
  except
  end;

  SigWelcome := PutSignature(WizardForm.WelcomePage);
  SigInner := PutSignature(WizardForm.InnerPage);
  SigFinish := PutSignature(WizardForm.FinishedPage);

  ExtractTemporaryFile('douyin_night_card.bmp');
  // 二维码名片必须用 TPanel（窗口控件）包着 TBitmapImage：欢迎页正文标签 WelcomeLabel2
  // 是不透明窗口控件，矩形一直延伸到页面下部，会把裸图形位图的下半截盖住（10-14 实测）。
  // 面板创建晚于标签 → 窗口层级在上，完整显示；再压低标签高度双保险。
  try
    WizardForm.WelcomeLabel2.Height := WizardForm.WelcomeLabel2.Height - ScaleY(150);
  except
  end;
  DouyinCardHost := TPanel.Create(WizardForm.WelcomePage);
  DouyinCardHost.Parent := WizardForm.WelcomePage;
  DouyinCardHost.SetBounds(WizardForm.WelcomePage.Width - ScaleX(180),
    WizardForm.WelcomePage.Height - ScaleY(283),
    ScaleX(164), ScaleY(243));
  DouyinCard := TBitmapImage.Create(DouyinCardHost);
  DouyinCard.Parent := DouyinCardHost;
  DouyinCard.Bitmap.LoadFromFile(ExpandConstant('{tmp}\douyin_night_card.bmp'));
  DouyinCard.Stretch := True;
  DouyinCard.SetBounds(ScaleX(2), ScaleY(2), ScaleX(160), ScaleY(239));
  DouyinCard.Cursor := crHand;
  DouyinCard.OnClick := @OpenDouyinPage;

  // 几何自证（仅 /LOG 运行时落盘，平时零开销）
  Log('WT: innerHost=' + IntToStr(WizardForm.InnerPage.Width) + 'x' + IntToStr(WizardForm.InnerPage.Height) +
      ' welcomeHost=' + IntToStr(WizardForm.WelcomePage.Width) + 'x' + IntToStr(WizardForm.WelcomePage.Height) +
      ' finishHost=' + IntToStr(WizardForm.FinishedPage.Width) + 'x' + IntToStr(WizardForm.FinishedPage.Height) +
      ' sigInner.T=' + IntToStr(SigInner.Top) + ' H=' + IntToStr(SigInner.Height) +
      ' card L=' + IntToStr(DouyinCard.Left) + ' T=' + IntToStr(DouyinCard.Top) +
      ' W=' + IntToStr(DouyinCard.Width) + ' H=' + IntToStr(DouyinCard.Height) +
      ' compList bottom=' + IntToStr(WizardForm.ComponentsList.Top + WizardForm.ComponentsList.Height));
end;


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

// 组件勾选 → 参数值（勾=500ms，不勾=0）
function CompMs(const comp: string): string;
begin
  if WizardIsComponentSelected(comp) then Result := '500' else Result := '0';
end;

// 按勾选直写 QuickAction cfg 五个分类键（v1.3.2 起替代 quickaction.boot.ini 引导）。
// 旧机制的值只作 Bind 默认值，被玩家 cfg 已固化的旧值压住——"取消勾选"永不生效
// （新玩家落地预置全 500 cfg、老玩家首跑已固化 500，两条路全中）。改为安装完成时
// 用 PowerShell 把五键值直写 cfg：勾=500/不勾=0，其余内容（Overrides/注释）原样保留。
// 用 PowerShell 是因为 Inno 原生 LoadStrings/SaveStrings 处理无 BOM UTF-8 中文 cfg
// 会按 ANSI 误读导致字节损坏；ps1 统一 .NET UTF-8 无 BOM 读写。
procedure ApplyQuickActionCfg();
var
  rc: Integer;
  ps1, cfg: string;
begin
  if not WizardIsComponentSelected('mod_quick') then exit;
  try
    ExtractTemporaryFile('fix_quickaction_cfg.ps1');
    ps1 := ExpandConstant('{tmp}\fix_quickaction_cfg.ps1');
    cfg := ExpandConstant('{app}\BepInEx\config\com.local.survivallog.quickaction.cfg');
    if Exec(ExpandConstant('{cmd}'),
        '/C powershell -NoProfile -ExecutionPolicy Bypass -File "' + ps1 + '"' +
        ' -CfgPath "' + cfg + '"' +
        ' -ItemMs '  + CompMs('mod_quick\qa_item')  +
        ' -FurnMs '  + CompMs('mod_quick\qa_furn')  +
        ' -MaintMs ' + CompMs('mod_quick\qa_maint') +
        ' -MiscMs '  + CompMs('mod_quick\qa_misc')  +
        ' -FunMs '   + CompMs('mod_quick\qa_fun'),
        '', SW_HIDE, ewWaitUntilTerminated, rc) and (rc = 0) then
      Log('QA cfg 五键已按勾选直写')
    else
      Log('QA cfg 直写未成功（exit=' + IntToStr(rc) + '），cfg 保持原值/预置 500');
  except
    Log('QA cfg 直写异常: ' + GetExceptionMessage);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    ApplyQuickActionCfg();
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
            WizardIsComponentSelected('mod_chill') or
            WizardIsComponentSelected('mod_cabaccess') or
            WizardIsComponentSelected('mod_cabburden') or
            WizardIsComponentSelected('mod_quick') or
            WizardIsComponentSelected('mod_robotspeed')) then
    begin
      MsgBox('基座之外至少需要选择一个 MOD（也可以全选）。', mbError, MB_OK);
      Result := False;
    end;
  end;
end;
