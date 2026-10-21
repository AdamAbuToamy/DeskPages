using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;

public static class DeskPagesFluent {
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    public static void Configure(Form form,string title,Size size){
        form.Text=title;form.Font=new Font("Segoe UI",10);form.BackColor=DeskPagesTheme.Canvas;form.ForeColor=DeskPagesTheme.Ink;form.Padding=new Padding(16);form.ClientSize=size;form.MinimumSize=new Size(Math.Min(size.Width+16,360),Math.Min(size.Height+38,300));form.ShowInTaskbar=false;form.MaximizeBox=false;form.MinimizeBox=false;form.ShowIcon=false;form.StartPosition=FormStartPosition.CenterScreen;form.AutoScaleDimensions=new SizeF(96,96);form.AutoScaleMode=AutoScaleMode.Dpi;
        bool rtl=DeskPagesWorkspaceApi.Language=="ar" || DeskPagesWorkspaceApi.Language=="he";form.RightToLeft=rtl?RightToLeft.Yes:RightToLeft.No;form.RightToLeftLayout=rtl;form.KeyPreview=true;form.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Escape)form.Close();};
        form.HandleCreated+=delegate{if(Environment.OSVersion.Platform!=PlatformID.Win32NT)return;try{int round=2;DwmSetWindowAttribute(form.Handle,33,ref round,4);}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}};
    }
    public static Control Heading(string title,string note){var panel=new Panel{Dock=DockStyle.Fill};var heading=new Label{Text=title,Dock=DockStyle.Top,AutoSize=false,Height=32,Font=new Font("Segoe UI",18,FontStyle.Regular),ForeColor=DeskPagesTheme.Ink};var subtitle=new Label{Text=note,Dock=DockStyle.Bottom,Height=24,ForeColor=DeskPagesTheme.Muted,AutoEllipsis=true};bool rtl=DeskPagesWorkspaceApi.Language=="ar" || DeskPagesWorkspaceApi.Language=="he";heading.TextAlign=subtitle.TextAlign=rtl?ContentAlignment.MiddleRight:ContentAlignment.MiddleLeft;panel.Controls.Add(heading);panel.Controls.Add(subtitle);return panel;}
    public static DeskPagesActionButton Button(string title,Action action,bool primary){var button=new DeskPagesActionButton{Text=title,AccessibleName=title,Primary=primary,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Margin=new Padding(0,3,8,3),MinimumSize=new Size(84,34),Padding=new Padding(10,5,10,5)};button.Click+=delegate{action();};return button;}
    public static Control Row(string title,string description,Control action,int actionWidth){
        return new DeskPagesSettingsRow(title,description,action,actionWidth);
    }

}

