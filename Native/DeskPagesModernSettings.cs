using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// The settings window has one physical-pixel layout owner. No nested tables,
// automatic sizing or mirrored native parent windows participate in its layout.
public static class DeskPagesModernStyle {
    public static readonly Color Background=Color.FromArgb(245,247,251),Ink=Color.FromArgb(25,38,59),Muted=Color.FromArgb(108,121,141),Line=Color.FromArgb(229,234,242),Blue=Color.FromArgb(48,93,230);
    public static void Round(Graphics g,Rectangle r,int radius,Color fill,Color border){
        if(r.Width<2 || r.Height<2)return;
        using(var path=DeskPagesTheme.Round(new Rectangle(r.X,r.Y,r.Width-1,r.Height-1),radius)){
            using(var brush=new SolidBrush(fill))g.FillPath(brush,path);
            if(border!=Color.Empty)using(var pen=new Pen(border))g.DrawPath(pen,path);
        }
    }
    public static void Text(Graphics g,string value,Rectangle r,float pixels,Color color,bool rtl,bool bold,bool center){
        using(var font=new Font("Segoe UI",pixels,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel))
        using(var brush=new SolidBrush(color))using(var format=new StringFormat()){
            format.Alignment=center?StringAlignment.Center:StringAlignment.Near;format.LineAlignment=StringAlignment.Center;
            format.Trimming=StringTrimming.EllipsisCharacter;format.FormatFlags=StringFormatFlags.NoWrap|(rtl?StringFormatFlags.DirectionRightToLeft:0);
            g.DrawString(value,font,brush,r,format);
        }
    }
}
public sealed class DeskPagesModernButton:Button {
    public bool Active,Primary,IsRtl;public float UiScale=1;private bool hover;
    public DeskPagesModernButton(){AutoSize=false;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;RightToLeft=RightToLeft.No;TabStop=true;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.Clear(Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Color fill=Primary?DeskPagesModernStyle.Blue:Active?Color.FromArgb(234,239,255):hover?Color.FromArgb(239,243,249):Color.White;
        DeskPagesModernStyle.Round(e.Graphics,ClientRectangle,(int)(9*UiScale),fill,Primary||Active?Color.Empty:DeskPagesModernStyle.Line);
        var text=Rectangle.Inflate(ClientRectangle,-(int)(12*UiScale),0);
        DeskPagesModernStyle.Text(e.Graphics,Text,text,14*UiScale,Primary?Color.White:Active?DeskPagesModernStyle.Blue:DeskPagesModernStyle.Ink,IsRtl,Active,false);
        if(Focused && ShowFocusCues){var r=Rectangle.Inflate(ClientRectangle,-4,-4);ControlPaint.DrawFocusRectangle(e.Graphics,r);}
    }
}
public sealed class DeskPagesModernToggle:CheckBox {
    public float UiScale=1;public bool IsRtl;
    public Rectangle TrackBounds {get{return new Rectangle((int)(3*UiScale),(Height-(int)(24*UiScale))/2,(int)(44*UiScale),(int)(24*UiScale));}}
    public DeskPagesModernToggle(){AutoSize=false;RightToLeft=RightToLeft.No;Cursor=Cursors.Hand;TabStop=true;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.Clear(BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;var r=TrackBounds;
        DeskPagesModernStyle.Round(e.Graphics,r,r.Height/2,Checked?DeskPagesModernStyle.Blue:Color.FromArgb(208,216,229),Color.Empty);
        int inset=Math.Max(2,(int)(3*UiScale)),dot=r.Height-2*inset;
        int x=Checked!=IsRtl?r.Right-dot-inset:r.Left+inset;
        using(var brush=new SolidBrush(Color.White))e.Graphics.FillEllipse(brush,x,r.Top+inset,dot,dot);
        if(Focused && ShowFocusCues){var focus=Rectangle.Inflate(r,2,2);focus.Intersect(ClientRectangle);ControlPaint.DrawFocusRectangle(e.Graphics,focus);}
    }
    protected override void OnCheckedChanged(EventArgs e){base.OnCheckedChanged(e);Invalidate();}
}
public sealed class DeskPagesModernSettingsItem {
    public string Title,Description;public Control Action;public Rectangle RowBounds,TitleBounds,DescriptionBounds;
    public DeskPagesModernSettingsItem(string title,string description,Control action){Title=title;Description=description;Action=action;action.AccessibleName=title;action.AccessibleDescription=description;action.RightToLeft=RightToLeft.No;action.BackColor=Color.White;}
}
public sealed class DeskPagesModernSettingsSurface:Panel {
    public readonly List<DeskPagesModernSettingsItem>[] Pages={new List<DeskPagesModernSettingsItem>(),new List<DeskPagesModernSettingsItem>(),new List<DeskPagesModernSettingsItem>(),new List<DeskPagesModernSettingsItem>()};
    private readonly DeskPagesModernButton[] tabs=new DeskPagesModernButton[4];
    public readonly DeskPagesModernButton Updates=new DeskPagesModernButton(),Pins=new DeskPagesModernButton();
    private readonly Image brand=DeskPagesTheme.LoadBrand();
    public readonly DeskPagesMemoryMeter Memory=new DeskPagesMemoryMeter();
    public readonly DeskPagesModernButton Replay=new DeskPagesModernButton(),SaveLayout=new DeskPagesModernButton(),RestoreLayout=new DeskPagesModernButton();
    public int Section {get;private set;}public float RequestedScale=1;public float UiScale=1;public bool IsRtl;
    public string Status="";private Rectangle card,extra;private bool arranging,initialized;private Font fieldFont;
    private static string L(string en,string ar,string he){return DeskPagesWorkspaceApi.L(en,ar,he);}
    public DeskPagesModernSettingsSurface(){
        Dock=DockStyle.Fill;DoubleBuffered=true;BackColor=DeskPagesModernStyle.Background;RightToLeft=RightToLeft.No;
        IsRtl=DeskPagesWorkspaceApi.Language=="ar" || DeskPagesWorkspaceApi.Language=="he";
        string[] names={L("General","عام","כללי"),L("Navigation","التنقل","ניווט"),L("Appearance","المظهر","מראה"),L("Pages","الصفحات","דפים")};
        for(int i=0;i<4;i++){int section=i;tabs[i]=new DeskPagesModernButton{Text=names[i],AccessibleName=names[i],IsRtl=IsRtl};tabs[i].Click+=delegate{SelectSection(section);};Controls.Add(tabs[i]);}
        Updates.Text=L("Check for updates","فحص التحديثات","בדיקת עדכונים");Pins.Text=L("Pinned icons","أيقونات ثابتة","סמלים קבועים");
        Replay.Text=L("Start the tour","ابدأ الجولة","התחלת הסיור");Replay.Primary=true;
        SaveLayout.Text=L("Save layout","حفظ الترتيب","שמירת סידור");SaveLayout.Primary=true;
        RestoreLayout.Text=L("Restore","استعادة","שחזור");
        foreach(var button in new[]{Replay,SaveLayout,RestoreLayout,Updates,Pins}){button.AccessibleName=button.Text;button.IsRtl=IsRtl;Controls.Add(button);}
        Controls.Add(Memory);
        initialized=true;
    }
    public void Add(int section,string title,string note,Control action){Pages[section].Add(new DeskPagesModernSettingsItem(title,note,action));if(fieldFont!=null)action.Font=fieldFont;Controls.Add(action);}
    public void SelectSection(int section){Section=Math.Max(0,Math.Min(3,section));PerformLayout();Invalidate();}
    public void SetStatus(string value){Status=value;Invalidate();}
    private Rectangle R(float x,float y,float w,float h){return new Rectangle((int)Math.Round(x*UiScale),(int)Math.Round(y*UiScale),(int)Math.Round(w*UiScale),(int)Math.Round(h*UiScale));}
    protected override void OnResize(EventArgs e){base.OnResize(e);PerformLayout();Invalidate();}
    protected override void OnLayout(LayoutEventArgs e){
        base.OnLayout(e);if(arranging || !initialized)return;arranging=true;
        try{
            // A monitor or a resized window can be smaller than the requested DPI canvas.
            // Keep the complete settings page visible within that actual client area.
            UiScale=Math.Max(.5F,Math.Min(RequestedScale,Math.Min(ClientSize.Width/640F,ClientSize.Height/480F)));
            float width=ClientSize.Width/UiScale,rail=width<700?132:156,outer=20,gap=24;
            if(fieldFont==null || Math.Abs(fieldFont.Size-14*UiScale)>.1F){var old=fieldFont;fieldFont=new Font("Segoe UI",14*UiScale,FontStyle.Regular,GraphicsUnit.Pixel);foreach(var page in Pages)foreach(var item in page)item.Action.Font=fieldFont;if(old!=null)old.Dispose();}
            float railX=IsRtl?width-outer-rail:outer,contentX=IsRtl?outer:outer+rail+gap,contentWidth=width-2*outer-rail-gap;
            for(int i=0;i<4;i++){tabs[i].UiScale=UiScale;tabs[i].Active=Section==i;tabs[i].Bounds=R(railX+12,126+i*48,rail-24,40);tabs[i].Invalidate();}
            card=R(contentX,112,contentWidth,Pages[Section].Count*76);
            for(int page=0;page<4;page++)for(int i=0;i<Pages[page].Count;i++){
                var item=Pages[page][i];item.Action.Visible=page==Section;
                if(page!=Section)continue;
                float y=112+i*76,actionWidth=item.Action is ComboBox?164:item.Action is Button?140:50,actionHeight=item.Action is ComboBox?((ComboBox)item.Action).PreferredHeight/UiScale:36;
                item.RowBounds=R(contentX,y,contentWidth,76);
                item.Action.Bounds=R(IsRtl?contentX+20:contentX+contentWidth-20-actionWidth,y+(76-actionHeight)/2,actionWidth,actionHeight);
                var toggle=item.Action as DeskPagesModernToggle;if(toggle!=null){toggle.UiScale=UiScale;toggle.IsRtl=IsRtl;toggle.Invalidate();}
                var combo=item.Action as ComboBox;if(combo!=null)combo.RightToLeft=IsRtl?RightToLeft.Yes:RightToLeft.No;
                float tx=IsRtl?contentX+20+actionWidth+20:contentX+20;
                float tw=contentWidth-60-actionWidth;
                item.TitleBounds=R(tx,y+14,tw,24);item.DescriptionBounds=R(tx,y+40,tw,19);
            }
            extra=R(contentX,112+Pages[Section].Count*76+18,contentWidth,Section==0?194:134);
            Updates.Visible=Section==0;Pins.Visible=Section==3;Memory.Visible=Section==0;Replay.Visible=Section==0;SaveLayout.Visible=RestoreLayout.Visible=Section==3;
            foreach(var button in new[]{Replay,SaveLayout,RestoreLayout,Updates,Pins})button.UiScale=UiScale;
            float extraY=112+Pages[Section].Count*76+18;
            Replay.Bounds=R(IsRtl?contentX+contentWidth-164:contentX+20,extraY+100,144,36);
            Memory.UiScale=UiScale;Memory.IsRtl=IsRtl;Memory.Bounds=R(IsRtl?contentX+20:contentX+176,extraY+100,180,80);
            Updates.Bounds=R(IsRtl?contentX+contentWidth-164:contentX+20,extraY+144,144,36);
            float saveX=IsRtl?contentX+contentWidth-130:contentX+20;
            SaveLayout.Bounds=R(saveX,extraY+78,110,36);
            RestoreLayout.Bounds=R(IsRtl?saveX-98:saveX+118,extraY+78,90,36);
            Pins.Bounds=R(IsRtl?saveX-246:saveX+216,extraY+78,140,36);
        }finally{arranging=false;}
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        float width=ClientSize.Width/UiScale,height=ClientSize.Height/UiScale,rail=width<700?132:156,railX=IsRtl?width-20-rail:20;
        float cx=IsRtl?20:20+rail+24,cw=width-40-rail-24;
        DeskPagesModernStyle.Round(g,R(railX,20,rail,height-40),(int)(16*UiScale),Color.White,DeskPagesModernStyle.Line);
        var mark=R(railX+(rail-48)/2,32,48,48);if(brand!=null)g.DrawImage(brand,mark);
        Draw(g,"DeskPages",R(railX+8,84,rail-16,24),15,DeskPagesModernStyle.Ink,true,true);
        Draw(g,"0.8.29 · Experiment",R(railX+8,height-67,rail-16,22),11,DeskPagesModernStyle.Muted,false,true);
        string[] titles={L("General","عام","כללי"),L("Navigation","التنقل","ניווט"),L("Appearance","المظهر","מראה"),L("Pages","الصفحات","דפים")};
        string[] notes={L("Make DeskPages feel like home.","خصص تجربة DeskPages كما تحب.","התאימו את DeskPages אליכם."),L("Choose how you move between pages.","اختر طريقتك للتنقل بين الصفحات.","בחרו כיצד לעבור בין דפים."),L("A calmer desktop, your way.","اضبط حركة الصفحات ومظهرها.","התאימו את תנועת הדפים והמראה."),L("Keep every file in the right place.","نظم ملفاتك واحفظ ترتيبك المفضل.","ארגנו קבצים ושמרו את הסידור." )};
        Draw(g,titles[Section],R(cx,30,cw,40),27,DeskPagesModernStyle.Ink,true,false);
        Draw(g,notes[Section],R(cx,76,cw,24),13,DeskPagesModernStyle.Muted,false,false);
        DeskPagesModernStyle.Round(g,card,(int)(14*UiScale),Color.White,DeskPagesModernStyle.Line);
        for(int i=0;i<Pages[Section].Count;i++){
            var item=Pages[Section][i];Draw(g,item.Title,item.TitleBounds,14,DeskPagesModernStyle.Ink,true,false);Draw(g,item.Description,item.DescriptionBounds,12,DeskPagesModernStyle.Muted,false,false);
            if(i>0)using(var pen=new Pen(DeskPagesModernStyle.Line))g.DrawLine(pen,item.RowBounds.Left+(int)(20*UiScale),item.RowBounds.Top,item.RowBounds.Right-(int)(20*UiScale),item.RowBounds.Top);
        }
        if(Section==0 || Section==3){
            DeskPagesModernStyle.Round(g,extra,(int)(14*UiScale),Section==0?Color.FromArgb(235,241,255):Color.White,Section==0?Color.FromArgb(221,230,253):DeskPagesModernStyle.Line);
            int pad=(int)(20*UiScale);var title=new Rectangle(extra.X+pad,extra.Y+(int)(17*UiScale),extra.Width-2*pad,(int)(27*UiScale));var note=new Rectangle(title.X,extra.Y+(int)(49*UiScale),title.Width,(int)(22*UiScale));
            Draw(g,Section==0?L("A quick tour","جولة قصيرة، وبداية أسهل","סיור קצר להתחלה קלה"):L("Your saved arrangement","ترتيبك المحفوظ","הסידור השמור שלכם"),title,16,DeskPagesModernStyle.Ink,true,false);
            Draw(g,Section==0?L("Learn to add pages and move between them.","تعلّم إضافة الصفحات والتنقل بينها بخطوات بسيطة.","למדו להוסיף דפים ולעבור ביניהם."):L("Save your layout, or restore it whenever you need.","احفظ مواضع الأيقونات أو استعدها وقتما تحتاج.","שמרו מיקומי סמלים או שחזרו בעת הצורך."),note,12,DeskPagesModernStyle.Muted,false,false);
        }
        Draw(g,Status,R(cx,height-48,cw,26),12,DeskPagesModernStyle.Muted,false,false);
    }
    protected override void Dispose(bool disposing){if(disposing){if(fieldFont!=null)fieldFont.Dispose();if(brand!=null)brand.Dispose();}base.Dispose(disposing);}
    private void Draw(Graphics g,string text,Rectangle r,float size,Color color,bool bold,bool center){DeskPagesModernStyle.Text(g,text,r,size*UiScale,color,IsRtl,bold,center);}
}
