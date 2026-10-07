# Survival Log 四合一 mod 套件 v1.0.0

## 组成
- `BASE\` —— BepInEx 部署基座（含 dotnet 运行时），整个解压到游戏根目录即可，只装一次
- `MODs\` —— 四个独立插件，想要哪个就把哪个 dll 复制到 `游戏目录\BepInEx\plugins\`

## 四个 mod
1. **SurvivalLog.BackpackExpand** 背包扩容：宽高各 ×1.5（四舍五入取整），负重 ×2。覆盖普通背包/仓管背包/登山包全部天赋链（含未解锁的链上大包），天赋扩格与负重加成照常叠加。
2. **SurvivalLog.CabinetExpand** 工具柜扩容：宽高各 ×1.5。默认改"工具柜(406)"的包(BagId 10007)。打开工具台界面时 BepInEx 日志会打印实际 BagConfigId；若家里某柜子没变化，把日志里的数字加进配置 `ExtraIds` 即可。
3. **SurvivalLog.FridgeExpand** 冰箱扩容：宽高各 ×1.5。覆盖双门冰箱/双开门冰箱/冰柜/豪华版/小冰箱（BagId 1004/115000/115001/3002）。
4. **SurvivalLog.FridgeChill** 冰箱冷藏增强：保鲜倍数 ×2（默认，只改 ColdRate 影响系数，绝不直接修改食物数据）。已在冰箱里的旧食物：移出再放回一次即按新系数重算（无损）。与天赋冷藏增强独立叠加；冷链房间、堆肥箱不受影响。

## 配置
配置文件在 `BepInEx\config\com.local.survivallog.<mod名>.cfg`，改倍数/名单后**重启游戏生效**（游戏运行中改配置会在约 2 秒内自动重算格子；负重要重新读档才体现到玩家身上）。

## 卸载
删除对应 dll 即可。mod 只改内存配置表，卸载后一切还原；已放进扩容区域的物品会被游戏的坐标矫正机制自动挪回，不会丢失。

## 已验证的兼容性（2026-10-02）
- 天赋"扩格/负重"继续正常升级与叠加（公式：负重 = Round((基础+天赋额外)×(1+天赋比例)+固定值)，基础与天赋独立）
- 存档读写正常（容器只存 BagConfigId + 物品坐标，扩容不破坏旧物品位置）
- 尺寸白名单 / 存档修复逻辑（HasBagSize / SetBagPosSafe）按改写后数值自动重建

## 已知边界
- 工具柜如无变化：看 `BepInEx\LogOutput.log` 里 `[CabinetExpand][诊断]` 行，把打印出的 BagConfigId 加进配置
- 购物车/轿车/置物架/堆肥箱等未在默认名单（刻意不动）；需要可自行加 BagId
- 挑战模式/排行榜对改动数值的判定未验证（`CheatEvidenceType.BagSizeIllegal` 存在但未定位到触发点，单机玩法无影响）
