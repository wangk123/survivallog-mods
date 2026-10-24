# ModCenter 第三方 Mod 接入指南

> 面向 Survival Log 模组开发者。你的 mod 接入 ModCenter（游戏内 MOD 管理中心）后，玩家无需手编 cfg 文件即可在游戏内查看与修改配置、启停你的 mod。
>
> 接入是**分档可选**的：什么都不做也能被自动识别（Tier 0），加一个清单文件获得中文定制页面（Tier 1，推荐），需要软停等深度能力再上 Tier 2。
>
> 文档版本：mcSpec 1（接口冻结承诺见 §7）。

## 一、总览：三档接入怎么选

```
你的 mod 用 BepInEx 6 (IL2CPP) + ConfigFile 存配置？
├─ 是 → Tier 0（零工作）  自动进面板：通用控件页 + 重启级启停
│        └─ 想要中文命名/分组/生效徽标/排序？ → Tier 1（加一个 modcenter.json，零代码）
│               └─ 想要软停（停用立即还原，不用重启）/ 状态回报？ → Tier 2（实现一个约定类型）
└─ 否（无 cfg / 纯数据 mod） → 面板中仅显示启停开关（Tier 0 退化形态）
```

| | Tier 0 自动识别 | Tier 1 声明式清单 | Tier 2 深度对接 |
|---|---|---|---|
| 开发者工作量 | 零 | 写一个 JSON | 实现约定类型（源码单文件） |
| 页面观感 | 通用控件页（英文键名） | 准套件级（中文/分组/徽标/排序） | 同 Tier 1 + 状态回报 |
| 配置生效 | 写入 cfg，生效时机取决于你的 mod | 同左 + 面板如实标注生效级别 | 同左 |
| 启停 | 重启级（dll 禁用） | 重启级；声明支持软停则两层 | 软停（停用即还原原值）+ 重启级 |

**核心原则：ModCenter 永远只读写你 mod 已声明的 cfg 键，绝不造键、绝不碰你的存档/内部数据。** 不装 ModCenter 时你的 mod 一切照常——对接不引入任何硬依赖。

## 二、Tier 0：零对接自动识别

ModCenter 启动后反射枚举 BepInEx Chainloader 的全部插件（套件 9 mod 与 ModCenter 自身除外），凡有 `ConfigFile` 的自动生成通用页：

- **每条 cfg entry 自动渲染**：`bool`→开关；数值且带 `AcceptableValueRange`→滑条；数值无范围→数值框；`string`→文本框；`enum`→下拉；
- **描述即文档**：`ConfigDescription` 里的 Description 显示为该项副文本；
- **写入即事件**：面板改值直接写 `ConfigEntry.Value`，会触发你订阅的 `SettingChanged`——如果你的 mod 逐次读 `.Value` 或订阅了该事件，当场热生效；否则写入 cfg 文件待你的 mod 下次读取。面板徽标统一如实标注"⚙ 已写入 cfg（生效视该 mod）"；
- **启停为重启级**：标记停用后，游戏退出时 ModCenter 把你的 dll 重命名为 `<原名>.disabled`（BepInEx 只加载 .dll 后缀，即不加载）；重新启用改回。面板会明确提示"本次会话仍在运行，下次启动后不再加载"。

你唯一需要做的（强烈建议，非必须）：

1. 给每条 `Config.Bind` 带上清晰的中文 `ConfigDescription`（玩家在 Tier 0 页面能直接看到）；
2. 给数值项加 `AcceptableValueRange`（面板自动变滑条，防玩家填出非法值）；
3. 需要热生效的逻辑直接读 `ConfigEntry.Value` 或订阅 `SettingChanged`，不要启动时快照后不再看。

## 三、Tier 1：声明式清单（推荐，零代码）

在你 mod 的 csproj 里内嵌一个清单资源：

```xml
<ItemGroup>
  <EmbeddedResource Include="modcenter.json" />
</ItemGroup>
```

`modcenter.json` 全字段参考（v1）：

```json
{
  "mcSpec": 1,
  "id": "com.yourname.statsui",
  "display": "统计面板",
  "summary": "常驻显示资源与产量统计，可调刷新频率与布局。",
  "softDisable": false,
  "groups": [
    {
      "title": "显示",
      "items": [
        { "key": "UI:AlwaysOnTop", "label": "面板常显", "badge": "now", "note": "顶置于其他界面之上" },
        { "key": "UI:Scale", "label": "面板缩放", "badge": "now", "min": 0.5, "max": 2.0, "step": 0.05 }
      ]
    },
    {
      "title": "性能",
      "items": [
        { "key": "Perf:RefreshMs", "label": "刷新间隔（毫秒）", "badge": "now", "min": 100, "max": 5000, "step": 100 }
      ]
    }
  ]
}
```

