# gamekb 工具集（设计见 docs/07-游戏资料库gamekb.md）

| 工具 | 用法 | 说明 |
|---|---|---|
| dumpdiff.js | `node dumpdiff.js <oldDump.cs> <newDump.cs> [关注子串...] [--moddeps moddeps.json]` | 两版 dump 结构对比；输出重定向到 gamekb/<ver>/diff_from_<prev>.md |
| sigscan.js | `node sigscan.js <GameAssembly.dll>` | FridgeChill 字节签名静态验证（模块可复用于任意 ?? 通配签名） |
| modcompat_check.js | `node modcompat_check.js <gamekb版本目录>` | 按 moddeps.json 核对 mod 依赖；全绿 exit 0，有红 exit 1 |
| moddeps.json | — | 全套件对外部代码的依赖清单（改挂钩点必须同步维护） |
| gamekb_sync.ps1 | `powershell -File gamekb_sync.ps1 [-AutoProbe]` | 游戏更新后一键同步（见 docs/07 §5） |

数据目录 `gamekb/` 不入库（版权红线），只在本机留存。
