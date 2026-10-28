# MOD 管理中心 ModCenter（Survival Log MOD）

> 作者 Night · 抖音 166660371 · 免费分享 · 不改存档 / 不碰游戏本体文件（它只写各 mod 自己的配置，见第三节）
> 版本随套件统一管理；适配游戏版本以仓库兼容说明为准。

## 一、这是干什么的

套件 mod 装多了，调个参数得一个个打开 `BepInEx\config` 里的记事本文件改完再重启游戏。本 mod 在主菜单和游戏内 ESC 菜单各加一个「MOD 管理」入口，打开面板就能集中查看、开关、调参整套件 mod——滑条拖完多数几秒内生效，不用重启。注意：它是个管理器，本身不改变任何游戏玩法。

## 二、功能清单

- **双入口**：主菜单与游戏内 ESC 设置菜单各一个入口按钮，对局中也能随时打开调参。
- **集中管理套件 9 个 mod**（背包扩容、柜子容量、保鲜增强、动作加速、储藏柜直达、角色解锁、机器人提速、自选规划、灾前加速）：开关、滑条、下拉直改配置，多数即时生效；面板每项标注生效时机（即时 / 读档后 / 重启后）。
- **正规写配置**：面板改的就是各 mod 自己的 `BepInEx\config` 配置文件，关面板自动保存（约 3 秒防抖落盘）；手改文件与面板互不冲突。
- **第三方 mod 三档接入**：未对接的第三方 dll 自动读取其配置生成通用设置页（能看能改）；带接入清单的按对方作者定义的中文页面展示；深度对接的开关可即时生效。
- **第三方 dll 一键禁用/启用**：禁用 = 把该 dll 改名为 `.disabled`（只改名、绝不删除文件），下次启动游戏不再加载；随时在面板里改回原名。

## 三、安全与还原

- 不改存档 / 不改变游戏玩法（本 mod 是管理器）/ 删 dll 即还原（入口按钮消失）。
- 面板的改动只写 `BepInEx\config\` 下各 mod 自己的配置文件（这正是它的功能），不碰游戏本体文件。
- 禁用第三方 mod = 该 dll 改名为 `.disabled`（只改名不删除）；重新启用即改回。若禁用后直接卸载本 mod，该 dll 保持 `.disabled` 状态，手动去掉文件名后缀即可恢复。
- 卸载本 mod 后，各 mod 保持面板所改的配置值（那些是各 mod 的正式配置，不是本 mod 的私有数据）。
- 面板打开期间键盘（含中文输入法）交给面板，游戏快捷键暂不触发；关闭面板即还原。

## 四、安装与卸载

**前置**：Steam 正版《生存日志》（AppID 4164790）+ BepInEx 6（IL2CPP）注入链。
- **Full 完整包（推荐新玩家）**：解压到游戏根目录（Steam 库 `steamapps\common\Survival Log`），自带 BepInEx 6（IL2CPP）基座，装完即用；
- **PluginOnly 纯插件包**：已装 BepInEx 6 的玩家，把 `SurvivalLog.ModCenter.dll` 放进 `<游戏目录>\BepInEx\plugins\`。

**卸载** = 删掉该 dll（Full 包玩家要彻底清基座：再删 `BepInEx\` 目录、`winhttp.dll`、`dotnet\` 目录；存档不受影响），入口按钮消失；各 mod 配置保持面板所改值，被禁用的第三方 dll 需手动去掉 `.disabled` 后缀恢复。游戏更新后失效属正常，等套件适配新版即可；本 mod 不改任何游戏文件。

## 五、配置说明

配置文件：`BepInEx\config\com.local.survivallog.modcenter.cfg`。本 mod 面板管理的是其他所有 mod 的配置，自身几乎无可调项，用记事本可改。

| 节.键 | 默认 | 说明（人话） |
|---|---|---|
| Debug.Probe | false | 开发诊断探针（对配置读写通道做一轮自检并把证据写到 BepInEx 目录的 ModCenter_probe.txt）。玩家长开无意义，验完请改回 false |

面板还会在自己的 cfg 里记录被禁用的第三方 dll 名单（`[DisabledTp]` 节，由面板自动维护，无需手改）。

## 六、English

**Mod Center** — A unified in-game panel to view, toggle and tune every mod in this suite (and manage third-party plugins).

**Features**
- One entry in the main menu and one in the in-game ESC menu; open the panel anytime.
- Manage all 9 suite mods (backpack, cabinet capacity, fridge, action speed, cabinet access, role unlock, robot speed, plan pick, pre-disaster speed): toggles, sliders and dropdowns write the real config, most take effect within seconds without restarting; each item shows when it applies (instant / after loading / after reboot).
- Writes each mod's own config file under BepInEx\config (debounced autosave); manual edits and panel edits stay in sync.
- Third-party plugins at three levels: auto-generated generic settings page, manifest-defined Chinese pages, or deep-integration with instant on/off.
- One-click disable/enable for third-party DLLs: disable = rename to `.disabled` (never deleted); re-enable anytime in the panel.

**Requirements / Install / Uninstall**
Survival Log (Steam AppID 4164790) + BepInEx 6 IL2CPP. Install the Full package (bundled BepInEx 6 base — extract into the game root, `steamapps\common\Survival Log`) or the PluginOnly package (drop `SurvivalLog.ModCenter.dll` into `BepInEx\plugins\` if BepInEx 6 is already installed). Uninstall = delete the DLL; the menu entries disappear and every mod keeps its current config. Third-party DLLs disabled through the panel stay renamed to `.disabled` — remove the suffix to re-enable them.

**Configuration**
Config file: `BepInEx\config\com.local.survivallog.modcenter.cfg`. The panel manages every other mod's config; the only local key is a debug probe that should stay false.

**Credits**
By Night (Douyin 166660371). Free for personal use. Do NOT re-upload or redistribute anywhere without permission.