字段说明：

| 字段 | 必填 | 说明 |
|---|---|---|
| `mcSpec` | 是 | 协议版本，当前 `1`。ModCenter 不认识的版本按 Tier 0 兜底，绝不报错 |
| `id` | 是 | 唯一标识，建议与 BepInEx GUID 一致 |
| `display` / `summary` | 是 | 面板显示名（可中文）/ 一句话简介 |
| `softDisable` | 否 | 默认 `false`。设 `true` 表示你的 mod 自带启停 gate（见 §5），面板会展示两层停用语义 |
| `groups[].title` | 是 | 分组标题 |
| `groups[].items[].key` | 是 | cfg 键，格式 `"Section:Key"`（对应 `Config.Bind("Section", "Key", ...)`）。**清单里没列出的键不出现在面板**——内部缓存键/调试键写进 cfg 但不进清单即可 |
| `label` / `note` | 是 / 否 | 中文显示名 / 项下补充说明小字 |
| `badge` | 否 | 生效级别 `now` / `reload` / `cold`，缺省 `cfg`（见 §6） |
| `min` / `max` / `step` | 否 | 覆盖滑条范围（缺省用你的 AcceptableValueRange） |
| `order` | 否 | 组/项排序权重，小的在前 |

**行为约定**：ModCenter 优先按清单渲染；清单键在实际 cfg 中不存在时该项自动隐藏（容忍版本差异）；同一 dll 多个 `modcenter.json` 取第一个，其余忽略并记日志。

## 四、Tier 2：深度对接（软停与状态回报）

### 4.1 Contracts 单文件（无 dll 依赖）

将下面这份源码**直接编进你的 mod**（源码共享，不引用任何外部程序集——与 Survival Log 套件 ModShared 同款模式）。ModCenter 按命名空间+类型名反射发现你：

```csharp
using System;
using System.Collections.Generic;

namespace ModCenter.Integration
{
    /// <summary>ModCenter 深度对接约定类型。
    /// 在你的插件类所在程序集定义 public static class ModCenterIntegration
    /// 并实现本接口即可被 ModCenter 发现（找不到时自动降级 Tier 1/0，不报错）。</summary>
    public interface IModCenterIntegration
    {
        /// <summary>协议版本，当前返回 1。</summary>
        int McSpec { get; }

        /// <summary>启停回调：enabled=false 时你的 mod 应立即停止全部生效行为
        /// （还原表值/卸下注入/放行官方逻辑），true 时恢复。
        /// 在 BepInEx 主线程调用。返回 true 表示已处理；返回 false 表示不支持软停
        /// （ModCenter 自动降级为重启级语义展示）。</summary>
        bool OnEnabledChanged(bool enabled);

        /// <summary>可选状态回报：返回给面板展示的运行时状态。
        /// 例：字节补丁安装失败时返回 failed 项让对应配置置灰。</summary>
        IReadOnlyList<McItemState> QueryStates();
    }

    /// <summary>单项运行时状态。</summary>
    public sealed class McItemState
    {
        /// <summary>cfg 键，格式 "Section:Key"；空串代表整个 mod。</summary>
        public string Key { get; set; } = "";
        /// <summary>ok=正常；failed=置灰并展示 Reason（如补丁未安装）。</summary>
        public string State { get; set; } = "ok";
        public string Reason { get; set; } = "";
    }
}
```

在你的插件程序集里：

```csharp
public static class ModCenterIntegration : ModCenter.Integration.IModCenterIntegration
{
    public int McSpec => 1;

    public bool OnEnabledChanged(bool enabled)
    {
        MyPlugin.Instance.SetActive(enabled); // 你的启停 gate：还原/恢复原值
        return true;
    }

    public IReadOnlyList<McItemState> QueryStates()
        => Array.Empty<McItemState>();
}
```

### 4.2 软停实现范式（与套件 mod 同款）

- 你的 mod 持有"进程内原值缓存"：首次改写游戏内存表/状态前记下原值；
- `OnEnabledChanged(false)`：把全部改动回写原值 → 停止 Ticker/注入/放行 Harmony prefix；
- `OnEnabledChanged(true)`：按当前 cfg 重新应用（幂等重算）；
- `Load()` 首行检查启停 cfg，false 直接 return（重启后彻底不加载）；
- 做不到立即还原的项（快照类字段），在清单里把徽标写成 `reload`，面板会如实告知玩家。

