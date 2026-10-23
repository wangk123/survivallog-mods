# 07 - 游戏资料库（gamekb）：设计与机制

> 2026-10-23 设计定稿（作者拍板"按推荐方案"）。本文档是 gamekb 的权威说明（"项目记忆文档"），
> 与 [05-版本管理与发版.md](05-版本管理与发版.md) 同级：05 管"怎么发版"，本文管"游戏更新了怎么同步资料、怎么核对 mod 兼容"。
> 状态：**设计定稿，实施待排期**（实施步骤见 §10；实施完成后本行改为"已实施"并补迭代维护清单）。

## 0. 已定决策（2026-10-23 作者拍板）

| 决策点 | 定案 |
|---|---|
| ConfigDumpProbe（表数据探针） | **做**——"全部游戏设定数值"的唯一来源（配置 bundle 全加密，无第二条路） |
| 存放位置 | **仓内 `gamekb/`，整目录 .gitignore**；公开内容只有 tools 工具源码 + 本文档 |
| 保留代数 | **全留**（磁盘 ~400MB/版，无压力） |
| DummyDll / il2cpp.h | **不逐版留存**；改为**保留源二进制**（GameAssembly.dll + global-metadata.dat），需要时随时重出 |
| 储物柜直达（CabinetEverywhere）筛选 NOFRAME 问题 | **不属 gamekb 范围**，作者自理（2026-10-23 更新影响排查中发现的唯一问题） |

## 1. 背景与目标

2026-10-23 游戏更新（buildid 26157732）当日做了一次完整影响排查，全流程人工：保全旧 dump 基线 →
对更新后二进制跑 Il2CppDumper → 手工清洗 diff（TypeDelta/RVA 注释噪声两轮翻车）→ 逐 mod 核对
依赖清单 → 字节签名静态扫描 → 读运行时日志互证。约 1.5 小时，且产物散落（基线在 il2cppdumper
目录、对比报告在临时目录、核对结论在会话记忆）。

gamekb 的目标：把这套流程**工具化、版本化、一条命令化**——

1. 每次游戏更新后 `gamekb_sync.ps1` 一条命令完成：版本快照（代码/数据/界面/元数据）+ 与上一版的
   结构 diff + 全套件 mod 依赖兼容核对，产出 `diff_from_<上一版>.md` 一页定案。
2. mod 开发基于 gamekb：查类型/方法/字段/表数值不再临场跑 dumper 或探针；DESIGN.md 与 RE 笔记
   锁版本引用，杜绝旧地址/旧偏移混用。
3. 发版门禁的一环：游戏更新 → sync → modcompat 全绿 → 实机启动自证 → 发版（v1.3.7"改代码页权限
   必崩"与"发版前必须实机启动"教训的制度化）。

**非目标**：素材层全量解包（data.unity3d 不入 KB，需要时用现成 `tools/unityfs_extract.js` 按需解）；
MainPackage 加密 bundle 破解（无计划）；任何形式的公开分发（见 §5 红线）。

## 2. 采集范围（五层，渠道全部实证过）

| 层 | 内容 | 渠道 | 单版体积 |
|---|---|---|---|
| 代码层 | dump.cs / script.json / stringliteral.json（类·方法·字段·地址·字符串字面量） | Il2CppDumper v6.7.46（3 参 CLI，Git Bash 直跑可行） | ~245MB |
| 数据层 | **全部 `Config_*` 配置表**（食物/材料/书本/工具/家具/动作/机器人/载具…全部设定数值） | ConfigDumpProbe 运行时探针（表在内存已解密；bundle 磁盘全加密静态无解） | ~几 MB |
| 界面层 | WebUI 全部 html/js/图（明文） | 拷贝 `SurvivalLog_Data\StreamingAssets\WebUI` | ~20MB |
| 元数据 | buildid、RuntimeAssets 版本、Unity 版本、关键文件 SHA256 | appmanifest_4164790.acf + RuntimeAssets_MainPackage.version + Player.log | 微量 |
| 源二进制 | GameAssembly.dll + global-metadata.dat（**一切的本源**，可对任意旧版重跑 dumper/sigscan/反汇编） | 拷贝 | ~131MB |

## 3. 目录结构与命名

