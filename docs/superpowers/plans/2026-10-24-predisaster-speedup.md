# 灾前加速（PreDisasterSpeed）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把官方五键倍速条（慢速/原速/2倍/3倍/暂停）原样覆盖到灾前阶段——JS 注入 CoreUI0 + C# 订阅 Vuplex 桥 + 调官方 `Ac_CoreUI1_SpeedChange.SendAction`，零 Harmony、零表改动。

**Architecture:** 2s 巡检 ticker 把内嵌 page.js 幂等 eval 进 CoreUI0 iframe（PlanPick 同款骨架）；按钮点击经 Root 无条件中继为 vuplex 协议(3)；mod C# 订阅 `IWebView.MessageEmitted` 识别自家标记后调官方 Redux action（reducer 无条件转发 `NotifyGlobal(10001)`，倒计时同步/暂停提示全官方原生）；CoreUI0 消失（灾变）自动归 1x。

**Tech Stack:** BepInEx IL2CPP 插件（net6.0，interop 引用 HotUpdate/Vuplex.WebView）、原生 JS 注入、node http + playwright-core 回归夹具。

**Spec:** `docs/superpowers/specs/2026-10-24-predisaster-speedup-design.md`（实证依据、决策记录、风险表都在规格里，本计划只讲怎么做）

## Global Constraints

- csproj `<Version>` = `1.3.8`（version.json suite 单源；助手不得自定/自增版本号）。
- **禁 `git add -A` / `git add .`**；每任务只 add 明确文件清单。
- `tools/gamekb/moddeps.json` 工作树里已有作者未提交改动——本计划只**编辑内容、不 stage 不提交**（留给作者随其改动一起入库）。
- 构建判退出码、禁管道：`"E:/Game/Mod/mod_stack/sdk/dotnet/dotnet.exe" build SurvivalLog.PreDisasterSpeed -c Release > SurvivalLog.PreDisasterSpeed/build.log 2>&1; echo $?`，必须 `exit=0`。
- page.js 是独立 `.js` 文件（EmbeddedResource），不进 C# verbatim；交付前 `node --check` 过。
- 不动 `tools/publish.ps1` / `installer/installer.iss`（作者已脏 + 发版才注册 `$suiteMods`）。
- 夹具全绿才许交付（`node tests/run_tests.js`）；UI 结论以装机实测定案（VERIFY.md 由作者执行）。

## File Structure

```
SurvivalLog.PreDisasterSpeed/
├── SurvivalLog.PreDisasterSpeed.csproj   # 引用/资源声明（Task 1）
├── PreDisasterSpeedPlugin.cs             # 插件入口 + cfg + 2s Ticker（Task 1）
├── SpeedLink.cs                          # 落地调用/章节倍率/事件桥/自愈（Task 2 + 5）
├── Injector.cs                           # WebView 获取 + page.js 注入 + payload 推送（Task 4）
├── page.js                               # 五键条 UI（注入 CoreUI0）（Task 3）
├── tests/
│   ├── fixture.html                      # CoreUI0 骨架夹具（Task 3）
│   └── run_tests.js                      # 浏览器回归断言（Task 3）
├── DESIGN.md                             # mod 设计文档 + 迭代维护清单（Task 6）
└── VERIFY.md                             # 装机验证清单（Task 6）
```

**跨任务契约（Interfaces）**
- Task 1 产出：`PreDisasterSpeedPlugin.Log/.Enabled/.Verbose`（static），`PdsTicker`（Update 每 2s 调 `Injector.Tick()`）。
- Task 2 产出：`SpeedLink.SetSpeed(int)`、`SpeedLink.TryGetChapterSpeeds(out int s1, out int s2)`、`SpeedLink.LastSpeed`（static int）。
- Task 3 产出（page.js 契约）：`window.__PdsInstalled` 安装守卫；`window.__PdsBoot({s1,s2})` payload 入口；点击发 `window.parent.postMessage({type:'PDS_SPEED', data:{speed:N}, sourcePageId:'CoreUI0'}, '*')`；DOM = `.top-center` 内 `#pdsSpeed`，五键类名 `pds-gear-4/1/2/3/0`。
- Task 4 产出：`Injector.Tick()`、`Injector.GetWebView()`、`SpeedLink.OnCoreUi0State(int)`（0=不在/隐藏，>0=在）。
- Task 5 产出：`SpeedLink.EnsureBridge(IWebView)`、`SpeedLink.OnMessageEmitted(object, EventArgs<string>)`。

---

### Task 1: mod 骨架可构建（csproj + Plugin + Ticker）

**Files:**
- Create: `SurvivalLog.PreDisasterSpeed/SurvivalLog.PreDisasterSpeed.csproj`
- Create: `SurvivalLog.PreDisasterSpeed/PreDisasterSpeedPlugin.cs`
- Create: `SurvivalLog.PreDisasterSpeed/Injector.cs`（空壳 stub——PdsTicker 引用 `Injector.Tick()`，Task 4 填充真实现）

**Interfaces:**
- Consumes: `../ModShared/WelcomeTip.cs`（防盗署名气泡，套件惯例）
- Produces: `PreDisasterSpeedPlugin.Log/.Enabled/.Verbose`（static）、`PdsTicker`（Update 每 2s 调 `Injector.Tick()`）、`Injector.Tick()` 空壳

- [ ] **Step 1: 写 csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <AssemblyName>SurvivalLog.PreDisasterSpeed</AssemblyName>
    <RootNamespace>SurvivalLog.PreDisasterSpeed</RootNamespace>
    <Version>1.3.8</Version>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
    <DebugType>embedded</DebugType>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <ProduceReferenceAssembly>false</ProduceReferenceAssembly>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CA1416</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="../ModShared/WelcomeTip.cs" />
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="page.js" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="BepInEx.Core">
      <HintPath>E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\BepInEx.Core.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="BepInEx.Unity.IL2CPP">
      <HintPath>E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\BepInEx.Unity.IL2CPP.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Il2CppInterop.Runtime">
      <HintPath>E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Il2CppInterop.Runtime.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Il2Cppmscorlib">
      <HintPath>E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\Il2Cppmscorlib.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Il2CppSystem.Core">
      <HintPath>E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\Il2CppSystem.Core.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="HotUpdate">
      <HintPath>E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Vuplex.WebView">
      <HintPath>E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\Vuplex.WebView.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