// One layout owner for text and action: fixed nested table cells previously
// clipped both text rows and the RTL switch after Windows DPI/font scaling.
public sealed class DeskPagesSettingsRow:Panel {
    private readonly string title,description;
    private readonly Control action;
    private readonly int actionWidth;
    private readonly bool rtl;
    private bool arranging;
    public Rectangle TitleBounds {get;private set;}
    public Rectangle DescriptionBounds {get;private set;}
    public Control ActionControl {get{return action;}}
    public DeskPagesSettingsRow(string title,string description,Control action,int actionWidth){
        this.title=title;this.description=description;this.action=action;this.actionWidth=actionWidth;
        rtl=DeskPagesWorkspaceApi.Language=="ar" || DeskPagesWorkspaceApi.Language=="he";
        DoubleBuffered=true;BackColor=Color.White;Dock=DockStyle.Top;AutoSize=false;
        RightToLeft=RightToLeft.No;AccessibleRole=AccessibleRole.Grouping;AccessibleName=title;AccessibleDescription=description;
        action.Dock=DockStyle.None;action.Margin=Padding.Empty;action.RightToLeft=rtl?RightToLeft.Yes:RightToLeft.No;
        if(String.IsNullOrEmpty(action.AccessibleName))action.AccessibleName=title;
        action.AccessibleDescription=description;Controls.Add(action);
        Margin=new Padding(0,0,0,6);Height=84;
    }
    protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);if(action!=null){PerformLayout();Invalidate();}}
    protected override void OnLayout(LayoutEventArgs e){
        base.OnLayout(e);if(arranging || action==null)return;arranging=true;
        try{
            int pad=DeskPagesTheme.Unit(this,14),gap=DeskPagesTheme.Unit(this,16);
            int titleHeight=Font.Height+DeskPagesTheme.Unit(this,5),noteHeight=Font.Height+DeskPagesTheme.Unit(this,3);
            int wantedHeight=Math.Max(DeskPagesTheme.Unit(this,84),titleHeight+noteHeight+2*pad);
            if(Height!=wantedHeight)Height=wantedHeight;
            int inner=Math.Max(1,ClientSize.Width-2*pad);
            int preferred=Math.Max(DeskPagesTheme.Unit(this,actionWidth),action.GetPreferredSize(Size.Empty).Width);
            int width=Math.Min(preferred,Math.Max(1,inner/2));
            int height=Math.Min(DeskPagesTheme.Unit(this,48),Math.Max(1,ClientSize.Height-2*pad));
            action.Bounds=new Rectangle(rtl?pad:ClientSize.Width-pad-width,(ClientSize.Height-height)/2,width,height);
            int textLeft=rtl?action.Right+gap:pad;
            int textRight=rtl?ClientSize.Width-pad:action.Left-gap;
            int y=(ClientSize.Height-titleHeight-noteHeight)/2;
            TitleBounds=new Rectangle(textLeft,y,Math.Max(1,textRight-textLeft),titleHeight);
            DescriptionBounds=new Rectangle(textLeft,y+titleHeight,TitleBounds.Width,noteHeight);
        }finally{arranging=false;}
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using(var path=DeskPagesTheme.Round(new Rectangle(0,0,Math.Max(1,Width-1),Math.Max(1,Height-1)),DeskPagesTheme.Unit(this,6)))
        using(var pen=new Pen(DeskPagesTheme.Line))e.Graphics.DrawPath(pen,path);
        var flags=TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|
            (rtl?TextFormatFlags.Right|TextFormatFlags.RightToLeft:TextFormatFlags.Left);
        TextRenderer.DrawText(e.Graphics,title,Font,TitleBounds,DeskPagesTheme.Ink,flags);
        using(var noteFont=new Font(Font.FontFamily,Font.SizeInPoints*.9F,FontStyle.Regular))
            TextRenderer.DrawText(e.Graphics,description,noteFont,DescriptionBounds,DeskPagesTheme.Muted,flags);
    }
}

