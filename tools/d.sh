#!/usr/bin/env bash
# d.sh <hexRVA> [maxInstrs] — disassemble via node-converted decimal
RVA=$(node -e "console.log(parseInt(process.argv[1],16))" "$1")
MAX=${2:-800}
cd "$(dirname "$0")"
dotnet disasm_tool/bin/Release/net6.0/disasm.dll "E:\SteamLibrary\steamapps\common\Survival Log\GameAssembly.dll" il2cppdumper/dump_out/script.json "$RVA" "$MAX"