</Project>
```

注意：`<EmbeddedResource Include="page.js" />` 在 page.js 尚不存在时构建会报错——本任务先建一个占位 page.js（一行注释），Task 3 替换为真实现：

```js
// placeholder —— Task 3 替换为五键条实现
```

- [ ] **Step 2: 写插件入口与 Injector 空壳**

`PreDisasterSpeedPlugin.cs`：

```csharp
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using SurvivalLog.ModShared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.PreDisasterSpeed;

/// <summary>灾前加速：官方五键倍速条覆盖到灾前（CoreUI0）。零 Harmony/零表改动，设计见 DESIGN.md。</summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class PreDisasterSpeedPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.predisasterspeed";
    public const string Name = "PreDisasterSpeed";
    public const string Version = "1.3.8";

    internal static new ManualLogSource Log;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> Verbose;

    public override void Load()
    {
        Log = base.Log;
        Enabled = Config.Bind("General", "Enabled", true, "灾前加速条总开关（关闭=不注入不订阅；对已在档对局下次启动生效）。");
        Verbose = Config.Bind("Debug", "Verbose", false, "注入/桥接明细日志。");

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<PdsTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<PdsTicker>();
        var go = new GameObject("PreDisasterSpeed.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<PdsTicker>();
        Log.LogInfo("[PdsSpeed] loaded（Enabled=" + Enabled.Value + "）");
        WelcomeTip.Install(Log);
    }
}

/// <summary>2s 巡检：WebView 就绪→注入 page.js→推倍率 payload→保活事件桥（详见 Injector/SpeedLink）。</summary>
internal sealed class PdsTicker : MonoBehaviour
{
    public PdsTicker(IntPtr ptr) : base(ptr) { }
    public PdsTicker() { }
    private float _check;
    private void Update() { _check += Time.deltaTime; if (_check < 2f) return; _check = 0; Injector.Tick(); }
}
```

`Injector.cs`（空壳，Task 4 填充）：

```csharp
namespace SurvivalLog.PreDisasterSpeed;

/// <summary>Root 巡检注入器（Task 4 填充：page.js 注入/payload 推送/桥保活）。</summary>
internal static class Injector
{
    internal static void Tick() { }
}
```

- [ ] **Step 3: 构建验证**

Run: `"E:/Game/Mod/mod_stack/sdk/dotnet/dotnet.exe" build SurvivalLog.PreDisasterSpeed -c Release > SurvivalLog.PreDisasterSpeed/build.log 2>&1; echo $?`（在仓库根 `E:/Game/Mod/mod_stack` 执行）
Expected: `exit=0`

- [ ] **Step 4: Commit**

```bash
git add SurvivalLog.PreDisasterSpeed/SurvivalLog.PreDisasterSpeed.csproj SurvivalLog.PreDisasterSpeed/PreDisasterSpeedPlugin.cs SurvivalLog.PreDisasterSpeed/Injector.cs SurvivalLog.PreDisasterSpeed/page.js
git commit -m "feat(speed): 灾前加速骨架——插件入口/cfg/2s巡检Ticker（page.js占位+Injector空壳）"
```

---

### Task 2: SpeedLink 落地半部（官方调档 + 章节倍率读取）

**Files:**
- Create: `SurvivalLog.PreDisasterSpeed/SpeedLink.cs`

**Interfaces:**
- Consumes: `PreDisasterSpeedPlugin.Log/.Verbose`（Task 1）
- Produces: `SpeedLink.SetSpeed(int speed): void`、`SpeedLink.TryGetChapterSpeeds(out int s1, out int s2): bool`、`SpeedLink.LastSpeed: int`（static，初值 1）。Task 5 会往本文件追加 `EnsureBridge/OnMessageEmitted/OnCoreUi0State`。

- [ ] **Step 1: 写 SpeedLink.cs（本任务只写落地半部）**

```csharp
using System;

namespace SurvivalLog.PreDisasterSpeed;

/// <summary>速度落地与章节倍率（DESIGN §3.2/§4.2）。
/// 落地入口 = 官方按钮同款 Ac_CoreUI1_SpeedChange.SendAction（public static，非泛型）：
/// reducer RA_SpeedChange 无条件 NotifyGlobal(10001)，GameTimeManager 全链路原生生效。</summary>
internal static class SpeedLink
{
    internal static int LastSpeed = 1;   // mod 最后设置的档位（自愈用；官方侧改档不回写）

    private static bool _warnSend;
    private static bool _warnTable;

    /// <summary>调官方档位（0=暂停 1=原速 2=二倍 3=三倍 4=慢速）；越界忽略。</summary>
    internal static void SetSpeed(int speed)
    {
        if (speed < 0 || speed > 4) return;
        try
        {
            GameCore.HotUpdate.ReduxUI.Ac_CoreUI1_SpeedChange.SendAction(speed);
            LastSpeed = speed;
            if (PreDisasterSpeedPlugin.Verbose.Value)
                PreDisasterSpeedPlugin.Log.LogInfo("[PdsSpeed] SetSpeed(" + speed + ")");
        }
        catch (Exception e)
        {
            if (_warnSend) return;
            _warnSend = true;
            PreDisasterSpeedPlugin.Log.LogWarning("[PdsSpeed] SendAction 异常: " + e.Message);
        }
    }

