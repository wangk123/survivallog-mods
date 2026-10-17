using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

// usage: disasm <pe> <scriptjson> <rvaDecimal> [maxInstrs]
// build: cd tools/disasm_tool && sdk/dotnet.exe build -c Release（Iced 引用 nuget 缓存，见 tool.csproj）
// script.json 由 tools/il2cppdumper 生成（产物 353MB 用完即删）；RVA 是十进制，可直接在 script.json grep
var pePath = args[0]; var sjPath = args[1]; var rva = long.Parse(args[2]);
var max = args.Length > 3 ? int.Parse(args[3]) : 400;

var buf = File.ReadAllBytes(pePath);
var peOff = BitConverter.ToInt32(buf, 0x3c);
var numSections = BitConverter.ToUInt16(buf, peOff + 6);
var optSize = BitConverter.ToUInt16(buf, peOff + 20);
var secOff = peOff + 24 + optSize;
uint RvaToOff(uint r) {
    for (int i = 0; i < numSections; i++) {
        var s = secOff + i * 40;
        uint va = BitConverter.ToUInt32(buf, s + 12), sz = BitConverter.ToUInt32(buf, s + 16), ptr = BitConverter.ToUInt32(buf, s + 20);
        if (r >= va && r < va + sz) return ptr + (r - va);
    }
    throw new Exception("bad rva");
}

var byAddr = new Dictionary<long, string>();
using (var doc = JsonDocument.Parse(File.ReadAllText(sjPath))) {
    foreach (var m in doc.RootElement.GetProperty("ScriptMethod").EnumerateArray()) {
        byAddr[m.GetProperty("Address").GetInt64()] = m.GetProperty("Name").GetString();
    }
}

var off = RvaToOff((uint)rva);
var reader = new Iced.Intel.ByteArrayCodeReader(buf, (int)off, (int)Math.Min(buf.Length - off, 0x2000));
var dec = Iced.Intel.Decoder.Create(64, reader, (ulong)rva);
var fmt = new Iced.Intel.NasmFormatter();
var so = new Iced.Intel.StringOutput();
for (int i = 0; i < max; i++) {
    var instr = dec.Decode();
    if (instr.IsInvalid) break;
    var sym = "";
    if (instr.FlowControl == Iced.Intel.FlowControl.Call || instr.FlowControl == Iced.Intel.FlowControl.UnconditionalBranch) {
        if (instr.Op0Kind == Iced.Intel.OpKind.NearBranch64 || instr.Op0Kind == Iced.Intel.OpKind.NearBranch32) {
            var tgt = (long)instr.NearBranchTarget;
            if (byAddr.TryGetValue(tgt, out var n)) sym = "  ; " + n;
        }
    }
    fmt.Format(instr, so);
    Console.WriteLine($"0x{instr.IP:x8}  {so.ToStringAndReset()}{sym}");
    if (instr.FlowControl == Iced.Intel.FlowControl.Return) break;
}