```
gamekb/
  2026-10-23_26157732/            ← 每版一目录：<捕获日期>_<buildid>
    meta.json                      ← 见下方 schema
    binaries/
      GameAssembly.dll
      global-metadata.dat
    il2cpp/
      dump.cs  script.json  stringliteral.json
    tables/
      _index.json                  ← 表名/行数/字段清单/内容哈希（含 _DONE 自证）
      Config_Item.json  Config_Furniture.json  ...
    webui/                         ← StreamingAssets\WebUI 全量镜像
    diff_from_2026-10-21.md        ← 与上一版的结构 diff + mod 兼容核对结果（自动生成）
  2026-10-21_<prevbuildid>/        ← 首个历史基线（从现有 tools/il2cppdumper/base_v20261021 迁入；
                                       无 binaries/webui/tables，标注 partial: true）
```

- `meta.json` schema：

```json
{
  "buildid": 26157732,
  "runtime_assets_version": "1.1.19611",
  "unity_version": "6000.2.7f2 (9f600a1b48e4)",
  "captured_at": "2026-10-23T20:10:43+08:00",
  "sha256": { "GameAssembly.dll": "…", "global-metadata.dat": "…" },
  "tools": { "il2cppdumper": "6.7.46", "dumpdiff": "1.0" },
  "partial": false,
  "notes": "人工备注：本版要点（如：新增 HomeStock 家仓体系；官方加了配方静态缓存）"
}
```

- 工具不入 gamekb，归仓库 `tools/gamekb/`（入库，见 §5）：`dumpdiff.js`、`sigscan.js`、
  `modcompat_check.js`、`moddeps.json`（mod 依赖清单数据）、`gamekb_sync.ps1`。

## 4. git 与版权红线

- **`gamekb/` 整目录 .gitignore**（实现时在 .gitignore 加一节）。理由：dump/表数值/游戏二进制/
  WebUI 资源均为游戏本体衍生物，仓库公开（GitHub wangk123/survivallog-mods），公开即侵权风险，
  与 N网 mod 转载红线（[03-教训与红线.md](03-教训与红线.md)）同理。
- **公开内容仅限**：`tools/gamekb/` 工具源码、`moddeps.json`（只有符号名清单，属 mod 开发接口
  描述）、本文档。diff 报告含游戏符号明细，**留在本地 gamekb 内不入库**。
- 现有先例延续：`tools/il2cppdumper/`、`tools/webui_src/`、`tools/il2cpp_out/` 已在 .gitignore；
  实现后 `tools/webui_src/` 停止更新（被 gamekb/<ver>/webui 取代），目录保留一个 README 指路。

## 5. 同步流水线 `tools/gamekb/gamekb_sync.ps1`

纯 ASCII（PS 5.1 无 BOM 坑红线）。步骤：

1. **定位游戏**：注册表 `HKCU:\Software\Valve\Steam` → libraryfolders.vdf → `appmanifest_4164790.acf`
   取 installdir + buildid（路径链 2026-10-18 实证）。
2. **幂等闸**：buildid 与 `gamekb/` 下最新目录相同 → 打印"无更新"退出 0。
3. **建目录** `gamekb/<日期>_<buildid>/`，拷源二进制并算 SHA256，记 RuntimeAssets 版本/Unity 版本。
4. **跑 dumper**：3 参 CLI（exe 在 tools/il2cppdumper/，被 gitignore——**缺失时明确报错提示先放回**，
   不静默跳过）。坑位（两次实证）：输出落 exe 所在目录而非第三参 → 脚本跑前检查 exe 目录无未迁移
   dump 产物，跑后从 exe 目录**移动**（非复制）到 `<ver>/il2cpp/`，天然防覆盖。
5. **拷 WebUI**：robocopy /MIR → `<ver>/webui/`。
6. **生成 diff**：`node tools/gamekb/dumpdiff.js <prev>/il2cpp/dump.cs <ver>/il2cpp/dump.cs` →
   `<ver>/diff_from_<prev>.md`。关注类列表自动取自 moddeps.json（`-Focus` 参数）。
7. **兼容核对**：`node tools/gamekb/modcompat_check.js <ver>` → 结果追加进 diff 报告。
8. **表数据（半自动）**：打印提示"启动游戏跑 ConfigDumpProbe"。`-AutoProbe` 开关可选全自动：
   `steam://rungameid/4164790` 拉起 → 轮询 `BepInEx\gamekb_tables\<buildid>\_DONE.txt`（表就绪
   约 15–25 秒后写出）→ 确认后 taskkill + 回读进程死透再收文件。**作者正在玩时绝不自动杀进程**
   （runtime-probe 红线：先守望等游戏进程自然退出；无法确认闲置就退出自动模式留给人工）。
9. **收尾打印**：ADDED/REMOVED 类型数、modcompat ✓/✗、表 dump 状态、`notes` 提醒人工补要点。

