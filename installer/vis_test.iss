; 署名渲染对照测试安装器（一次性诊断用，不发布）
; 目的：一次运行看清 A/B/C/D 四种控件方案在 Inno7 皮肤下谁能显示：
;   A = TNewStaticText（窗口化静态文本，官方同类）——底部署名条候选
;   B = TLabel（图形标签，对照组）
;   C = TBitmapImage 裸位图（图形）——二维码名片候选
;   D = TPanel 包裹 TBitmapImage（窗口化容器）——二维码名片候选
; 玩家侧只看两页：欢迎页（tag=欢迎）与完成页（tag=完成）。

[Setup]
AppId={{6FE1D2C7-9A48-4E71-8B2F-3C0A9C77D001}
AppName=署名渲染测试
AppVersion=1.0.0
DefaultDirName={sd}\__wt_vis_test
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableWelcomePage=no
CreateAppDir=no
Uninstallable=no
PrivilegesRequired=lowest
WizardStyle=modern
OutputBaseFilename=署名渲染测试

[Files]
Source: "assets\douyin_night_card.bmp"; Flags: dontcopy

[Code]
const
  Douyin = 'https://v.douyin.com/z99n5ZQfkyw/';

procedure OpenUrl(Sender: TObject);
var
  ErrorCode: Integer;
begin
  ShellExec('open', Douyin, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

procedure PutVariants(host: TNewNotebookPage; tag: String);
var
  st: TNewStaticText;
  lb: TLabel;
  bm: TBitmapImage;
  pn: TPanel;
begin
  // A：窗口化静态文本（白底深蓝字，可点击）
  st := TNewStaticText.Create(host);
  st.Parent := host;
  st.Caption := tag + '页-A条(TNewStaticText)：MOD免费分享·请支持正版 | 作者Night 抖音号166660371（点击打开主页）';
  st.Color := clWhite;
  st.Font.Color := clNavy;
  st.Font.Size := 9;
  st.Alignment := taCenter;
  st.Cursor := crHand;
  st.OnClick := @OpenUrl;
  st.SetBounds(ScaleX(16), host.Height - ScaleY(44), host.Width - ScaleX(32), ScaleY(24));

  // B：图形标签对照（红字）
  lb := TLabel.Create(host);
  lb.Parent := host;
  lb.Caption := tag + '页-B行(TLabel红色对照)';
  lb.Font.Color := clRed;
  lb.Font.Size := 9;
  lb.SetBounds(ScaleX(16), host.Height - ScaleY(76), ScaleX(420), ScaleY(20));

  // C：裸位图（右侧靠外）
  bm := TBitmapImage.Create(host);
  bm.Parent := host;
  bm.Bitmap.LoadFromFile(ExpandConstant('{tmp}\douyin_night_card.bmp'));
  bm.Stretch := True;
  bm.SetBounds(host.Width - ScaleX(370), ScaleY(36), ScaleX(160), ScaleY(239));
  bm.Cursor := crHand;
  bm.OnClick := @OpenUrl;

  // D：面板包裹位图（右侧靠内）
  pn := TPanel.Create(host);
  pn.Parent := host;
  pn.SetBounds(host.Width - ScaleX(195), ScaleY(36), ScaleX(175), ScaleY(255));
  bm := TBitmapImage.Create(pn);
  bm.Parent := pn;
  bm.Bitmap.LoadFromFile(ExpandConstant('{tmp}\douyin_night_card.bmp'));
  bm.Stretch := True;
  bm.SetBounds(ScaleX(5), ScaleY(5), ScaleX(160), ScaleY(239));
  bm.Cursor := crHand;
  bm.OnClick := @OpenUrl;

  Log('VT: ' + tag + ' host=' + IntToStr(host.Width) + 'x' + IntToStr(host.Height) +
      ' A.T=' + IntToStr(st.Top) + ' B.T=' + IntToStr(lb.Top) +
      ' C.L=' + IntToStr(host.Width - ScaleX(370)) + ' D.L=' + IntToStr(host.Width - ScaleX(195)));
end;

procedure InitializeWizard();
begin
  ExtractTemporaryFile('douyin_night_card.bmp');
  PutVariants(WizardForm.WelcomePage, '欢迎');
  PutVariants(WizardForm.FinishedPage, '完成');
  Log('VT: InitializeWizard done');
end;