## 五、启停语义协议

| 你的支持 | 面板开关行为 | 实现方 |
|---|---|---|
| Tier 0/1（默认） | 标记停用 → 退出时 dll 改名 `.disabled` → 下次启动不加载；面板标注"重启级" | ModCenter |
| `softDisable: true`（Tier 1 声明 + 你自实现 gate） | 同上 + 面板展示两层文案（你保证关开关即还原） | 你 |
| Tier 2 `OnEnabledChanged` 返回 true | 关开关立即软停（你还原原值）+ 下次启动不加载（你 Load 首行 return） | 你 + ModCenter |

重启级禁用的工程细节（ModCenter 负责，知会你）：改名在进程退出钩子执行（运行中 dll 被锁）；强杀进程导致未改名时，下次启动 ModCenter 检测标记残留并提示玩家；ModCenter 除改名外不在你的目录写任何文件。

## 六、生效级别（徽标）定义

| badge | 面板显示 | 语义 | 什么时候标 |
|---|---|---|---|
| `now` | ⚡ 立即生效（≤5秒） | 改值后你的 mod 在秒级内应用 | 你逐次读 `.Value` 或订阅 `SettingChanged` |
| `reload` | 📁 读档后生效 | 表值即时改，但既有游戏对象要重读档才体现 | 改的是 `OnInit` 快照类字段 |
| `cold` | 🔄 重启后生效 | 只在启动期消费（如补丁安装判定） | 启动期一次性逻辑 |
| 缺省 `cfg` | ⚙ 已写入 cfg | 已写入，生效时机取决于你的 mod | 不确定时的诚实缺省 |

## 七、ModCenter 的兼容性承诺与边界

1. **接口冻结**：`mcSpec 1` 内的清单字段与 Contracts 接口签名保持不变；破坏性变更升 `mcSpec 2` 并对 1 保持兼容渲染；
2. **只动声明的键**：ModCenter 只写你清单/cfg 里已存在的键；不删键；不碰 BepInEx/config 之外的任何文件（唯一例外：启停的 `.disabled` 改名）；
3. **不拦截官方通道**：ModCenter 不挂 `WebUIVm.HandleCallback` 等官方消息链，与你的 UI 注入/消息监听互不干扰；
4. **无硬依赖**：不装 ModCenter，你的 mod（含 Tier 2）行为完全不变——约定类型只是静静躺在程序集里没人调用；
5. **不背书**：面板"第三方 MOD"分组与套件分组视觉区分，ModCenter 不为第三方 mod 的行为背书。

## 八、自测清单（对接完成后过一遍）

- [ ] 面板"第三方 MOD"分组出现你的 mod，显示名正确；
- [ ] 逐项核对：清单列出的键全部出现、没列的没出现；
- [ ] 改一个 `now` 徽标项 → 你的 mod ≤5 秒内实际生效（自己加日志确认）；
- [ ] 数值项滑条范围 = 你的 AcceptableValueRange 或清单覆盖值；
- [ ] Tier 0：Description 副文本正常显示；Tier 1：中文 label/分组/徽标正确；
- [ ] Tier 2：`OnEnabledChanged(false)` 后你的 mod 行为确实停止且可还原，`true` 恢复；
- [ ] 重启级停用 → 重启游戏 → 你的 mod 未加载（BepInEx 控制台无你的插件日志）；重新启用 → 恢复；
- [ ] 卸载 ModCenter 后你的 mod 独立运行正常。

## 九、FAQ

**Q：清单里能加 ModCenter 没有的控件类型吗（颜色选择器、按键绑定）？**
A：`mcSpec 1` 支持 开关/滑条/数值/文本/下拉。新控件类型走 `mcSpec 2` 提案（附 Issue 描述场景）。

**Q：cfg 键有中文 Section/Key 可以吗？**
A：可以，`key` 字段按 `"Section:Key"` 原样匹配（中文一致即可）。

**Q：玩家手改了 cfg 文件，面板会读到吗？**
A：会。面板每次打开时重新快照，不缓存旧值。

**Q：BepInEx cfg 首次运行会固化默认值，影响对接吗？**
A：不影响。ModCenter 写的是运行时 `ConfigEntry.Value`（改的是"当前值"），与默认值固化机制无关。

**Q：我的 mod 没有任何 cfg，只想被启停？**
A：Tier 0 即可——面板显示为"仅启停"页（与套件的柜子直达/灾前加速同款形态）。