public sealed class DeskPagesSimpleSettings:Form {
    private readonly ComboBox language=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat};
    private readonly ComboBox captureMode=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat};
    private readonly ComboBox inbox=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat};
    private readonly DeskPagesModernToggle[] basic=new DeskPagesModernToggle[4];
    private readonly DeskPagesModernToggle reverse=new DeskPagesModernToggle(),keyboard=new DeskPagesModernToggle(),reduced=new DeskPagesModernToggle(),primaryOnly=new DeskPagesModernToggle();
    private readonly Timer load=new Timer{Interval=80},save=new Timer{Interval=180};
    public readonly DeskPagesModernSettingsSurface Surface=new DeskPagesModernSettingsSurface();
    private bool loading=true,ready,preview;private readonly string locale;private float scale=1;
    private sealed class Destination {public int Id;public string Name;public override string ToString(){return Name;}}
    private static string L(string en,string ar,string he){return DeskPagesWorkspaceApi.L(en,ar,he);}
    public DeskPagesSimpleSettings(){
        SuspendLayout();locale=DeskPagesWorkspaceApi.Language;
        Text=L("DeskPages settings","إعدادات DeskPages","הגדרות DeskPages");
        AutoScaleMode=AutoScaleMode.None;RightToLeft=RightToLeft.No;RightToLeftLayout=false;
        ClientSize=new Size(760,560);MinimumSize=new Size(660,500);BackColor=DeskPagesModernStyle.Background;
        ShowInTaskbar=false;ShowIcon=false;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterScreen;
        KeyPreview=true;KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Escape)Close();};Controls.Add(Surface);
        language.Items.AddRange(new object[]{"English","العربية","עברית"});
        Surface.Add(0,L("Language","اللغة","שפה"),L("Choose the app language","اختر لغة التطبيق","בחרו את שפת היישום"),language);
        for(int i=0;i<4;i++){basic[i]=new DeskPagesModernToggle();basic[i].CheckedChanged+=Changed;}
        Surface.Add(1,L("Touchpad swipe","السحب بلوحة اللمس","החלקה במשטח המגע"),L("Use a supported horizontal gesture","تنقل بإيماءة أفقية يدعمها جهازك","מעבר במחווה אופקית נתמכת"),basic[0]);
        Surface.Add(1,L("Windows + arrows","Windows + الأسهم","Windows + חצים"),L("Switch while you are on the desktop","بدّل الصفحات وأنت على سطح المكتب","מעבר כאשר אתם בשולחן העבודה"),keyboard);
        Surface.Add(1,L("Reverse swipe","عكس اتجاه السحب","היפוך החלקה"),L("Flip the direction of the touchpad gesture","اعكس اتجاه إيماءة لوحة اللمس","הפכו את כיוון המחווה"),reverse);
        Surface.Add(1,L("Drag between pages","السحب بين الصفحات","גרירה בין דפים"),L("Hold files at the outside screen edge","أمسك الملفات عند الحافة الخارجية للشاشة","החזיקו קבצים בקצה החיצוני של המסך"),basic[1]);
        Surface.Add(2,L("Page animation","حركة الانتقال","הנפשת מעבר"),L("Slide smoothly between pages","انتقال سلس عند تغيير الصفحة","מעבר חלק בין דפים"),basic[2]);
        Surface.Add(2,L("Page indicator","رقم الصفحة","מספר הדף"),L("A brief cue when the page changes","إشارة قصيرة عند تغيير الصفحة","חיווי קצר בעת מעבר דף"),basic[3]);
        Surface.Add(2,L("Reduce motion","تقليل الحركة","הפחתת תנועה"),L("Use pages without transition animations","تنقل دون حركات انتقال","מעבר ללא הנפשה"),reduced);
        captureMode.Items.AddRange(new object[]{L("Direct capture","التقاط مباشر","לכידה ישירה"),L("Cached · experimental","صور محفوظة · تجريبي","מטמון · ניסיוני")});
        Surface.Add(2,L("Page capture","صور التنقل","לכידת דפים"),L("Reuse recent page images","اختر الصور المحفوظة أو الالتقاط المباشر","בחרו מטמון או לכידה חדשה"),captureMode);
        captureMode.SelectedIndexChanged+=delegate{if(loading || !ready)return;try{DeskPagesWorkspaceApi.Execute("capture-mode",(captureMode.SelectedIndex==1).ToString());Surface.SetStatus(L("Saved","تم الحفظ","נשמר"));}catch(Exception ex){loading=true;try{captureMode.SelectedIndex=bool.Parse(DeskPagesWorkspaceApi.Execute("capture-settings")[0])?1:0;}finally{loading=false;}Surface.SetStatus(ex.Message);}};
        Surface.Performance.Click+=delegate{Run(delegate{DeskPagesWorkspaceApi.Execute("measure-resources");});};
        Surface.Add(3,L("New files","الملفات الجديدة","קבצים חדשים"),L("Choose the page for incoming files","اختر الصفحة التي تستقبل الملفات","בחרו דף לקבלת קבצים חדשים"),inbox);
        Surface.Add(3,L("Main display only","الشاشة الرئيسية فقط","מסך ראשי בלבד"),L("Keep other screens' icons in place","أبقِ أيقونات الشاشات الأخرى ثابتة","השאירו סמלים במסכים אחרים במקומם"),primaryOnly);
        Surface.Pins.Click+=delegate{using(var dialog=new DeskPagesPinnedIcons())dialog.ShowDialog(this);};
        Surface.Updates.Click+=delegate{DeskPagesWorkspaceApi.CheckUpdates();};
        Surface.Replay.Click+=delegate{DeskPagesWorkspaceApi.StartTour();};
        Surface.SaveLayout.Click+=delegate{Run(delegate{DeskPagesWorkspaceApi.Execute("save-layout");Surface.SetStatus(L("Layout saved","تم حفظ الترتيب","הסידור נשמר"));});};
        Surface.RestoreLayout.Click+=delegate{Restore();};
        primaryOnly.CheckedChanged+=delegate{if(loading || !ready)return;bool requested=primaryOnly.Checked;try{DeskPagesWorkspaceApi.Execute("display-scope",requested.ToString());DeskPagesWorkspaceApi.PrimaryOnly=requested;Surface.SetStatus(L("Saved","تم الحفظ","נשמר"));}catch(Exception ex){loading=true;try{var committed=DeskPagesWorkspaceApi.Execute("display-settings");primaryOnly.Checked=bool.Parse(committed[0]);DeskPagesWorkspaceApi.PrimaryOnly=primaryOnly.Checked;}catch{primaryOnly.Checked=!requested;}finally{loading=false;}Surface.SetStatus(ex.Message);}};
        keyboard.CheckedChanged+=Changed;reverse.CheckedChanged+=Changed;reduced.CheckedChanged+=Changed;inbox.SelectedIndexChanged+=Changed;language.SelectedIndexChanged+=Changed;
        Surface.SetStatus(L("Loading…","جارٍ التحميل…","בטעינה…"));load.Tick+=delegate{load.Stop();RefreshData();};save.Tick+=delegate{save.Stop();Persist();};
        FormClosing+=delegate(object sender,FormClosingEventArgs e){if(save.Enabled){save.Stop();if(!Persist())e.Cancel=true;}};
        Surface.SelectSection(0);ResumeLayout(true);
    }
    protected override void OnShown(EventArgs e){base.OnShown(e);if(!preview){using(var g=CreateGraphics())ApplyScale(g.DpiX/96F,760,560,true);}}
    public void SetPreviewLayout(float value,int width,int height){preview=true;ApplyScale(value,width,height,false);}
    private void ApplyScale(float value,float width,float height,bool fit){
        scale=Math.Max(.75F,Math.Min(4,value));Surface.RequestedScale=scale;
        var area=Screen.FromControl(this).WorkingArea;
        MinimumSize=new Size(Math.Min((int)(640*scale),Math.Max(320,area.Width-40)),Math.Min((int)(480*scale),Math.Max(300,area.Height-40)));
        ClientSize=new Size(fit?Math.Min((int)(width*scale),area.Width-60):(int)(width*scale),fit?Math.Min((int)(height*scale),area.Height-80):(int)(height*scale));
        if(fit)Location=new Point(Math.Max(area.Left+12,Math.Min(Left,area.Right-Width-12)),Math.Max(area.Top+12,Math.Min(Top,area.Bottom-Height-12)));
        Surface.PerformLayout();Surface.Invalidate();
    }
    protected override void WndProc(ref Message m){
        if(m.Msg==0x02E0){float width=ClientSize.Width/scale,height=ClientSize.Height/scale;int dpi=(int)((long)m.WParam & 0xffff);base.WndProc(ref m);ApplyScale(dpi/96F,width,height,true);return;}
        base.WndProc(ref m);
    }
    private void Run(Action action){try{action();}catch(Exception ex){Surface.SetStatus(L("Couldn't complete the action: ","تعذر إتمام العملية: ","לא ניתן להשלים: ")+ex.Message);DeskPagesStartupGate.Log("settings: "+ex.ToString());}}
    private void Restore(){Run(delegate{var details=DeskPagesWorkspaceApi.Execute("saved-layout-info");if(MessageBox.Show(this,L("Restore your saved pages and icon positions? Newer files will be kept.","استعادة الصفحات ومواضع الأيقونات المحفوظة؟ ستبقى الملفات الأحدث.","לשחזר דפים ומיקומי סמלים שמורים? קבצים חדשים יישמרו.")+Environment.NewLine+String.Join(Environment.NewLine,details),"DeskPages",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes){DeskPagesWorkspaceApi.Execute("restore-layout");Surface.SetStatus(L("Layout restored","تمت استعادة الترتيب","הסידור שוחזר"));}});}
    private void Changed(object sender,EventArgs e){if(loading || !ready)return;save.Stop();save.Start();Surface.SetStatus(L("Saving…","جارٍ الحفظ…","בשמירה…"));}
    private bool Persist(){
        if(!ready)return true;try{var destination=inbox.SelectedItem as Destination;if(destination==null)throw new InvalidOperationException("Select a destination.");string next=language.SelectedIndex==1?"ar":language.SelectedIndex==2?"he":"en";var values=DeskPagesSimplePreferences.Build(next,new[]{basic[0].Checked,basic[1].Checked,basic[2].Checked,basic[3].Checked},reverse.Checked,keyboard.Checked,reduced.Checked,destination.Id);DeskPagesWorkspaceApi.Execute("preferences-save",values);Surface.SetStatus(L("Saved automatically","تُحفظ التغييرات تلقائيًا","השינויים נשמרים אוטומטית"));if(next!=locale){DeskPagesWorkspaceApi.Language=next;DeskPagesWorkspaceApi.RefreshSettingsLanguage();}return true;}catch(Exception ex){Surface.SetStatus(L("Not saved. Try again.","لم تُحفظ التغييرات. أعد المحاولة.","לא נשמר. נסו שוב."));DeskPagesStartupGate.Log("preferences: "+ex.ToString());return false;}
    }
    public void QueueRefresh(){load.Stop();load.Start();}
    public void RefreshData(){Run(delegate{loading=true;try{var values=DeskPagesWorkspaceApi.Execute("settings");if(values.Length!=16)throw new InvalidOperationException("Invalid settings response.");var scope=DeskPagesWorkspaceApi.Execute("display-settings");if(scope.Length!=1)throw new InvalidOperationException("Invalid display settings.");primaryOnly.Checked=bool.Parse(scope[0]);captureMode.SelectedIndex=bool.Parse(DeskPagesWorkspaceApi.Execute("capture-settings")[0])?1:0;language.SelectedIndex=values[1]=="ar"?1:values[1]=="he"?2:0;int[] indices={7,12,10,11};for(int i=0;i<4;i++)basic[i].Checked=bool.Parse(values[indices[i]]);reverse.Checked=bool.Parse(values[8]);keyboard.Checked=bool.Parse(values[9]);reduced.Checked=bool.Parse(values[2]);inbox.Items.Clear();inbox.Items.Add(new Destination{Id=0,Name=L("Current page","الصفحة الحالية","הדף הנוכחי")});foreach(var row in DeskPagesWorkspaceApi.Execute("pages")){var f=row.Split('\t');if(f.Length==6 && f[3]!="True")inbox.Items.Add(new Destination{Id=int.Parse(f[0]),Name=f[1]});}for(int i=0;i<inbox.Items.Count;i++)if(((Destination)inbox.Items[i]).Id==int.Parse(values[0]))inbox.SelectedIndex=i;ready=true;Surface.SetStatus(L("Changes save automatically","تُحفظ التغييرات تلقائيًا","השינויים נשמרים אוטומטית"));}finally{loading=false;}});}
    protected override void Dispose(bool disposing){if(disposing){load.Dispose();save.Dispose();}base.Dispose(disposing);}
}

