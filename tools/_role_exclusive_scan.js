// 角色专属内容全量扫描：17 张 PlayerSelectId 表 + GlobalSetting 白名单 + 配方解锁链
// 用法: node tools/_role_exclusive_scan.js <tables目录>
const fs = require('fs');
const path = require('path');
const dir = process.argv[2] || path.join(__dirname, '../gamekb/2026-10-23_26157732/tables');
const L = (r, k) => r['_'+k+'_k__BackingField'] ?? r[k];
const nameOf = r => L(r,'Name_Local') || L(r,'Name') || '';

const tables = ['Config_Talent','Config_Event','Config_Story','Config_DateTimeline',
 'Config_ThinkOpen','Config_ThinkOpenAction','Config_ThinkExplore','Config_ThinkFloor',
 'Config_ThinkTime','Config_ThinkActionType','Config_ThinkBlackScreen',
 'Config_GuidanceFurniture','Config_GuidanceNpc','Config_Show','Config_BlackScreen',
 'Config_ActionVoice','Config_RobotMonologue'];

for (const t of tables) {
  const j = JSON.parse(fs.readFileSync(path.join(dir, t+'.json'),'utf8'));
  const groups = {}; // key=白名单串
  for (const r of j.data) {
    const v = L(r,'PlayerSelectId');
    const key = Array.isArray(v) ? v.join(';') : String(v);
    (groups[key] = groups[key] || []).push(r);
  }
  console.log('\n===== '+t+' rows='+j.rows+' =====');
  for (const key of Object.keys(groups).sort()) {
    const rows = groups[key];
    const label = key==='' || key==='[]' ? '(空=全角色)' : '仅['+key+']';
    console.log('-- '+label+' n='+rows.length);
    if (key!=='' && key!=='[]') {
      for (const r of rows.slice(0,40))
        console.log('   '+L(r,'ID')+' | '+nameOf(r)+' | '+(L(r,'Dec_Local')||'').slice(0,50));
      if (rows.length>40) console.log('   ...共'+rows.length+'行');
    } else {
      console.log('   样本: '+rows.slice(0,3).map(r=>L(r,'ID')+'|'+nameOf(r)).join(' / '));
    }
  }
}
