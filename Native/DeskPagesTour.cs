using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

public static class DeskPagesTourStore {
    public static string PathName {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPages","onboarding-v1.done");}}
    public static bool Seen {get{return File.Exists(PathName);}}
    public static void Mark(){try{string path=PathName;Directory.CreateDirectory(Path.GetDirectoryName(path));if(!File.Exists(path)){string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temp,"DeskPagesTour1");try{File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}}catch(Exception ex){DeskPagesStartupGate.Log("tour preference: "+ex.Message);}}
}
public sealed class DeskPagesTourCoach:Form {
    private readonly Button target;private readonly Form owner;private readonly Action dismiss;
    private readonly Timer pulse=new Timer{Interval=32};private int tick;private int side;
    public DeskPagesTourCoach(Form parent,Button button,Action onDismiss){
        owner=parent;target=button;dismiss=onDismiss;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;
        StartPosition=FormStartPosition.Manual;ClientSize=new Size(286,94);BackColor=Color.FromArgb(248,249,251);
        Font=new Font("Segoe UI",10);AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;DoubleBuffered=true;
        var close=new Button{FlatStyle=FlatStyle.Flat,BackColor=Color.White,AutoSize=false,Text="×",AccessibleName=DeskPagesWorkspaceApi.L("Skip tour","تخطي الجولة","דילוג על הסיור"),Location=new Point(14,12),Size=new Size(28,28),MinimumSize=Size.Empty,Padding=Padding.Empty,TabStop=true};
        close.FlatAppearance.BorderSize=0;close.Click+=delegate{dismiss();};Controls.Add(close);
        owner.LocationChanged+=Follow;owner.SizeChanged+=Follow;target.LocationChanged+=Follow;target.SizeChanged+=Follow;
        pulse.Tick+=delegate{tick++;Invalidate();};VisibleChanged+=delegate{if(Visible && !DeskPagesWorkspaceApi.ReduceMotion)pulse.Start();else pulse.Stop();};
    }
    public void Place(){
        if(!owner.Visible)return;Rectangle r=target.RectangleToScreen(target.ClientRectangle),area=Screen.FromControl(owner).WorkingArea;
        int x=owner.Left-Width-12,y=r.Top+r.Height/2-Height/2;side=1;
        if(x<area.Left+8){x=owner.Right+12;side=-1;}
        if(x+Width>area.Right-8){x=Math.Max(area.Left+8,Math.Min(r.Left+r.Width/2-Width/2,area.Right-Width-8));y=owner.Top-Height-12;side=0;if(y<area.Top+8){y=owner.Bottom+12;side=2;}}
        Location=new Point(x,Math.Max(area.Top+8,Math.Min(y,area.Bottom-Height-8)));Invalidate();
    }
    private void Follow(object sender,EventArgs e){Place();}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;float unit=ClientSize.Width/286f;
        using(var path=DeskPagesTheme.Round(new Rectangle(1,1,Width-3,Height-3),(int)(12*unit)))using(var b=new SolidBrush(Color.White))using(var p=new Pen(Color.FromArgb(214,222,233))){g.FillPath(b,path);g.DrawPath(p,path);}
        bool rtl=DeskPagesWorkspaceApi.Language!="en";var align=rtl?TextFormatFlags.Right|TextFormatFlags.RightToLeft:TextFormatFlags.Left;
        using(var f=new Font("Segoe UI",8.5f))TextRenderer.DrawText(g,"01 / 02",f,new Rectangle((int)(52*unit),(int)(13*unit),(int)(212*unit),(int)(19*unit)),Color.FromArgb(108,119,134),align);
        using(var f=new Font("Segoe UI",11.5f,FontStyle.Regular))TextRenderer.DrawText(g,DeskPagesWorkspaceApi.L("Add your first page here","أضف أول صفحة من هنا","הוסיפו כאן את הדף הראשון"),f,new Rectangle((int)(24*unit),(int)(44*unit),(int)(238*unit),(int)(31*unit)),DeskPagesTheme.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|align);
        g.ScaleTransform(unit,unit);float offset=DeskPagesWorkspaceApi.ReduceMotion?0:(float)Math.Sin(tick*.13)*2;
        using(var pen=new Pen(DeskPagesTheme.Accent,2)){pen.StartCap=LineCap.Round;pen.EndCap=LineCap.Round;
            if(side==1){float x=286-8+offset;g.DrawLine(pen,x-10,47,x,47);g.DrawLine(pen,x,47,x-4,43);g.DrawLine(pen,x,47,x-4,51);}
            else if(side==-1){float x=8-offset;g.DrawLine(pen,x+10,47,x,47);g.DrawLine(pen,x,47,x+4,43);g.DrawLine(pen,x,47,x+4,51);}
            else{float y=side==0?94-7:7;int s=side==0?1:-1;g.DrawLine(pen,286/2,y-10*s,286/2,y);g.DrawLine(pen,286/2,y,286/2-4,y-4*s);g.DrawLine(pen,286/2,y,286/2+4,y-4*s);}
        }
    }
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override void Dispose(bool disposing){if(disposing){pulse.Dispose();owner.LocationChanged-=Follow;owner.SizeChanged-=Follow;target.LocationChanged-=Follow;target.SizeChanged-=Follow;}base.Dispose(disposing);}
}

