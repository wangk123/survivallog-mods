using System.Collections.Generic;

namespace SurvivalLog.ModCenter;

/// <summary>元数据表条目：一个 mod 的面板页定义（spec §7 v4 表逐行转写）。</summary>
internal sealed class MetaMod
{
    public string Guid;  // BepInEx 插件 GUID（路由主键）
    public string Name;  // 面板显示名（中文）
    public string En;    // 内部名（短名路由兼容）
    public string Desc;  // 一句话简介
    public List<MetaGroup> Groups = new();
    public MetaItem OvrHost; // ovr 清单宿主项（动作加速 其他.Overrides；不进 groups items，可写）

    // 启停键（任务 13）：该 mod 总开关在【其自己 cfg】里的真实节/键——SetModEnabled 直写
    // 此 entry（默认 General:Enabled，与 T9-T12 九 mod 的 Bind 一致；个别 mod 键名不同时覆盖）。
    public string EnabledSection = "General";
    public string EnabledKey = "Enabled";
}

internal sealed class MetaGroup
{
    public string Title;   // 分组标题（显示；≠cfg 节名）
    public List<MetaItem> Items = new();
}

internal sealed class MetaItem
{
    public string Section;   // cfg 真实节名
    public string Key;       // 主键（路由/兜底锚点；联动项 = 宽度键）
    public string[] WriteKeys; // 实写键数组（联动=宽高两键等值写；null=单键 [Key]）
    public string Label;     // 面板中文名（l）
    public string Badge;     // 生效徽标（b）：now / reload / reboot / cfg
    public string Type;      // float/int/bool/sel（entry 在场时以反射 SettingType 为准，此处仅兜底）
    public object Def;       // v 兜底：entry 尚不存在时用（如 RoleUnlock Plan.* 两键待任务 8 落地）
    public double? Min, Max; // 范围兜底：AcceptableValueRange 缺失时用（对照 spec §7 表）
    public double? St;       // 滑条步进（AcceptableValue 不携带步进，永远用兜底）
    public string[] Opts;    // sel 选项兜底（string 型无 AcceptableValueList，如 UiFit.Mode）
    public string Note;      // 静态补充说明
    public string LinkedSuffix; // 联动项动态 note 后缀（前缀"当前 宽w×高h"由桥现场拼）

    public bool Linked => WriteKeys != null && WriteKeys.Length > 1;

    /// <summary>实写键（永不 null：未声明联动 = 单键 [Key]；TrySet/快照统一走此口）。</summary>
    public string[] WKeys => WriteKeys != null && WriteKeys.Length > 0 ? WriteKeys : new[] { Key };

    /// <summary>展示键 k（仅显示；写路由以 Section+WriteKeys 为准）。联动 = "节.键A + 键B"（原型 v4 同款）。</summary>
    public string K => Linked
        ? Section + "." + WriteKeys[0] + " + " + WriteKeys[1]
        : Section + "." + Key;
}

/// <summary>
/// 元数据表（任务 6）：9 mod 页面定义唯一蓝本 = spec §7 v4 九张表
/// （docs/superpowers/specs/2026-10-24-modcenter-design.md §7；结构对照原型 v4 MODS 数组）。
/// 纪律（作者 v3 评审 + spec §5.4）：**默认值与范围不写死为准**——快照时从
/// ConfigEntry.DefaultValue/AcceptableValueRange 反射合入；此处的 Def/Min/Max 仅在
/// AcceptableValueRange 缺失或 entry 尚不存在时兜底（数值照 spec §7 表抄，防漂移口径见 DESIGN §6）。
/// 两处对 spec 的实证修正（实证优先，硬规则 12）：
/// ① UiFit.Mode 合法值是 Scroll/Zoom/Off（ViewportFit.cs 描述明示；spec §7 表 "Fit" 为笔误）；
/// ② RoleUnlock Plan.ClearPool / Plan.VaseRecipe 两个 entry 任务 8 才新增，先按 spec 默认 true 兜底展示。
/// </summary>
internal static class Meta
{
    internal const string OvrSection = "其他";
    internal const string OvrKey = "Overrides";

