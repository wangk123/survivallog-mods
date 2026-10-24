# PreDisasterSpeed 装机验证清单（作者执行）

部署：拷 SurvivalLog.PreDisasterSpeed.dll 到 <游戏>\BepInEx\plugins\，steam://rungameid/4164790 启动。
日志看 BepInEx `[PdsSpeed]` 打点（强杀不落盘，必要时看 cfg Verbose）。

## 灾前（新开局或灾前存档）
- [ ] CoreUI0 左下官方位出现五键条（慢速/原速/二倍/三倍/暂停）——.action-row 尾部（完成准备按钮同一排），不与功能按钮/完成准备重叠
- [ ] 悬停各键出 tooltip；当前档高亮正确（默认原速高亮）
- [ ] 点二倍：钟面/倒计时消耗肉眼 ×2；动作进度 ×2；再点原速恢复
- [ ] 点三倍：倒计时明显狂飙（×32，官方表值——观感反馈记录在此）
- [ ] 点暂停：倒计时冻结、动作冻结、屏幕出现官方暂停提示；再点原速恢复
- [ ] 点慢速：高亮后停手 3 秒，时间明显变慢（空闲 0.3）；一动就恢复
- [ ] 睡眠加速期间点手动档：睡眠加速被取消（官方行为）
- [ ] 倒计时走完→灾变过渡：HUD 切灾后（CoreUI1），档位自动回 1x（日志有"档位自动归 1x"），灾后官方倍速条可正常用
- [ ] 全程无异常刷屏；重进对局五键条回来（幂等重装）

## 桥自证（装机二轮起桥=Harmony prefix 挂 WebUILayer.OnPageMessage）
- [ ] 点任一速度键立即生效（倒计时/动作变速）；开 Verbose 后日志出现 `[PdsSpeed] SetSpeed(N)`
- [ ] 注入回执正常：日志无 `[PdsSpeed] 注入异常` 一次性警告
- [ ] 灾变过渡日志出现 `CoreUI0 消失（灾变过渡）——档位自动归 1x`（若灾前最后档位≠1）
- [ ] 若点键无效：查日志有无 Harmony 补丁异常（OnPageMessage prefix 加载失败），及 loaded 行是否带 "桥=OnPageMessage prefix"

## 开关
- [ ] cfg Enabled=false 重启：五键条不出现，游戏行为无任何差异

## 回归
- [ ] 灾后章节/无尽模式不受扰：官方倍速条照常，倒计时/结算正常