public sealed class DeskPagesNavigationTour:Form {
    public readonly DeskPagesTourIllustration Touchpad,Keyboard;
    private static string L(string en,string ar,string he){return DeskPagesWorkspaceApi.L(en,ar,he);}
    public DeskPagesNavigationTour(Action finished){
        DeskPagesFluent.Configure(this,L("Quick start · DeskPages","البدء السريع · DeskPages","התחלה מהירה · DeskPages"),new Size(680,486));
        Padding=new Padding(24);MinimumSize=new Size(650,510);BackColor=Color.FromArgb(247,248,250);
        var grid=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty};
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));grid.RowStyles.Add(new RowStyle(SizeType.Absolute,104));grid.RowStyles.Add(new RowStyle(SizeType.Percent,100));grid.RowStyles.Add(new RowStyle(SizeType.Absolute,68));Controls.Add(grid);
        var header=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty};
        header.RowStyles.Add(new RowStyle(SizeType.Absolute,25));header.RowStyles.Add(new RowStyle(SizeType.Absolute,40));header.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        bool rtl=RightToLeft==RightToLeft.Yes;ContentAlignment alignment=rtl?ContentAlignment.MiddleRight:ContentAlignment.MiddleLeft;
        header.Controls.Add(new Label{Text="DESKPAGES   /   02",ForeColor=DeskPagesTheme.Accent,Font=new Font("Segoe UI",8.5f,FontStyle.Bold),Dock=DockStyle.Fill,TextAlign=alignment,Margin=Padding.Empty},0,0);
        header.Controls.Add(new Label{Text=L("Your next page is a gesture away.","مساحة أكثر. بخطوة واحدة.","עוד מקום. בתנועה אחת."),Font=new Font("Segoe UI",21),ForeColor=DeskPagesTheme.Ink,Dock=DockStyle.Fill,TextAlign=alignment,Margin=Padding.Empty},0,1);
        header.Controls.Add(new Label{Text=L("Choose the way that feels natural.","اختر طريقة التنقل الأنسب لك.","בחרו את דרך המעבר הנוחה לכם."),Font=new Font("Segoe UI",10),ForeColor=DeskPagesTheme.Muted,Dock=DockStyle.Fill,TextAlign=alignment,Margin=Padding.Empty},0,2);grid.Controls.Add(header,0,0);
        var cards=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty,RightToLeft=RightToLeft.No};cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));cards.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        Touchpad=new DeskPagesTourIllustration(false){Dock=DockStyle.Fill,Margin=new Padding(0,0,7,0)};Keyboard=new DeskPagesTourIllustration(true){Dock=DockStyle.Fill,Margin=new Padding(7,0,0,0)};cards.Controls.Add(Touchpad,0,0);cards.Controls.Add(Keyboard,1,0);grid.Controls.Add(cards,0,1);
        var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0,14,0,0),RightToLeft=RightToLeft.No};footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,138));footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        footer.Controls.Add(new Label{Text=L("On the desktop. Touchpad support varies by device.","على سطح المكتب. دعم اللمس يعتمد على الجهاز.","בשולחן העבודה. תמיכת מחוות משתנה בין מכשירים."),Font=new Font("Segoe UI",8.5f),ForeColor=DeskPagesTheme.Muted,Dock=DockStyle.Fill,TextAlign=alignment,Margin=new Padding(0,0,16,0)},0,0);
        var done=DeskPagesFluent.Button(L("Let's start","ابدأ الاستخدام","בואו נתחיל"),delegate{Close();},true);done.Dock=DockStyle.Fill;done.Margin=new Padding(0,6,0,6);footer.Controls.Add(done,1,0);grid.Controls.Add(footer,0,2);AcceptButton=done;FormClosed+=delegate{finished();};
    }
}

