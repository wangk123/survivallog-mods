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
            Desc = "宽度/基础高度/负重三个明确数值统一应用全部背包（0=恢复官方原值）；天赋『背包加一行』照常每级+1；热改即时生效。",
            Groups =
            {
                new MetaGroup
                {
                    Title = "扩容数值（0=恢复官方原值）",
                    Items =
                    {
                        new MetaItem
                        {
                            Section = "Expand", Key = "Width", Label = "背包宽度",
                            Badge = "now", Type = "int", Def = 11, Min = 0, Max = 30, St = 1,
                            Note = "全部背包统一列数（原生最大 10）"
                        },
                        new MetaItem
                        {
                            Section = "Expand", Key = "Height", Label = "基础高度",
                            Badge = "now", Type = "int", Def = 10, Min = 0, Max = 30, St = 1,
                            Note = "初始背包与登山包行数；天赋『背包加一行』每级自动 +1，照常生效"
                        },
                        new MetaItem
                        {
                            Section = "Expand", Key = "Burden", Label = "负重上限",
                            Badge = "reload", Type = "int", Def = 48000, Min = 0, Max = 150000, St = 1000,
                            Note = "全部背包统一基础值，天赋加成照常叠加；读档后体现"
                        }
                    }
                },
                new MetaGroup
                {
                    Title = "防呆上限（0=不封顶）",
                    Items =
                    {
                        new MetaItem
                        {
                            Section = "Global", Key = "MaxWidth", Label = "宽度上限",
                            Badge = "now", Type = "int", Def = 16, Min = 0, Max = 30, St = 1,
                            Note = "Width 超出即夹到此值；过宽挤压背包页右侧栏"
                        },
                        new MetaItem
                        {
                            Section = "Global", Key = "MaxHeight", Label = "高度上限",
                            Badge = "now", Type = "int", Def = 18, Min = 0, Max = 30, St = 1
                        }
                    }
                }
            }
            // 2026-10-25 数值化对账（作者定稿极简版）：Bind 面 = General.Enabled + Expand.Width/
            // Height/Burden + Global.MaxWidth/MaxHeight，共 6 键；面板暴露 5 键（Enabled 走侧栏启停位）。
            // Height 语义=基础高度，天赋加行三连（31931-31933）每级 +1 由 mod 按链级数表计算。
            // 旧倍率键（Expand.*Mult）与 ExtraIds/ExcludeIds/Debug.* 已全部退役（首跑迁移后删除）。
        },
        new MetaMod
        {
            Guid = "com.local.survivallog.cabinetsize",
            Name = "柜子容量", En = "CabinetSize",
            Desc = "常用 12 包（置物架/冰箱/载具）与工具柜单独定宽高（0=恢复该柜原生）；其余柜子按 小/中/大/特大 档位自动调整；全局上限兜底；热改即时生效。",
            Groups =
            {
                new MetaGroup
                {
                    Title = "通用：档位与上限（未单独列出的柜子自动落档）",
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
                },
                new MetaGroup
                {
                    Title = "专属修改：常用 12 包（0=恢复该柜原生）",
                    Items =
                    {
                        BagW(110002, "小型置物架", 14, 10, "原生 6x8｜小档"),
                        BagH(110002, "小型置物架", 10, "原生 6x8｜小档"),
                        BagW(110001, "中型置物架", 16, 12, "原生 8x10｜中档；游戏内'工具柜'家具(70006)共用此包"),
                        BagH(110001, "中型置物架", 12, "原生 8x10｜中档"),
                        BagW(110000, "大型置物架", 18, 14, "原生 12x10｜大档"),
                        BagH(110000, "大型置物架", 14, "原生 12x10｜大档"),
                        BagW(110008, "超大型置物架", 20, 16, "原生 15x12｜特大档"),
                        BagH(110008, "超大型置物架", 16, "原生 15x12｜特大档"),
                        BagW(110009, "材料架", 20, 16, "原生 20x12｜特大档"),
                        BagH(110009, "材料架", 16, "原生 20x12｜特大档"),
                        BagW(1004, "双门冰箱", 16, 12, "原生 8x7｜中档"),
                        BagH(1004, "双门冰箱", 12, "原生 8x7｜中档"),
                        BagW(115000, "双开门冰箱", 16, 12, "原生 8x7｜中档"),
                        BagH(115000, "双开门冰箱", 12, "原生 8x7｜中档"),
                        BagW(115001, "冰柜", 18, 14, "原生 8x10｜大档"),
                        BagH(115001, "冰柜", 14, "原生 8x10｜大档"),
                        BagW(115005, "大容量冰柜", 20, 16, "原生 12x10｜特大档"),
                        BagH(115005, "大容量冰柜", 16, "原生 12x10｜特大档"),
                        BagW(1021, "自行车车筐", 12, 6, "原生 5x4｜独立定值 12x6"),
                        BagH(1021, "自行车车筐", 6, "原生 5x4｜独立定值"),
                        BagW(1022, "小汽车后备箱", 16, 8, "原生 10x8｜独立定值 16x8"),
                        BagH(1022, "小汽车后备箱", 8, "原生 10x8｜独立定值"),
                        BagW(1023, "SUV后备箱+门口塑料袋", 18, 10, "原生 12x10｜独立定值 18x10"),
                        BagH(1023, "SUV后备箱+门口塑料袋", 10, "原生 12x10｜独立定值")
                    }
                },
                new MetaGroup
                {
                    Title = "专属修改：工具柜（独立调整，不随小档；0=恢复原生）",
                    Items =
                    {
                        BagW(10007, "工具柜", 14, 10, "原生 6x7｜包 10007（家具 406）；游戏内另一'工具柜'家具(70006)走中型置物架包 110001"),
                        BagH(10007, "工具柜", 10, "原生 6x7｜独立调整，不随小档")
                    }
                }
            }
            // T19 对账注记（2026-10-25 作者令）：12 包 + 工具柜 10007 加入面板为"专属修改"两组，
            // 档位/上限归"通用"组。CabinetSize v4 起 13 包 [Bag.<id>] 节升级为 BepInEx 绑定键
            // （BagBindings），本表 Section="Bag.<id>" 直达既有反射桥（读写/防抖/T17 闸全复用，
            // ConfigBridge 零改动）；此前 [Bag.*] 是孤儿节（Save 按加载时快照回写，外部写会被打回，
            // BepInEx 577eba4 源码实证），绑定制一并根除。General.Enabled 已由 v4 补 Bind（原 T17 时代
            // 启停被拒的缺口闭合）。Preset（勿手改）/ExcludeIds/Debug.* 不进面板。
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
                    Title = "藤蔓与花种子",
                    Items =
                    {
                        new MetaItem { Section = "Vine", Key = "UnlockVine", Label = "解锁藤蔓（爬山虎）", Badge = "now", Type = "bool", Def = true },
                        // SkipDefenseGate 已随 2026-10-24 作者定案从插件移除（藤蔓种子防御门槛永远保留官方规则），注册表同步删除
                        new MetaItem { Section = "Vine", Key = "UnlockFlowerSeeds", Label = "解锁花种子", Badge = "now", Type = "bool", Def = true }
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
        },
        new MetaMod
        {
            Guid = "com.local.survivallog.endlessguard",
            Name = "囤鼠桃园", En = "EndlessGuard",
            Desc = "无尽模式永不沦陷：拦新沦陷 + 恢复已沦陷的交易点/探险图/营地 + 抹掉沦陷倒计时；附 11 条拾遗扩展规划（D100-D300）。",
            Groups =
            {
                new MetaGroup
                {
                    Title = "沦陷防护",
                    Items =
                    {
                        new MetaItem
                        {
                            Section = "Fall", Key = "PreventNewFalls", Label = "拦截新沦陷", Badge = "now", Type = "bool", Def = true,
                            Note = "关闭即恢复官方沦陷调度（阶段点名表还原需重启）"
                        },
                        new MetaItem
                        {
                            Section = "Fall", Key = "RestoreFallenPoints", Label = "恢复已沦陷点位", Badge = "reload", Type = "bool", Def = true,
                            Note = "读档时恢复交易点/探险图/营地（走过撤离剧情的营地除外）；物资份数始终按官方探索物资难度"
                        }
                    }
                },
                new MetaGroup
                {
                    Title = "生存规划扩展",
                    Items =
                    {
                        new MetaItem
                        {
                            Section = "Plans", Key = "ExtendScavengePlans", Label = "拾遗扩展 11 条", Badge = "reboot", Type = "bool", Def = true,
                            Note = "超市拾遗·六~十/医院踏勘·三~五/学校摸排·三~五（D100-D300 每 20 天一条）；关闭后重启生效，本局已注入内容保留"
                        }
                    }
                }
            }
        }
    };

    private static MetaItem MsItem(string key, string label) => new()
    {
        Section = "分类修改", Key = key, Label = label,
        Badge = "now", Type = "int", Def = 500, Min = 0, Max = 5000, St = 50
    };

    // 柜子容量 13 包显式条目（T19）：Section="Bag.<id>" 是 CabinetSize v4 绑定键——
    // 反射桥按 entry 读写（V/Min/Max 快照反射+Meta 兜底，写走 TrySet 常规路径）
    private static MetaItem BagW(int id, string name, int defW, int defH, string note) => new()
    {
        Section = "Bag." + id, Key = "Width", Label = name + " 宽度",
        Badge = "now", Type = "int", Def = defW, Min = 0, Max = 40, St = 1, Note = note
    };

    private static MetaItem BagH(int id, string name, int defH, string note) => new()
    {
        Section = "Bag." + id, Key = "Height", Label = name + " 高度",
        Badge = "now", Type = "int", Def = defH, Min = 0, Max = 30, St = 1, Note = note
    };

    private static MetaItem MultItem(string key) => new()
    {
        Section = "机器人提速", Key = key, Label = key,
        Badge = "now", Type = "float", Def = 2, Min = 0.5, Max = 10, St = 0.5
    };
}