// Explicit file list keeps pinning available after the desktop loses focus.
public sealed class DeskPagesPinnedIcons:Form {
    private readonly CheckedListBox list=new CheckedListBox{Dock=DockStyle.Fill,CheckOnClick=true,IntegralHeight=false,HorizontalScrollbar=true};
    private readonly Label status=new Label{Dock=DockStyle.Bottom,Height=42,AutoEllipsis=true};
    private readonly System.Collections.Generic.List<string[]> entries=new System.Collections.Generic.List<string[]>();
    private static string L(string en,string ar,string he){return DeskPagesWorkspaceApi.L(en,ar,he);}
    public DeskPagesPinnedIcons(){
        DeskPagesFluent.Configure(this,L("Pinned icons","الأيقونات الثابتة","סמלים קבועים"),new Size(570,440));
        var heading=DeskPagesFluent.Heading(L("Show on every page","إظهار في كل الصفحات","הצגה בכל הדפים"),L("Check icons to keep them visible on all pages.","حدد الأيقونات التي تريد إبقاءها ظاهرة في كل الصفحات.","בחרו סמלים שיוצגו בכל הדפים."));heading.Dock=DockStyle.Top;heading.Height=76;
        var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48};
        footer.Controls.Add(DeskPagesFluent.Button(L("Save","حفظ","שמירה"),Save,true));
        footer.Controls.Add(DeskPagesFluent.Button(L("Close","إغلاق","סגירה"),Close,false));
        Controls.Add(list);Controls.Add(status);Controls.Add(footer);Controls.Add(heading);
        Shown+=delegate{LoadIcons();};
    }
    public void LoadIcons(){try{list.Items.Clear();entries.Clear();foreach(string row in DeskPagesWorkspaceApi.Execute("pinned-icons")){
        var f=row.Split('\t');if(f.Length!=4)throw new InvalidOperationException("Invalid pin list.");
        entries.Add(f);string name=f[0]=="file"?System.IO.Path.GetFileName(f[3]):f[2]=="{20D04FE0-3AEA-1069-A2D8-08002B30309D}"?L("This PC","هذا الكمبيوتر","מחשב זה"):f[2]=="{645FF040-5081-101B-9F08-00AA002F954E}"?L("Recycle Bin","سلة المحذوفات","סל המיחזור"):L("Control Panel","لوحة التحكم","לוח הבקרה");
        list.Items.Add(name+" — "+(f[0]=="file"?f[3]:L("Windows icon","أيقونة ويندوز","סמל Windows")),bool.Parse(f[1]));
    }status.Text=L("Uncheck to show an icon only on its own page.","أزل التحديد لإظهار الأيقونة في صفحتها الأصلية فقط.","בטלו סימון להצגה בדף המקורי בלבד.");}catch(Exception ex){status.Text=ex.Message;}}
    private void Save(){try{var changes=new System.Collections.Generic.List<string>();for(int i=0;i<entries.Count;i++){var f=entries[i];bool value=list.GetItemChecked(i);if(value!=bool.Parse(f[1]))changes.Add(f[0]+"\t"+value+"\t"+(f[0]=="file"?f[3]:f[2]));}if(changes.Count>0)DeskPagesWorkspaceApi.Execute("pins-save",changes.ToArray());LoadIcons();status.Text=L("Saved","تم الحفظ","נשמר");}catch(Exception ex){status.Text=ex.Message;}}
}
