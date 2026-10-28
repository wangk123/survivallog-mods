# 灾前加速 PreDisasterSpeed（Survival Log MOD）

> 作者 Night · 抖音 166660371 · 免费分享 · 不改存档 / 不改游戏文件
> 版本随套件统一管理；适配游戏版本以仓库兼容说明为准。

## 一、这是干什么的

灾前开荒阶段没有倍速条，种田、种藤蔓、整理家当只能按原速干等。本 mod 把官方灾后的五键倍速条（暂停/慢速/原速/二倍/三倍）原样搬到灾前界面上，点按钮或按快捷键都能变速，变速调用的是游戏自带的换档通道，开箱即用。

## 二、功能清单

- 官方五键倍速条完整出现在灾前界面左下角，暂停与慢速档也一并可用，样式与灾后同款
- 快捷键与官方一致：~ 慢速、1 原速、2 二倍、3 三倍、4 暂停（仅灾前界面显示期间生效）
- 三倍档与官方一致（×32）：官方"三倍"键在灾前本来就是快进档，本 mod 不改各档速度语义；想要真实 ×3 在配置开 `TrueThreeX`（默认关，重启生效）
- 睡眠跳夜不受影响：睡觉的时间加速保持官方 ×32 原样
- 灾前倒计时随倍速同步走快（游戏原生机制）；点击变速走游戏官方通道
- 进入灾变时自动归回 1 倍速，倍率不会带进灾后

## 三、安全与还原

- 不改存档 / 不改游戏文件 / 删 dll 即还原
- 工作方式：往灾前界面注入一条加速条，变速调用游戏自带的换档接口——不修改任何游戏文件，也不写存档
- 还原时机是"重启级"：关闭总开关（General.Enabled）或删除 dll 后，已在档的这局里注入的加速条可能还在（切场景后会自然消失），下次启动游戏即完全还原
- 睡眠跳夜、灾后官方倍速条均不受本 mod 影响

## 四、安装与卸载

**前置**：Steam 正版《生存日志》（AppID 4164790）+ BepInEx 6（IL2CPP）注入链。
- **Full 完整包（推荐新玩家）**：解压到游戏根目录（Steam 库 `steamapps\common\Survival Log`），自带 BepInEx 6（IL2CPP）基座，装完即用；
- **PluginOnly 纯插件包**：已装 BepInEx 6 的玩家，把 `SurvivalLog.PreDisasterSpeed.dll` 放进 `<游戏目录>\BepInEx\plugins\`。

**卸载** = 删掉该 dll（Full 包玩家要彻底清基座：再删 `BepInEx\` 目录、`winhttp.dll`、`dotnet\` 目录；存档不受影响），游戏回到原版（无灾前倍速条），下次启动游戏后完全还原。游戏更新后失效属正常，等套件适配新版即可；本 mod 不改任何游戏文件。

## 五、配置说明

配置文件：`BepInEx\config\com.local.survivallog.predisasterspeed.cfg`。真×3 校正与快捷键开关改动后即时生效；总开关为重启级（关闭后下次启动生效）。用记事本可改。

| 节.键 | 默认 | 说明（人话） |
|---|---|---|
| General.Enabled | true | 灾前加速条总开关（关闭=不注入不挂钩；对已在档对局下次启动生效）。 |
| General.TrueThreeX | false | 默认=false：三倍档与官方一致（×32 快进档）。true=手动三键按真实×3 生效（临时换档校正；睡眠跳夜保持官方×32 不变）。重启生效。 |
| Hotkeys.Enabled | true | 快捷键（仅灾前 HUD 显示期间生效，键位与官方一致）：~=慢速 1=原速 2=二倍 3=三倍 4=暂停。注意：网页输入框聚焦时按键仍会触发。 |
| Debug.Verbose | false | 注入/payload 明细日志（排查问题用，平时保持关闭）。 |

## 六、English

**Pre-Disaster Speed** — Brings the official five-button speed bar (pause / slow / 1x / 2x / 3x) to the pre-disaster HUD, with the official hotkeys.

**Features**
- The official speed bar appears in the pre-disaster HUD (bottom-left), pause and slow-speed gears included
- Hotkeys match the official ones: ~ slow, 1 normal, 2 double, 3 triple, 4 pause (active only while the pre-disaster HUD is shown)
- Optional true ×3 correction (default OFF): the official "triple" button is a ×32 fast-forward tier pre-disaster; enable TrueThreeX to make the manual triple button a real ×3
- Sleep time-skip is untouched and keeps the official ×32
- The pre-disaster countdown speeds up together with the clock (native game behavior); clicks go through the game's own speed-change channel
- Speed automatically resets to 1x at the disaster transition, so the multiplier never carries into the post-disaster phase

**Requirements / Install / Uninstall**
Survival Log (Steam AppID 4164790) + BepInEx 6 IL2CPP. Install the Full package (bundled BepInEx 6 base — extract into the game root, `steamapps\common\Survival Log`) or the PluginOnly package (drop `SurvivalLog.PreDisasterSpeed.dll` into `BepInEx\plugins\` if BepInEx 6 is already installed). Uninstall = delete the DLL; the game returns to vanilla (no pre-disaster speed bar). Restoration is restart-level — fully effective on the next game launch.

**Configuration**
Config file: `BepInEx\config\com.local.survivallog.predisasterspeed.cfg`. Master switch (takes effect on next launch), true-×3 correction and hotkey toggle (both instant).

**Credits**
By Night (Douyin 166660371). Free for personal use. Do NOT re-upload or redistribute anywhere without permission.