    /// <summary>读灾前章节倍率：_Config_Chapter_Dict 中 WorldStateType==1 的行（当前版本 ID=1，
    /// SpeedUp1=2/SpeedUp2=32）。读不到返回 false 并保持官方基线 2/32。</summary>
    internal static bool TryGetChapterSpeeds(out int s1, out int s2)
    {
        s1 = 2; s2 = 32;
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.IsInstanceCreated) return false;
            var cm = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.Instance;
            var dict = cm._Config_Chapter_Dict;
            if (dict == null) return false;
            foreach (var pair in dict)
            {
                if (pair.Value == null) continue;
                if ((int)pair.Value.WorldStateType == 1)
                {
                    s1 = pair.Value.SpeedUp1;
                    s2 = pair.Value.SpeedUp2;
                    return true;
                }
            }
            return false;
        }
        catch (Exception e)
        {
            if (_warnTable) return false;
            _warnTable = true;
            PreDisasterSpeedPlugin.Log.LogWarning("[PdsSpeed] 读章节表异常: " + e.Message);
            return false;
        }
    }
}
```

若 `(int)pair.Value.WorldStateType` 因 interop 枚举不可显式转换报错，改用 `Convert.ToInt32(pair.Value.WorldStateType)`——语义相同。

- [ ] **Step 2: 构建验证**

Run: `"E:/Game/Mod/mod_stack/sdk/dotnet/dotnet.exe" build SurvivalLog.PreDisasterSpeed -c Release > SurvivalLog.PreDisasterSpeed/build.log 2>&1; echo $?`
Expected: `exit=0`。若 `foreach (var pair in dict)` 枚举器不可用（Il2Cpp 字典版本差异），改为 `for (var e = dict.GetEnumerator(); e.MoveNext();)` 风格或 `dict.TryGet(1, out var row1)`（表键即章节 ID，当前版本灾前行 ID=1，同时保留 WorldStateType==1 校验）。

- [ ] **Step 3: Commit**

```bash
git add SurvivalLog.PreDisasterSpeed/SpeedLink.cs
git commit -m "feat(speed): 落地调用官方SendAction + 灾前章节倍率读取"
```

---

### Task 3: 五键条 UI —— 夹具红灯 → page.js → 绿灯

**Files:**
- Create: `SurvivalLog.PreDisasterSpeed/tests/fixture.html`
- Create: `SurvivalLog.PreDisasterSpeed/tests/run_tests.js`
- Modify: `SurvivalLog.PreDisasterSpeed/page.js`（替换 Task 1 占位）

**Interfaces:**
- Consumes: page.js 契约（见 File Structure 跨任务契约）
- Produces: `node tests/run_tests.js` 全绿（约 14 断言）；page.js 行为被钉死供 Task 4/5 依赖

- [ ] **Step 1: 写 fixture.html（CoreUI0 骨架）**

```html
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="UTF-8">
<title>PdsSpeed fixture</title>
<style>
    body { margin: 0; background: #2b2b2b; font-family: 'Microsoft YaHei', sans-serif; }
    .top-center { position: fixed; top: 20px; left: 50%; transform: translateX(-50%);
                  display: flex; flex-direction: column; align-items: center; gap: 6px; }
    .timer-box { display: flex; gap: 6px; align-items: center; background: #222;
                 padding: 4px 12px; border-radius: 6px; color: #fff; }
    .progress-container { width: 220px; height: 6px; background: #333; border-radius: 3px; }
</style>
</head>
<body>
<div id="app">
    <div class="top-center" data-interactive>
        <div class="timer-box"><span>倒计时 12:34</span></div>
        <div class="progress-container"><div class="progress-bar"></div></div>
    </div>
</div>
<script>
    // 夹具通道 1：模拟 Root→页面的官方 TimeScaleMsg（走真 postMessage 触发 message 事件）
    window.__fixtureTimeScale = function (v) {
        window.postMessage({ type: 'WebUI_CoreUI0_TimeScaleMsg', data: { timeScale: v } }, '*');
    };
    // 夹具通道 2：截获 page.js 的点击上报（fixture 无父页，window.parent===window）
    window.__sent = [];
    (function () {
        var orig = window.postMessage.bind(window);
        window.postMessage = function (msg, origin) {
            if (msg && msg.type === 'PDS_SPEED') { window.__sent.push(msg); return; }
            return orig(msg, origin);
        };
    })();
</script>
<script src="page.js"></script>
<script>window.__PdsBoot && window.__PdsBoot({ s1: 2, s2: 32 });</script>
</body>
</html>
```

- [ ] **Step 2: 写 run_tests.js**

```js
// PreDisasterSpeed page.js 浏览器回归：node tests/run_tests.js（仓库根或本目录均可）
// http 服务 /fixture.html /page.js；playwright-core + 系统 Chrome（PlanPick 同款驱动解析）。
const http = require('http');
const fs = require('fs');
const path = require('path');
const assert = require('assert');

const ROOT = path.resolve(__dirname, '..');
const PORT = parseInt(process.env.PDS_PORT || process.argv[2] || '8617', 10);
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';

const MIME = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8' };
const server = http.createServer((req, res) => {
    const u = req.url.split('?')[0];
    const file = u === '/fixture.html' ? path.join(ROOT, 'tests', 'fixture.html')
        : u === '/page.js' ? path.join(ROOT, 'page.js') : null;
    if (!file) { res.writeHead(404); res.end('404'); return; }
    fs.readFile(file, (err, data) => {
        if (err) { res.writeHead(500); res.end(err.message); return; }
        res.writeHead(200, { 'Content-Type': MIME[path.extname(file)] });
        res.end(data);
    });
});

const sleep = ms => new Promise(r => setTimeout(r, ms));
let PASSED = 0;
const ok = (cond, name) => { assert.ok(cond, name); PASSED++; console.log('  ok - ' + name); };
const activeSpeed = () => {
    const b = document.querySelector('#pdsSpeed .speed-btn.active');
    return b ? +(b.className.match(/pds-gear-(\d)/) || [0, -1])[1] : -1;
};

(async () => {
    await new Promise(r => server.listen(PORT, r));
    const browser = await chromium.launch({ executablePath: CHROME, headless: true });
    const page = await browser.newPage();
    const errors = [];
    page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
    page.on('pageerror', e => errors.push(String(e)));

    await page.goto('http://127.0.0.1:' + PORT + '/fixture.html');
    await sleep(500);   // 等 mount 重试周期（250ms）

    // A) 注入与结构
    ok(await page.locator('#pdsSpeed').count() === 1, '五键条已挂载且唯一');
    ok(await page.evaluate(() => document.querySelector('#pdsSpeed').parentElement.classList.contains('top-center')),
        '挂在 .top-center 内');
    const gears = await page.$$eval('#pdsSpeed .speed-btn', els => els.map(e => +(e.className.match(/pds-gear-(\d)/) || [0, -1])[1]));
    assert.deepStrictEqual(gears, [4, 1, 2, 3, 0], '五键顺序 慢速/原速/二倍/三倍/暂停'); PASSED++; console.log('  ok - 五键顺序');

    // B) 点击上报（postMessage 契约）
    await page.click('#pdsSpeed .pds-gear-2');
    await sleep(100);
    let sent = await page.evaluate(() => window.__sent[window.__sent.length - 1]);
    assert.deepStrictEqual(sent, { type: 'PDS_SPEED', data: { speed: 2 }, sourcePageId: 'CoreUI0' }, 'gear-2 上报 speed=2'); PASSED++; console.log('  ok - gear-2 上报');
    await page.click('#pdsSpeed .pds-gear-0');
    await page.click('#pdsSpeed .pds-gear-4');
    await sleep(100);
    sent = await page.evaluate(() => window.__sent.slice(-2));
    assert.deepStrictEqual(sent.map(m => m.data.speed), [0, 4], '暂停/慢速上报 0/4'); PASSED++; console.log('  ok - 暂停/慢速上报');

    // C) 官方时标回显映射（payload s1=2/s2=32）
    const cases = [[1, 1], [2, 2], [32, 3], [0.0001, 0], [0.3, -1]];
    for (const [ts, gear] of cases) {
        await page.evaluate(v => window.__fixtureTimeScale(v), ts);
        await sleep(80);
        const a = await page.evaluate(activeSpeed);
        assert.strictEqual(a, gear, 'timeScale=' + ts + ' 高亮 gear-' + gear); PASSED++;
        console.log('  ok - timeScale=' + ts + ' → gear-' + gear);
    }

    // D) 慢速本地高亮被官方同步重算
    await page.click('#pdsSpeed .pds-gear-4');
    await sleep(80);
    assert.strictEqual(await page.evaluate(activeSpeed), 4, '点击慢速本地高亮'); PASSED++;
    await page.evaluate(() => window.__fixtureTimeScale(1));
    await sleep(80);
    assert.strictEqual(await page.evaluate(activeSpeed), 1, '官方 scale=1 同步后慢速高亮被重算'); PASSED++;

    // E) 幂等：重跑 page.js 不重复挂条
    const src = fs.readFileSync(path.join(ROOT, 'page.js'), 'utf8');
    await page.evaluate(s => window.eval(s), src);
    await sleep(300);
    ok(await page.locator('#pdsSpeed').count() === 1, '重复 eval 不重复挂载');

    // F) 无 console error
    ok(errors.length === 0, '无 console error / pageerror: ' + errors.slice(0, 3).join(' | '));

    console.log('ALL ' + PASSED + ' ASSERTIONS PASSED / console errors ' + errors.length);
    await browser.close();
    server.close();
    process.exit(0);
})().catch(e => { console.error('FAIL @#' + (PASSED + 1) + ':', e.message); process.exit(1); });
```

- [ ] **Step 3: 先跑一次确认红灯（page.js 仍是占位）**

Run: `node SurvivalLog.PreDisasterSpeed/tests/run_tests.js`
Expected: `FAIL #1: 五键条已挂载且唯一`（locator count 0）——红灯即测试有效。

- [ ] **Step 4: 写真 page.js（替换占位）**

```js
// PreDisasterSpeed page.js —— 注入 CoreUI0（灾前 HUD）的官方五键倍速条（样式照抄 CoreUI1 speed-controls）。
// 约定（DESIGN §4.1/§4.3）：
//  - 安装守卫 window.__PdsInstalled；iframe 被游戏重建后标记消失，C# 巡检自动重装。
//  - C# 经 window.__PdsBoot({s1,s2}) 推灾前章节倍率（JS 不硬编码）。
//  - 点击 → window.parent.postMessage({type:'PDS_SPEED',data:{speed},sourcePageId:'CoreUI0'})
//    Root 对任意 type 无条件中继为 vuplex 协议(3)，C# 桥订阅 MessageEmitted 落地（DESIGN §3.5）。
//  - 高亮：官方 WebUI_CoreUI0_TimeScaleMsg 的 timeScale 映射档位；慢速档(4)不改 scale，
//    用本地点击态，收到任一官方 scale 同步即重算。临时时标（睡眠/空闲 0.3）全灭。
//  - 按钮为 <button>，自动进 webui-core collectRects 交互矩形上报（选择器含 button），点击可达。
(function () {
    if (window.__PdsInstalled) return;
    window.__PdsInstalled = true;

    var payload = null;              // {s1, s2}

    var GEARS = [
        { speed: 4, label: '慢速', svg: '<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M9 4v11l8-5.5z"/><rect x="5" y="17.5" width="14" height="2" rx="1"/></svg>' },
        { speed: 1, label: '原速', svg: '<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M8 5v14l11-7z"/></svg>' },
        { speed: 2, label: '二倍', svg: '<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M4 18l8.5-6L4 6v12zm9-12v12l8.5-6L13 6z"/></svg>' },
        { speed: 3, label: '三倍', svg: '<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M2 18l6-6-6-6v12zm7-12v12l6-6-6-6zm7 0v12l6-6-6-6z"/></svg>' },
        { speed: 0, label: '暂停', svg: '<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M6 5h4v14H6zm8 0h4v14h-4z"/></svg>' }
    ];

    function send(speed) {
        window.parent.postMessage({ type: 'PDS_SPEED', data: { speed: speed }, sourcePageId: 'CoreUI0' }, '*');
    }

    function setActive(speed) {
        var btns = document.querySelectorAll('#pdsSpeed .speed-btn');
        for (var i = 0; i < btns.length; i++) btns[i].classList.remove('active');
        if (speed < 0) return;
        var b = document.querySelector('#pdsSpeed .pds-gear-' + speed);
        if (b) b.classList.add('active');
    }

    function scaleToSpeed(v) {
        var s1 = payload ? payload.s1 : 2, s2 = payload ? payload.s2 : 32;
        if (v < 0.01) return 0;
        if (Math.abs(v - 1) < 0.01) return 1;
        if (Math.abs(v - s1) < 0.01) return 2;
        if (Math.abs(v - s2) < 0.01) return 3;
        return -1;
    }

    window.addEventListener('message', function (ev) {
        var m = ev.data;
        if (!m || typeof m.type !== 'string') return;
        if (m.type === 'WebUI_CoreUI0_TimeScaleMsg' && m.data && typeof m.data.timeScale === 'number') {
            setActive(scaleToSpeed(m.data.timeScale));
        }
    });

    window.__PdsBoot = function (p) {
        if (p && typeof p.s1 === 'number' && typeof p.s2 === 'number') payload = p;
    };

    function mount() {
        var host = document.querySelector('.top-center');
        if (!host) return false;
        if (document.getElementById('pdsSpeed')) return true;
        var css = document.createElement('style');
        css.textContent = ''
            + '#pdsSpeed{position:relative;display:flex;gap:4px;background-color:rgba(30,30,30,0.85);padding:2px;border-radius:6px;border:1px solid rgba(255,255,255,0.12);width:fit-content;}'
            + '#pdsSpeed .speed-btn{position:relative;width:30px;height:30px;background:transparent;border:1px solid transparent;border-radius:4px;color:rgba(255,255,255,0.6);cursor:pointer;transition:all .2s;padding:0;display:flex;align-items:center;justify-content:center;line-height:1;}'
            + '#pdsSpeed .speed-btn:hover{background-color:rgba(255,255,255,0.1);color:#fff;}'
            + '#pdsSpeed .speed-btn.active{background-color:rgba(255,193,7,0.15);color:#FFC107;border-color:rgba(255,193,7,0.3);font-weight:bold;box-shadow:inset 0 0 4px rgba(255,193,7,0.2);}'
            + '#pdsSpeed .speed-tooltip{position:absolute;bottom:130%;left:50%;transform:translateX(-50%);background-color:rgba(20,20,20,0.95);border:1px solid rgba(255,255,255,0.2);border-radius:4px;padding:6px 10px;color:#eee;font-size:12px;white-space:nowrap;pointer-events:none;opacity:0;transition:opacity .2s;}'
            + '#pdsSpeed .speed-btn:hover .speed-tooltip{opacity:1;}';
        (document.head || document.documentElement).appendChild(css);
        var strip = document.createElement('div');
        strip.id = 'pdsSpeed';
        GEARS.forEach(function (g) {
            var b = document.createElement('button');
            b.className = 'speed-btn pds-gear-' + g.speed;
            b.innerHTML = g.svg + '<div class="speed-tooltip">' + g.label + '</div>';
            b.addEventListener('click', function () { send(g.speed); setActive(g.speed); });
            strip.appendChild(b);
        });
        host.appendChild(strip);
        return true;
    }

    // CoreUI0 body 未就绪时重试（250ms×40=10s）；挂载后交互矩形由 webui-core 的 MutationObserver 自动重报
    var tries = 0;
    var timer = setInterval(function () {
        if (mount() || ++tries >= 40) clearInterval(timer);
    }, 250);
    mount();
})();
```

- [ ] **Step 5: 语法检查 + 夹具转绿**

Run: `node --check SurvivalLog.PreDisasterSpeed/page.js && node SurvivalLog.PreDisasterSpeed/tests/run_tests.js`
Expected: `ALL 14 ASSERTIONS PASSED / console errors 0`（C 组 5 断言 + D 组 2 + A 2 + B 2 + E 1 + F 1 = 13~14，以实际计数为准，全 PASS 即可）

- [ ] **Step 6: Commit**

```bash
git add SurvivalLog.PreDisasterSpeed/page.js SurvivalLog.PreDisasterSpeed/tests/fixture.html SurvivalLog.PreDisasterSpeed/tests/run_tests.js
git commit -m "feat(speed): 五键倍速条 page.js + 浏览器回归夹具全绿"
```

---

### Task 4: Injector —— 注入、payload 推送、CoreUI0 状态回执

**Files:**
- Modify: `SurvivalLog.PreDisasterSpeed/Injector.cs`（替换 Task 1 空壳为真实现）

**Interfaces:**
- Consumes: `SpeedLink.TryGetChapterSpeeds`（Task 2）、`PreDisasterSpeedPlugin.Log/.Verbose/.Enabled`（Task 1）、page.js 契约（Task 3）
- Produces: `Injector.Tick(): void`（PdsTicker 调用）、`Injector.GetWebView(): Vuplex.WebView.IWebView`、`SpeedLink.OnCoreUi0State(int)` 的调用点（方法本体 Task 5 提供）

- [ ] **Step 1: 写 Injector.cs**

```csharp
using System;
using System.Reflection;

namespace SurvivalLog.PreDisasterSpeed;

/// <summary>Root 巡检注入器（每 2s，幂等，PlanPick 同款骨架）：
/// 1) page.js 装进 CoreUI0 iframe（__PdsInstalled 守卫，iframe 重建自愈重装）；
/// 2) 回执经 ExecuteJavaScript callback 读安装脚本返回值（0=不在/隐藏 >0=在）喂自愈；
/// 3) 章节倍率 payload 签名变化才推（防每 2s 重渲染）；4) 保活 MessageEmitted 桥。</summary>
internal static class Injector
{
    private const string ResourceName = "SurvivalLog.PreDisasterSpeed.page.js";

    private static string _pageJsJson;    // page.js 的 JSON 字符串字面量（装载时转义一次后复用）
    private static bool _pageJsWarned;
    private static bool _warnInstall;
    private static bool _warnPush;
    private static bool _announced;
    private static string _payloadSig = "";

    internal static void Tick()
    {
        if (!PreDisasterSpeedPlugin.Enabled.Value) return;
        var web = GetWebView();
        if (web == null) { SpeedLink.OnCoreUi0State(0); return; }   // WebView 失联视同不在（含灾后场景）
        if (_pageJsJson == null && !LoadPageJs()) return;

        // 1) 条件安装 + 状态回执（回执是异步的，晚一拍到自愈无碍——2s 巡检周期消化）
        var install =
            "(function(){var f=document.getElementById('CoreUI0');" +
            "if(!f||f.style.display==='none'||!f.contentWindow)return 0;" +
            "var w=f.contentWindow;if(w.__PdsInstalled)return 1;" +
            "w.eval(" + _pageJsJson + ");return 2;})()";
        try { web.ExecuteJavaScript(install, s => { int v; if (int.TryParse(s, out v)) SpeedLink.OnCoreUi0State(v); }); }
        catch (Exception e) { WarnOnce(ref _warnInstall, "注入异常", e); }

        // 2) payload：章节倍率（C# 读表，JS 不硬编码）；签名变化才推
        if (SpeedLink.TryGetChapterSpeeds(out var s1, out var s2))
        {
            var sig = s1 + ":" + s2;
            if (sig != _payloadSig)
            {
                _payloadSig = sig;
                var push =
                    "(function(){var f=document.getElementById('CoreUI0');" +
                    "if(!f||!f.contentWindow)return;var w=f.contentWindow;" +
                    "if(!w.__PdsBoot)return;w.__PdsBoot({\"s1\":" + s1 + ",\"s2\":" + s2 + "});})()";
                try { web.ExecuteJavaScript(push, null); }
                catch (Exception e) { WarnOnce(ref _warnPush, "推送异常", e); }
                if (PreDisasterSpeedPlugin.Verbose.Value)
                    PreDisasterSpeedPlugin.Log.LogInfo("[PdsSpeed] 推 payload s1=" + s1 + " s2=" + s2);
            }
        }

        // 3) 保活事件桥（实例变更自动重挂，见 SpeedLink.EnsureBridge）
        SpeedLink.EnsureBridge(web);

        if (!_announced)
        {
            _announced = true;
            PreDisasterSpeedPlugin.Log.LogInfo("[PdsSpeed] 巡检就绪（CoreUI0 灾前 HUD，五键条待注入）");
        }
    }

    private static bool LoadPageJs()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                if (!_pageJsWarned)
                {
                    _pageJsWarned = true;
                    PreDisasterSpeedPlugin.Log.LogWarning("[PdsSpeed] 内嵌资源 " + ResourceName + " 缺失；实际资源: " +
                        string.Join(", ", Assembly.GetExecutingAssembly().GetManifestResourceNames()));
                }
                return false;
            }
            using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
            _pageJsJson = "\"" + JsEscape(reader.ReadToEnd()) + "\"";
            return true;
        }
        catch (Exception e)
        {
            if (!_pageJsWarned) { _pageJsWarned = true; PreDisasterSpeedPlugin.Log.LogWarning("[PdsSpeed] 读取 page.js 失败: " + e.Message); }
            return false;
        }
    }

    private static string JsEscape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

    private static void WarnOnce(ref bool flag, string what, Exception e)
    {
        if (flag) return;
        flag = true;
        PreDisasterSpeedPlugin.Log.LogWarning("[PdsSpeed] " + what + ": " + e.Message);
    }

    /// <summary>ViewportProbe/PlanPick 同款取 Root WebView 路径（ReduxUISystem → WebUILayer → canvasWebViewPrefab）。</summary>
    internal static Vuplex.WebView.IWebView GetWebView()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.IsInstanceCreated) return null;
            var rus = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.Instance;
            return rus?.GetWebUILayer()?.canvasWebViewPrefab?.WebView;
        }
        catch { return null; }
    }
}
```

若 `ExecuteJavaScript(install, s => ...)` 的 lambda 到 `Action<string>` 转换报编译错（interop 侧要求 Il2Cpp 委托），改为方法组 + 显式转换：`web.ExecuteJavaScript(install, OnInstallResult);` 配 `private static void OnInstallResult(string s) {...}`（Il2CppInterop 对方法组隐式转换支持最稳）。

- [ ] **Step 2: 构建验证（预期失败一处——OnCoreUi0State/EnsureBridge 还没有）**

Run: `"E:/Game/Mod/mod_stack/sdk/dotnet/dotnet.exe" build SurvivalLog.PreDisasterSpeed -c Release > SurvivalLog.PreDisasterSpeed/build.log 2>&1; echo $?`
Expected: `exit!=0`，错误恰好是 `SpeedLink.OnCoreUi0State` / `SpeedLink.EnsureBridge` 不存在——这是 Task 5 的接口预告，立即做 Task 5 再回来复验。

- [ ] **Step 3: Commit（与 Task 5 合并提交，见 Task 5 Step 4）**

---

### Task 5: 桥接 + 灾变自愈（SpeedLink 后半部，与 Task 4 联编）

**Files:**
- Modify: `SurvivalLog.PreDisasterSpeed/SpeedLink.cs`（文件末尾追加桥接区）

**Interfaces:**
- Consumes: Task 2 已有半部、Task 4 的 `Injector.Tick()` 调用点、page.js 的 `PDS_SPEED` postMessage
- Produces: `SpeedLink.EnsureBridge(Vuplex.WebView.IWebView)`、`SpeedLink.OnCoreUi0State(int)`

- [ ] **Step 1: SpeedLink.cs 追加桥接区（using 区补 `System.Text.RegularExpressions`）**

```csharp
    // ---------- 桥接 + 自愈（Task 5） ----------

    private static Vuplex.WebView.IWebView _bridged;
    private static bool _coreUi0Visible;
    private static bool _warnBridge;
    private static readonly Regex SpeedRegex = new Regex("\"speed\"\\s*:\\s*([0-4])", RegexOptions.Compiled);

    /// <summary>订阅 Vuplex MessageEmitted（与官方 WebUILayer.OnMessageFromJS 并行收，多播互不干扰）。
    /// WebView 实例变更时对新实例重挂；旧实例随页面销毁，无需退订。</summary>
    internal static void EnsureBridge(Vuplex.WebView.IWebView web)
    {
        if (ReferenceEquals(_bridged, web)) return;
        try
        {
            web.MessageEmitted += OnMessageEmitted;
            _bridged = web;
            PreDisasterSpeedPlugin.Log.LogInfo("[PdsSpeed] MessageEmitted 桥已挂");
        }
        catch (Exception e)
        {
            if (_warnBridge) return;
            _warnBridge = true;
            PreDisasterSpeedPlugin.Log.LogWarning("[PdsSpeed] 订阅 MessageEmitted 异常: " + e.Message);
        }
    }

    /// <summary>官方协议 `typeId\x1EpageId\x1EeventType\x1Ejson`（Root.html 中继，DESIGN §3.5）。
    /// 只认 3/CoreUI0/PDS_SPEED，其余消息原样放过不碰。</summary>
    private static void OnMessageEmitted(object sender, Vuplex.WebView.EventArgs<string> e)
    {
        try
        {
            var msg = e.Value;
            if (string.IsNullOrEmpty(msg)) return;
            var p = msg.Split('\x1E');
            if (p.Length < 4 || p[0] != "3" || p[1] != "CoreUI0" || p[2] != "PDS_SPEED") return;
            var m = SpeedRegex.Match(p[3]);
            if (!m.Success) return;
            SetSpeed(int.Parse(m.Groups[1].Value));
        }
        catch
        {
            // v1.3.2 红线：静默出口必须可观测——桥内异常打点（只记一次，防刷屏）
            if (_warnBridge) return;
            _warnBridge = true;
            PreDisasterSpeedPlugin.Log.LogWarning("[PdsSpeed] 桥内解析异常（后续静默）");
        }
    }

    /// <summary>CoreUI0 可见性回执（Injector 安装脚本 0=不在/隐藏 1=已装 2=本轮新装）。
    /// 可见→不可见 = 灾变过渡：mod 记录的档位 ≠1 则归 1x，防倍率带进灾后（DESIGN §4.4）。</summary>
    internal static void OnCoreUi0State(int state)
    {
        var visible = state > 0;
        if (_coreUi0Visible && !visible && LastSpeed != 1)
        {
            SetSpeed(1);
            PreDisasterSpeedPlugin.Log.LogInfo("[PdsSpeed] CoreUI0 消失（灾变过渡）——档位自动归 1x");
        }
        _coreUi0Visible = visible;
    }
```

若 `web.MessageEmitted += OnMessageEmitted` 或 `Vuplex.WebView.EventArgs<string>` 泛型名在 interop 程序集里不同（如 `EventArgs_1<System.String>`），以 `BepInEx/interop/Vuplex.WebView.dll` 实际导出为准调整类型名——订阅机制与处理器逻辑不变（这是编译期适配，不是设计变更）。

- [ ] **Step 2: 联编验证（覆盖 Task 4 Step 2 的预期失败）**

Run: `"E:/Game/Mod/mod_stack/sdk/dotnet/dotnet.exe" build SurvivalLog.PreDisasterSpeed -c Release > SurvivalLog.PreDisasterSpeed/build.log 2>&1; echo $?`
Expected: `exit=0`，0 error 0 warning（warning 有则逐条过目，interop 转换 warning 记入 DESIGN）。

- [ ] **Step 3: 夹具回归不受扰（page.js 未改，应仍绿）**

Run: `node SurvivalLog.PreDisasterSpeed/tests/run_tests.js`
Expected: `ALL ... ASSERTIONS PASSED`

- [ ] **Step 4: Commit（Task 4+5 一并）**

```bash
git add SurvivalLog.PreDisasterSpeed/Injector.cs SurvivalLog.PreDisasterSpeed/SpeedLink.cs
git commit -m "feat(speed): 巡检注入器 + MessageEmitted 桥 + 灾变过渡自愈归1x"
```

---

### Task 6: 文档与依赖登记收尾

**Files:**
- Create: `SurvivalLog.PreDisasterSpeed/DESIGN.md`
- Create: `SurvivalLog.PreDisasterSpeed/VERIFY.md`
- Modify: `tools/gamekb/moddeps.json`（**只编辑、不 stage、不提交**——工作树已有作者未提交改动）

**Interfaces:**
- Consumes: 前 5 个任务的全部成品
- Produces: 可维护文档 + 游戏更新后 `modcompat_check.js` 能核对本 mod 依赖

- [ ] **Step 1: 写 DESIGN.md**

```markdown
# SurvivalLog.PreDisasterSpeed 设计文档

> 灾前加速：把官方五键倍速条（慢速/原速/二倍/三倍/暂停）原样覆盖到灾前（CoreUI0 HUD）。
> 决策记录与全部实证依据见 docs/superpowers/specs/2026-10-24-predisaster-speedup-design.md。

## 1. 机制（为什么零 Harmony 成立）

- 官方 `GameTimeManager.ChangeGameSpeed(int)` 无阶段校验（仅 speed≤4）；倍率是章节表乘数
  （灾前章节 WorldStateType==1：SpeedUp1=2、SpeedUp2=32——"三倍"键实为 ×32，官方原设计）。
- 灾后倍速条的门禁只在 UI 显示层（PermissionsManager.FuncMap[SpeedUpEntry]），
  点击链路 VM lambda→Ac_CoreUI1_SpeedChange.SendAction→reducer RA_SpeedChange→
  NotifyGlobal(10001) 全程无校验。
- 倒计时同步加速是官方原生：ChangeGameSpeed 末尾 NotifyCountDownTimerScaleSync 仅在
  WorldStateType==PreDisaster 重发 CountDownTimerStart；CoreUI0 JS 倒计时速率=
  decreasePerSecond×BattleTimeScale。

## 2. 实现

| 部件 | 文件 | 职责 |
|---|---|---|
| 入口 | PreDisasterSpeedPlugin.cs | cfg（Enabled/Verbose）+ 2s PdsTicker + WelcomeTip |
| 注入 | Injector.cs | WebView 获取（ReduxUISystem→WebUILayer→canvasWebViewPrefab）；page.js 幂等 eval 进 CoreUI0；payload（章节倍率）签名变化才推；安装回执喂自愈 |
| 桥+落地 | SpeedLink.cs | 订阅 IWebView.MessageEmitted 解析 vuplex 协议(3)/CoreUI0/PDS_SPEED；调官方 Ac_CoreUI1_SpeedChange.SendAction；CoreUI0 消失且档位≠1 自动归 1x |
| UI | page.js（EmbeddedResource） | 五键条挂 .top-center 尾部；点击 postMessage 契约；官方 TimeScaleMsg 高亮映射；慢速本地态 |

## 3. 契约（改前必读）

- page.js 契约：`__PdsInstalled` 守卫 / `__PdsBoot({s1,s2})` / 点击 postMessage
  `{type:'PDS_SPEED',data:{speed},sourcePageId:'CoreUI0'}` / DOM `#pdsSpeed .pds-gear-{4,1,2,3,0}`。
- vuplex 协议：`3\x1EpageId\x1EeventType\x1Ejson`（Root.html 无条件中继任意 type）。
- 桥只认 `3/CoreUI0/PDS_SPEED`，其余消息零接触。
- tests/run_tests.js 是 page.js 行为回归夹具，改 page.js 必须先跑绿。

## 4. 依赖（moddeps.json "PreDisasterSpeed" 条目；harmony 槽位此处仅做存在性核对，非补丁）

- GameCore.HotUpdate.ReduxUI.Ac_CoreUI1_SpeedChange.SendAction(int)（落地入口）
- _Config_Chapter_Dict / Config_Chapter.{WorldStateType,TimeScale,SpeedUp1,SpeedUp2}
- WebUI_CoreUI0 页（.top-center DOM、TimeScaleMsg/PauseMsg 通道）
- Vuplex IWebView.MessageEmitted / ExecuteJavaScript(script, callback)
- BaseSingleton<ReduxUISystem>.GetWebUILayer().canvasWebViewPrefab.WebView 取桥路径

## 5. 已知边界

- 灾前"三倍"=×32（官方表值）；嫌猛的备选项=内存表临时编辑 SpeedUp2（未实施）。
- 手动档会取消睡眠临时加速（官方 CancelTempSpeedUp 行为）。
- LastSpeed 只记 mod 侧设置；官方侧改档（灾后官方条）不回写——自愈只在 CoreUI0 消失瞬间比对。
- 若 interop 泛型名有出入（EventArgs_1 等），以 interop 程序集实际导出为准（编译期适配）。

## 6. 迭代维护清单

- 改桥协议/事件号/SendAction 签名 → 同步 moddeps.json + 本文档 §4 + 规格 §3/§5。
- 改 page.js 行为 → 先改 tests 夹具断言再改实现（红灯→绿灯）。
- 游戏更新后：node tools/gamekb/modcompat_check.js gamekb/<版本> 看本 mod 红灯项。
- 发版：作者定号后在 publish.ps1 $suiteMods 登记（本 mod 未发版前不动发版脚本）。
```

- [ ] **Step 2: 写 VERIFY.md**

```markdown
# PreDisasterSpeed 装机验证清单（作者执行）

部署：拷 SurvivalLog.PreDisasterSpeed.dll 到 <游戏>\BepInEx\plugins\，steam://rungameid/4164790 启动。
日志看 BepInEx `[PdsSpeed]` 打点（强杀不落盘，必要时看 cfg Verbose）。

## 灾前（新开局或灾前存档）
- [ ] CoreUI0 顶部出现五键条（慢速/原速/二倍/三倍/暂停），位置在倒计时进度条下方，不遮挡金币/任务
- [ ] 悬停各键出 tooltip；当前档高亮正确（默认原速高亮）
- [ ] 点二倍：钟面/倒计时消耗肉眼 ×2；动作进度 ×2；再点原速恢复
- [ ] 点三倍：倒计时明显狂飙（×32，官方表值——观感反馈记录在此）
- [ ] 点暂停：倒计时冻结、动作冻结、屏幕出现官方暂停提示；再点原速恢复
- [ ] 点慢速：高亮后停手 3 秒，时间明显变慢（空闲 0.3）；一动就恢复
- [ ] 睡眠加速期间点手动档：睡眠加速被取消（官方行为）
- [ ] 倒计时走完→灾变过渡：HUD 切灾后（CoreUI1），档位自动回 1x（日志有"档位自动归 1x"），灾后官方倍速条可正常用
- [ ] 全程无异常刷屏；重进对局五键条回来（幂等重装）

## 开关
- [ ] cfg Enabled=false 重启：五键条不出现，游戏行为无任何差异

## 回归
- [ ] 灾后章节/无尽模式不受扰：官方倍速条照常，倒计时/结算正常
```

- [ ] **Step 3: moddeps.json 增加条目（编辑文件，不提交）**

在 `tools/gamekb/moddeps.json` 顶层对象末尾（`PlanPick` 条目之后）追加：

```json
  "PreDisasterSpeed": {
    "harmony": [
      { "type": "GameCore.HotUpdate.ReduxUI.Ac_CoreUI1_SpeedChange", "method": "SendAction", "params": 1 }
    ],
    "configFields": [
      { "accessor": "_Config_Chapter_Dict", "rowType": "Config_Chapter", "fields": ["WorldStateType", "TimeScale", "SpeedUp1", "SpeedUp2"] }
    ],
    "nativeSigs": []
  }
```

说明：harmony 槽位在本 mod 语义=「方法存在性核对」（modcompat_check 只查类型/方法/参数个数，不要求真是补丁）——该说明已写入 DESIGN.md §4。

- [ ] **Step 4: modcompat 全绿**

Run: `node tools/gamekb/modcompat_check.js gamekb/2026-10-23_26157732`
Expected: `## PreDisasterSpeed` 下 5 项全 G（SendAction 1参 + accessor + 4 字段），其余 mod 无新增红灯（原本全绿基线）。

- [ ] **Step 5: 终构建 + 终回归**

Run: `"E:/Game/Mod/mod_stack/sdk/dotnet/dotnet.exe" build SurvivalLog.PreDisasterSpeed -c Release > SurvivalLog.PreDisasterSpeed/build.log 2>&1; echo $? && node SurvivalLog.PreDisasterSpeed/tests/run_tests.js`
Expected: `exit=0` + `ALL ... ASSERTIONS PASSED`

- [ ] **Step 6: Commit（moddeps.json 不入库）**

```bash
git add SurvivalLog.PreDisasterSpeed/DESIGN.md SurvivalLog.PreDisasterSpeed/VERIFY.md
git commit -m "docs(speed): DESIGN/VERIFY 收尾；moddeps 登记（moddeps.json 留工作树随作者改动入库）"
```

---

## 验收口径（全计划）

1. 构建退出码 0、夹具全绿、modcompat 本 mod 5 项全绿。
2. 仓库只新增 `SurvivalLog.PreDisasterSpeed/` 与 3 个 commit；`tools/gamekb/moddeps.json` 改动留工作树。
3. 装机验证（VERIFY.md）由作者执行；实机结论以作者反馈为准（UI 铁律：没听到"可以"不收尾）。