public sealed class DeskPagesTourIllustration:Control {
    private readonly bool keys;private readonly Timer timer=new Timer{Interval=16};private readonly Stopwatch clock=new Stopwatch();private int preview=-1;
    private static readonly Color ink=Color.FromArgb(31,39,51),muted=Color.FromArgb(101,114,132),blue=Color.FromArgb(0,103,192);
    public DeskPagesTourIllustration(bool keyboard){keys=keyboard;DoubleBuffered=true;BackColor=Color.FromArgb(247,248,250);Font=new Font("Segoe UI",10);AccessibleName=keyboard?"Windows + Left or Right arrow":"Two-finger horizontal swipe";
        timer.Tick+=delegate{Invalidate();};VisibleChanged+=delegate{if(Visible && !DeskPagesWorkspaceApi.ReduceMotion){clock.Start();timer.Start();}else{timer.Stop();clock.Stop();}};
    }
    public void SetPreviewFrame(int value){preview=value;Invalidate();}
    private static void Round(Graphics g,RectangleF r,float radius,Color fill,Color line){using(var path=Path(r,radius)){if(fill.A>0)using(var b=new SolidBrush(fill))g.FillPath(b,path);if(line.A>0)using(var p=new Pen(line))g.DrawPath(p,path);}}
    private static GraphicsPath Path(RectangleF r,float radius){float d=radius*2;var p=new GraphicsPath();p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        float sx=Width/308f,sy=Height/266f;g.ScaleTransform(sx,sy);Round(g,new RectangleF(.5f,.5f,307,265),12,Color.White,Color.FromArgb(226,231,237));
        double frame=DeskPagesWorkspaceApi.ReduceMotion?40:(preview>=0?preview:clock.Elapsed.TotalSeconds*30);double phase=(frame%240)/120;bool backward=phase>=1;double t=phase%1;
        double move=Math.Max(0,Math.Min(1,(t-.23)/.46));move=move*move*(3-2*move);bool pressed=move>0 && move<1;
        // A small desktop, clipped independently of the input illustration.
        Round(g,new RectangleF(29,22,250,138),10,Color.FromArgb(233,237,242),Color.Transparent);
        Round(g,new RectangleF(27,19,254,138),9,Color.FromArgb(247,249,252),Color.FromArgb(209,218,228));
        var clip=g.Save();using(var screen=Path(new RectangleF(33,25,242,126),5))g.SetClip(screen);
        using(var bg=new LinearGradientBrush(new Rectangle(33,25,242,126),Color.FromArgb(215,233,255),Color.FromArgb(100,145,227),35f))g.FillRectangle(bg,33,25,242,126);
        using(var wave=new GraphicsPath()){wave.AddBezier(80,155,104,47,178,28,224,80);wave.AddBezier(224,80,241,99,202,153,285,146);wave.AddLine(285,146,285,170);wave.AddLine(285,170,80,170);using(var b=new SolidBrush(Color.FromArgb(92,107,169,233)))g.FillPath(b,wave);}
        using(var wave=new GraphicsPath()){wave.AddBezier(105,153,132,66,179,92,222,116);wave.AddBezier(222,116,255,133,280,88,290,50);wave.AddLine(290,50,290,160);wave.CloseFigure();using(var b=new SolidBrush(Color.FromArgb(100,56,112,209)))g.FillPath(b,wave);}
        float dx=(float)move*242*(backward?1:-1);DesktopIcons(g,43+dx,35,backward?2:1);DesktopIcons(g,43+dx+(backward?-242:242),35,backward?1:2);
        using(var b=new SolidBrush(Color.FromArgb(145,249,252,255)))g.FillRectangle(b,33,139,242,12);
        for(int i=0;i<4;i++)Round(g,new RectangleF(135+i*10,142,6,6),1,Color.FromArgb(100+i*15,138,192),Color.Transparent);
        g.Restore(clip);
        // Realistic trackpad / keyboard keycaps share a baseline and visual weight.
        if(!keys){Round(g,new RectangleF(100,172,108,49),8,Color.FromArgb(227,232,239),Color.Transparent);Round(g,new RectangleF(99,170,110,48),8,Color.FromArgb(246,248,251),Color.FromArgb(201,212,225));
            float shift=(float)move*35*(backward?1:-1),x=backward?127:162;int alpha=pressed?240:120;
            for(int i=0;i<2;i++){Round(g,new RectangleF(x+shift-i*18,178,9,23),4,Color.FromArgb(alpha,172,194,218),Color.FromArgb(alpha,127,157,192));using(var b=new SolidBrush(Color.FromArgb(alpha,0,103,192)))g.FillEllipse(b,x+shift-i*18+2,180,5,5);}
            using(var pen=new Pen(Color.FromArgb(130,0,103,192),1.5f)){float from=backward?127:175,to=backward?175:127;g.DrawLine(pen,from,208,to,208);g.DrawLine(pen,to,208,to+(backward?-4:4),205);g.DrawLine(pen,to,208,to+(backward?-4:4),211);}
        }else{Key(g,82,172,58,"",pressed,true);using(var b=new SolidBrush(pressed?blue:muted)){for(int i=0;i<4;i++)g.FillRectangle(b,102+i%2*7,186+i/2*7,5,5);}using(var p=new Pen(Color.FromArgb(155,166,181),1.5f)){g.DrawLine(p,151,191,161,191);g.DrawLine(p,156,186,156,196);}Key(g,174,172,46,backward?"←":"→",pressed,false);}
        string title=keys?DeskPagesWorkspaceApi.L("Keyboard","لوحة المفاتيح","מקלדת"):DeskPagesWorkspaceApi.L("Touchpad","لوحة اللمس","משטח מגע");
        string note=keys?DeskPagesWorkspaceApi.L("Win + Left / Right","Win مع السهم الأيمن أو الأيسر","Win עם חץ ימינה או שמאלה"):DeskPagesWorkspaceApi.L("Swipe left or right with two fingers","اسحب يمينًا أو يسارًا بإصبعين","החליקו ימינה או שמאלה בשתי אצבעות");
        using(var f=new Font("Segoe UI",14f,FontStyle.Bold,GraphicsUnit.Pixel))using(var b=new SolidBrush(ink))using(var fmt=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString(title,f,b,new RectangleF(10,224,288,19),fmt);
        using(var f=new Font("Segoe UI",11f,FontStyle.Regular,GraphicsUnit.Pixel))using(var b=new SolidBrush(muted))using(var fmt=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString(note,f,b,new RectangleF(8,244,292,16),fmt);
    }
    private static void DesktopIcons(Graphics g,float x,float y,int page){for(int i=0;i<(page==1?4:2);i++){float yy=y+i*21;Round(g,new RectangleF(x,yy,11,13),2,Color.FromArgb(240,250,253,255),Color.FromArgb(190,221,240));using(var b=new SolidBrush(i%2==0?Color.FromArgb(103,163,231):Color.FromArgb(235,192,106)))g.FillRectangle(b,x+2,yy+3,7,5);using(var p=new Pen(Color.FromArgb(100,255,255,255),1))g.DrawLine(p,x-2,yy+17,x+14,yy+17);}}
    private static void Key(Graphics g,float x,float y,float width,string text,bool active,bool windows){Round(g,new RectangleF(x,y+3,width,40),6,Color.FromArgb(214,222,232),Color.Transparent);Round(g,new RectangleF(x,y,width,40),6,active?Color.FromArgb(229,241,255):Color.FromArgb(248,250,252),active?Color.FromArgb(126,177,228):Color.FromArgb(207,216,227));if(!windows)using(var f=new Font("Segoe UI",24f,FontStyle.Regular,GraphicsUnit.Pixel))using(var b=new SolidBrush(active?blue:muted))using(var fmt=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString(text,f,b,new RectangleF(x,y,width,38),fmt);}
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}
