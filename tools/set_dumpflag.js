const fs = require('fs');
const p = 'E:/SteamLibrary/steamapps/common/Survival Log/BepInEx/config/com.local.survivallog.cabineteverywhere.cfg';
let t = fs.readFileSync(p, 'utf8');
if (!t.includes('DumpFurniture')) {
  const q = s => String.fromCharCode(s); // 0x201C/0x201D curly quotes
  const note = '## \u7f6e true \u540e\uff08\u6e38\u620f\u8fd0\u884c\u4e2d\u6539 cfg \u7ea6 5 \u79d2\u5185\u751f\u6548\uff09'
    + '\u5728\u65e5\u5fd7\u5217\u51fa\u5bb6\u4e2d\u5168\u90e8\u5e26\u5305\u5bb6\u5177\u660e\u7ec6\uff1aowner/\u914d\u7f6e/\u540d\u79f0/\u9501\u5b9a\u6001/\u662f\u5426\u5165\u9009\u3002'
    + '\u6392\u67e5' + q(0x201C) + '\u65b0\u67dc\u5b50\u4e0d\u751f\u6548' + q(0x201D) + '\u7528\uff0c\u5e73\u65f6\u5173\u3002';
  t = t.trimEnd() + '\n\n' + note + '\n# Setting type: Boolean\n# Default value: false\nDumpFurniture = true\n';
  fs.writeFileSync(p, t, 'utf8');
}
console.log(fs.readFileSync(p, 'utf8').includes('DumpFurniture = true') ? 'CFG-SET' : 'FAIL');