失败处理：任何一步失败打印到哪一步、已产出什么、重跑方式（幂等：目录已存在则续跑缺项）。

## 6. ConfigDumpProbe 规格（`SurvivalLog.ConfigDumpProbe`）

一次性取证探针，与 ActionProbe/PowerProbe 同族。**红线：探针 ≠ 玩家 mod**——绝不在 $suiteMods
白名单、安装器 [Files]、InstallDelete 名单里（跑完留玩家机器无害，但绝不随套件分发）。

- **触发**：轮询 ConfigManager 实例就绪 + 首个 `_Config_*_Dict` 非空（主菜单阶段即就绪，实证），
  再做两轮间隔快照比对（表数与各行数不变）才算加载完毕，开始 dump。
- **枚举**：反射 ConfigManager 全部公有 `_Config_*_Dict` 字段 → 每表一个 JSON：

```json
{ "table": "Config_Item", "rows": 4187,
  "columns": ["ID", "Name", "Category", "StackLimit", "UseAction", "…"],
  "data": [ { "ID": 110001, "…": "…" }, … ] }
```

  值序列化：基础类型直出；List/字典递归 JSON；遇到非可序列化类型降级为 `ToString()` 并在
  `_index.json` 标注该字段 degraded。
- **产物与自证**：逐表写 `BepInEx\gamekb_tables\<buildid>\`，全部写完后写 `_index.json`（表名/
  行数/字段清单/每表内容哈希）再写 `_DONE.txt`（总表数/总行数/时间戳）——sync 以 `_DONE.txt`
  为成功判据（BepInEx 日志强杀不落盘红线：自证文件即真相）。
- **零配置**：不写 cfg（cfg 首跑固化坑）、不挂 Harmony（纯读）、每文件写完即 Flush 关闭。
- **性能**：一次性全表序列化在主菜单做，几十 MB JSON 秒级，不做增量不做缓存。

## 7. moddeps.json 与 modcompat_check.js

`tools/gamekb/moddeps.json`——全套件对外部代码的完整依赖清单（2026-10-23 排查已人工核对过一轮，
直接固化）。schema：

```json
{
  "StackLimit999": {
    "harmony": [
      { "type": "GameCore.HotUpdate.Battle.Logic.ItemManager", "method": "AddItem", "params": 8 },
      { "type": "GameCore.HotUpdate.ReduxUI.WebUIVm", "method": "HandleCallback" }
    ],
    "configFields": [],
    "nativeSigs": []
  },
  "FridgeChill": {
    "harmony": [],
    "configFields": [
      { "accessor": "_Config_Furniture_Dict", "rowType": "Config_Furniture", "fields": ["ColdRate"] }
    ],
    "nativeSigs": [
      { "name": "TalentCapBytePatch",
        "sigHex": "F30F5CD9F30F5FD3F30F59D0F30F1005????????F30F5FC2C3",
        "wildcardNote": "????=rel32 通配（第 17–20 字节）", "expectOldSlot": 0.1, "expectHits": 1 }
    ]
  }
}
```

`modcompat_check.js <ver>` 逐项核对新版 gamekb：
- harmony → 在 `il2cpp/dump.cs` 查类型存在 + 方法名存在（参数个数粗核对；签名级精对交给运行时
  Harmony 报错兜底）；
- configFields → 类型 + 字段名存在；再对 `tables/` 新版 JSON 核对字段仍在表头（schema 变更即红）；
- nativeSigs → 调 `sigscan.js` 对 `binaries/GameAssembly.dll` 静态扫（命中数 + 旧槽值双验）。

输出：逐 mod ✓/✗ 清单 + 汇总（exit code 非零 = 有红，供发版门禁消费）。**新增 mod 或改挂钩点时
必须同步维护 moddeps.json**（与 DESIGN.md 同规则，写进各 mod DESIGN.md 迭代维护清单）。

## 8. 与开发/发版流程的联动

1. **DESIGN.md**：每个 mod 的 DESIGN.md 增加"验证于 gamekb：`<目录名>`"一行，随验证更新。
2. **RE 笔记版本锁定**：任何文档/记忆引用方法地址（RVA）、字段偏移，必须带 gamekb 版本号；
   跨版本引用旧地址一律先 `dumpdiff.js` 确认（本次更新 ConfigManager 尾部新增私有字段、
   State_Web_ToolTable 头部插新字段——偏移漂移是常态，符号名才是稳定锚）。
3. **发版门禁顺序**（接进 05 号文档的流程）：游戏有更新 → `gamekb_sync.ps1` → diff 报告人工过目 →
   modcompat 全绿 → 实机启动一次看各 mod 自证"生效" → 走 publish.ps1。
4. **表数值查阅**：写 mod 找设定实数（食物保鲜/家具电力/配方材料…）直接查 `gamekb/<ver>/tables/`，
  不再临时写探针。

## 9. 首版基线（从现有产物迁入，实施第 2 步）

| 版本目录 | 来源 | 构成 |
|---|---|---|
| `2026-10-21_<prevbuildid>/` | `tools/il2cppdumper/base_v20261021/` 五件套 | partial: true（无 binaries/webui/tables；buildid 从 appmanifest 历史不可得时以 `2026-10-21_pre26157732` 命名并在 meta 注明） |
| `2026-10-23_26157732/` | 当日新 dump（`tools/il2cppdumper/` 现存五件套）+ 游戏目录拷 GameAssembly/global-metadata + WebUI + 首跑 ConfigDumpProbe | 完整版；`diff_from_2026-10-21.md` 用当日人工对比结论补齐（含 +39/-0 类型清单与全绿核对表） |

## 10. 实施步骤（概要；排期后按 writing-plans 出细案）

| # | 内容 | 验收 |
|---|---|---|
| 1 | `_dumpdiff.js`/`_sigscan.js` 转正为 `tools/gamekb/dumpdiff.js`/`sigscan.js`（去下划线、输出规范化、`-Focus` 支持从 moddeps 读） | 对两版基线重跑，结论与 2026-10-23 人工核对一致 |
| 2 | 建 `gamekb/` 目录 + .gitignore + 按 §9 迁入两版基线 | 目录结构齐、meta.json 齐、`git status` 不出现 gamekb |
| 3 | 写 ConfigDumpProbe 工程 + 实机跑 2026-10-23 版 | `_DONE.txt` 生成；抽查 Config_Action 行数≈4187（QuickAction v2.1.2 实测值）、Config_Furniture 含 ColdRate 字段 |
| 4 | 写 `moddeps.json`（9 mod + ModShared 全清单）+ `modcompat_check.js` | 对 2026-10-23 版跑出全绿 |
| 5 | 写 `gamekb_sync.ps1` 收编全流程 | 幂等重跑 exit 0；对当前 buildid 不重复建目录 |
| 6 | 本文档状态改"已实施" + 补迭代维护清单；CHANGELOG；05 号文档发版流程加 gamekb 门禁引用 | 文档互链无死链 |

## 11. 风险与对策

| 风险 | 对策 |
|---|---|
| dumper 输出落 exe 目录覆盖产物 | sync 脚本"移动而非复制"+ 跑前检查（§5.4，两次实证坑） |
| 游戏改表结构（字段增删/改名） | modcompat 亮红人工评估；dumpdiff 的 CHANGED 明细定位到行 |
| ConfigDumpProbe 混进发布套件 | 探针红线名单制（§6），publish.ps1 白名单不含它（与 ActionProbe 同治理） |
| 表 dump 时作者正在玩 | -AutoProbe 默认关；守望等自然退出，不确定就人工（§5.8） |
| 磁盘增长 | ~400MB/版全留；超 10 版可手工删老版 binaries/（dump 保留） |
| ps1 编码坑 | ASCII-only 规则（BOM 红线） |
| Il2CppDumper.exe 被 gitignore 后换机缺失 | sync 报错并给下载指路（Perfare/Il2CppDumper net6-win） |
| moddeps 清单与代码漂移 | 规则进各 mod DESIGN.md 迭代维护清单：改挂钩点必须同步 moddeps.json |

## 12. 附：本机制验证过的实证链（2026-10-23）

- 更新检测：appmanifest buildid 26157732、RuntimeAssets 版本 1.1.19611；
- dump 对比：24754→24793 类型，+39/-0（新增 HomeStock 体系、工作台配方收藏/已看/拆包、配方静态缓存）；
- 依赖核对：11 个 ItemManager 挂钩、WebUIVm.HandleCallback、BagComponent.OnInit、
  Ac_ToolTable_RefreshBag、ToolTableManager.TryCreateItem/RefreshRecipeList、10 个表访问器、
  10 个配置行类、ActionType/FurnitureType 枚举——全部存活；
- 字节签名：FridgeChill 20 字节签名新 DLL 唯一命中 @0x29756C5、旧槽值 0.1，与运行时日志互证；
- 运行时：interop 自动重生、9 插件全部"生效"。
