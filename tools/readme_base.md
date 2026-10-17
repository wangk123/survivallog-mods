# Survival Log Mod 基座（BepInEx 框架）

使用任何 SurvivalLog.* 插件前，先安装本基座（只装一次）。

## 安装
1. 把包内全部内容（`BepInEx\`、`dotnet\`、`winhttp.dll`、`doorstop_config.ini`、`.doorstop_version`）解压到游戏根目录（`Steam\steamapps\common\Survival Log\`，和 `SurvivalLog.exe` 同级）。
2. 启动一次游戏（首次启动会在本地生成适配文件，画面可能停留 1~3 分钟，属正常；所需文件已全部内置，无需联网下载），`BepInEx\` 下出现 `interop\`、`LogOutput.log` 等即安装成功。
3. 之后把想要的插件 dll 放进 `BepInEx\plugins\` 即可。

## 卸载
删除游戏根目录的 `winhttp.dll`、`doorstop_config.ini`、`.doorstop_version`、`BepInEx\`、`dotnet\` 即可，不影响游戏本体和存档。

## 注意
- `dotnet\` 目录是 BepInEx 的运行时，缺少它会导致注入静默失败（游戏正常跑但 mod 全部不加载）。
- `BepInEx\unity-libs\` 是首次启动生成适配文件所需的 Unity 基础库（已内置，勿删；删掉后首次启动会尝试联网重新下载）。
- 基座本身不改任何游戏内容。