    internal static readonly List<MetaMod> Mods = new()
    {
        new MetaMod
        {
            Guid = "com.local.survivallog.backpackexpand",
            Name = "背包扩容", En = "BackpackExpand",
            Desc = "一个倍率搞定：自动计算背包宽度与高度（超游戏上限自动封顶）；负重单独调。",
            Groups =
            {
                new MetaGroup
                {
                    Title = "扩容",
                    Items =
                    {
                        new MetaItem
                        {
                            Section = "Expand", Key = "WidthMult",
                            WriteKeys = new[] { "WidthMult", "HeightMult" },
                            Label = "背包倍率", Badge = "now", Type = "float",
                            Def = 1.5, Min = 1, Max = 3, St = 0.1,
                            LinkedSuffix = " · 拖动后宽高同步为新倍率，超上限自动封顶"
                        },
                        new MetaItem
                        {
                            Section = "Expand", Key = "BurdenMult",
                            Label = "负重倍率", Badge = "reload", Type = "int",
                            Def = 2, Min = 1, Max = 10, St = 1,
                            Note = "表值即时改，既有背包读档后体现"
                        }
                    }
                }
            }
        },
        new MetaMod
        {
            Guid = "com.local.survivallog.cabinetsize",
            Name = "柜子容量", En = "CabinetSize",
            Desc = "普通柜/冰箱/载具按原生格数分四档定宽高（小/中/大/特大），全局上限兜底；热改即时生效。",
            Groups =
            {
                new MetaGroup
                {
                    Title = "档位与上限（热改即时生效）",
                    Items =
                    {
                        new MetaItem
                        {
                            Section = "Global", Key = "MaxWidth", Label = "宽度上限",
                            Badge = "now", Type = "int", Def = 20, Min = 0, Max = 40, St = 1,
                            Note = "0=不封顶；默认 20=官方原生最大宽度（材料架列数）"
                        },
                        new MetaItem
                        {
                            Section = "Global", Key = "MaxHeight", Label = "高度上限",
                            Badge = "now", Type = "int", Def = 16, Min = 0, Max = 30, St = 1,
                            Note = "0=不封顶；默认 16=特大档高度"
                        },
                        new MetaItem { Section = "Tier.Small", Key = "Width", Label = "小档宽度", Badge = "now", Type = "int", Def = 14, Min = 4, Max = 40, St = 1, Note = "原生格数 ≤60 的柜子" },
                        new MetaItem { Section = "Tier.Small", Key = "Height", Label = "小档高度", Badge = "now", Type = "int", Def = 10, Min = 4, Max = 30, St = 1 },
                        new MetaItem { Section = "Tier.Medium", Key = "Width", Label = "中档宽度", Badge = "now", Type = "int", Def = 16, Min = 4, Max = 40, St = 1, Note = "原生格数 61~99" },
                        new MetaItem { Section = "Tier.Medium", Key = "Height", Label = "中档高度", Badge = "now", Type = "int", Def = 12, Min = 4, Max = 30, St = 1 },
                        new MetaItem { Section = "Tier.Large", Key = "Width", Label = "大档宽度", Badge = "now", Type = "int", Def = 18, Min = 4, Max = 40, St = 1, Note = "原生格数 100~149" },
                        new MetaItem { Section = "Tier.Large", Key = "Height", Label = "大档高度", Badge = "now", Type = "int", Def = 14, Min = 4, Max = 30, St = 1 },
                        new MetaItem { Section = "Tier.XLarge", Key = "Width", Label = "特大档宽度", Badge = "now", Type = "int", Def = 20, Min = 4, Max = 40, St = 1, Note = "原生格数 ≥150" },
                        new MetaItem { Section = "Tier.XLarge", Key = "Height", Label = "特大档高度", Badge = "now", Type = "int", Def = 16, Min = 4, Max = 30, St = 1 }
                    }
                }
            }
            // T17 对账注记：作者 CabinetSize v3 终表（d02e985）Bind 面 = Global.MaxWidth/MaxHeight/Preset/
            // ExcludeIds + Tier.Small..XLarge.Width/Height + Debug.*。此处只暴露档位与上限 10 键；
            // Preset（勿手改）/ExcludeIds（同 BP ExcludeIds 口径不暴露）/Debug.* 不进面板。
            // 旧倍率模型条目（Expand.*Mult + UiFit.*）已随 v3 档位模型退役——UiFit 系官方自适应接管。
        },
        new MetaMod
        {
            Guid = "com.local.survivallog.fridgechill",
            Name = "保鲜增强", En = "FridgeChill",
            Desc = "冰箱/保鲜类容器食材保鲜时长倍率；天赋保鲜上限可调。",
            Groups =
            {
                new MetaGroup
                {
                    Title = "保鲜",
                    Items =
                    {
                        new MetaItem
                        {
                            Section = "Chill", Key = "KeepMult",
                            Label = "保鲜时长倍率", Badge = "now", Type = "float",
                            Def = 2, Min = 1, Max = 50, St = 0.5
                        },
                        new MetaItem
                        {
                            Section = "Chill", Key = "MaxTotalMult",
                            Label = "天赋保鲜上限倍率", Badge = "now", Type = "float",
                            Def = 100, Min = 1, Max = 500, St = 1,
                            Note = "启动时字节补丁安装失败则置灰"
                        }
                    }
                }
            }
        },
        new MetaMod
        {
            Guid = "com.local.survivallog.quickaction",
            Name = "动作加速", En = "QuickAction",
            Desc = "全游戏动作耗时按分类统一加速（毫秒，0=官方原值）；单个动作可加覆盖单独调。",
            Groups =
            {
                new MetaGroup
                {
                    Title = "分类耗时（毫秒，0=官方原值）",
                    Items =
                    {
                        MsItem("物品使用Ms", "物品使用"),
                        MsItem("家具功能Ms", "家具功能"),
                        MsItem("房屋维护Ms", "房屋维护"),
                        MsItem("杂项交互Ms", "杂项交互"),
                        MsItem("娱乐锻炼Ms", "娱乐锻炼")
                    }
                }
            },
            // 动作覆盖清单宿主：string "id:ms,id:ms" ↔ ovr:[{id,ms}]（解析在 ConfigBridge）
            OvrHost = new MetaItem
            {
                Section = OvrSection, Key = OvrKey, Label = "动作覆盖清单",
                Badge = "now", Type = "sel", Def = ""
            }
        },
        new MetaMod
        {
            Guid = "com.local.survivallog.cabineteverywhere",
            Name = "更好的储物柜", En = "CabinetEverywhere",
            Desc = "全容器作为制作/烹饪/酿造等取料源直达；柜子页签标签筛选与场景固定视图。仅支持启用/停用。",
            Groups = { } // 仅启停（v4 定稿；Access.*/Filter.*/Scenario.* 保留生效不暴露）
        },
        new MetaMod
        {
            Guid = "com.local.survivallog.roleunlock",
            Name = "角色解锁", En = "RoleUnlock",
            Desc = "角色专属内容逐项解锁；关闭即还原官方门槛。",
            Groups =
            {
                new MetaGroup
                {
                    Title = "藤蔓角色",
                    Items =
                    {
                        new MetaItem { Section = "Vine", Key = "UnlockVine", Label = "解锁藤蔓角色", Badge = "now", Type = "bool", Def = true },
                        new MetaItem
                        {
                            Section = "Vine", Key = "SkipDefenseGate", Label = "藤蔓免防御等级门槛",
                            Badge = "now", Type = "bool", Def = false,
                            Note = "开启后跳过官方解锁藤蔓所需的防御等级条件"
                        },
                        new MetaItem { Section = "Vine", Key = "UnlockFlowerSeeds", Label = "解锁藤蔓专属花种子", Badge = "now", Type = "bool", Def = false }
                    }
                },
                new MetaGroup
                {
                    Title = "配方与规划",
                    Items =
                    {
                        new MetaItem { Section = "Defense", Key = "ShareDefenseRecipes", Label = "解锁防御系角色专属配方（全角色共享）", Badge = "now", Type = "bool", Def = true },
                        // 以下两 entry 任务 8 新增（spec §8-1「RoleUnlock 两项补开关」）；落地前快照按 spec 默认 true 兜底
                        new MetaItem { Section = "Plan", Key = "ClearPool", Label = "解锁角色专属生存规划（46 项功能类）", Badge = "now", Type = "bool", Def = true },
                        new MetaItem { Section = "Plan", Key = "VaseRecipe", Label = "解锁花瓶配方", Badge = "now", Type = "bool", Def = true }
                    }
                }
            }
        },
        new MetaMod
        {
            Guid = "com.local.survivallog.robotspeed",
            Name = "机器人提速", En = "RobotSpeed",
            Desc = "机器人移速/充电/续航/耗电/动作时长/货舱六维倍率 + 与手动动作对齐。",
            Groups =
            {
                new MetaGroup
                {
                    Title = "机器人提速",
                    Items =
                    {
                        new MetaItem { Section = "机器人提速", Key = "动作速度对齐手动", Label = "动作速度对齐手动", Badge = "now", Type = "bool", Def = true },
                        MultItem("移速倍率"),
                        MultItem("充电速率倍率"),
                        MultItem("续航倍率"),
                        MultItem("耗电降低倍率"),
                        MultItem("动作时长倍率"),
                        new MetaItem
                        {
                            Section = "机器人提速", Key = "货舱格数倍率", Label = "货舱格数倍率",
                            Badge = "now", Type = "float", Def = 2, Min = 0.5, Max = 10, St = 0.5,
                            Note = "面板重开即见"
                        }
                    }
                }
            }
        },
        new MetaMod
        {
            Guid = "com.local.survivallog.planpick",
            Name = "自选生存规划", En = "PlanPick",
            Desc = "生存规划改自选：解锁日全表候选 + 搜索 + 键盘；整数百分比加成。",
            Groups =
            {
                new MetaGroup
                {
                    Title = "花费",
                    Items =
                    {
                        new MetaItem
                        {
                            Section = "Cost", Key = "CostMultiplierPercent", Label = "加成百分比（100=原值）",
                            Badge = "now", Type = "int", Def = 200, Min = 100, Max = 300, St = 5
                            // 范围不用兜底：PlanPick Bind 自带 AcceptableValueRange<int>(100,300)，快照反射合入
                        }
                    }
                },
                new MetaGroup
                {
                    Title = "列表",
                    Items =
                    {
                        new MetaItem { Section = "List", Key = "ShowLocked", Label = "展示未解锁规划（置灰+原因）", Badge = "now", Type = "bool", Def = true },
                        new MetaItem { Section = "List", Key = "ShowUnpooled", Label = "展示不入池规划", Badge = "now", Type = "bool", Def = true }
                    }
                }
            }
        },
        new MetaMod
        {
            Guid = "com.local.survivallog.predisasterspeed",
            Name = "灾前加速", En = "PreDisasterSpeed",
            Desc = "灾变倒计时前的官方速度条增强（热键变档 + 真三倍校正）。仅支持启用/停用。",
            Groups = { } // 仅启停（顶部开关即全部，映射 General.Enabled；TrueThreeX/Hotkeys 不暴露）
        }
    };

    private static MetaItem MsItem(string key, string label) => new()
    {
        Section = "分类修改", Key = key, Label = label,
        Badge = "now", Type = "int", Def = 500, Min = 0, Max = 5000, St = 50
    };

    private static MetaItem MultItem(string key) => new()
    {
        Section = "机器人提速", Key = key, Label = key,
        Badge = "now", Type = "float", Def = 2, Min = 0.5, Max = 10, St = 0.5
    };
}
