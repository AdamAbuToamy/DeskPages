using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// One shared palette and DPI-scaled drawing surface for both utility windows.
public static class DeskPagesTheme {
    public static readonly Color Canvas=Color.FromArgb(243,243,243),Ink=Color.FromArgb(26,26,26),Muted=Color.FromArgb(96,96,96),Accent=Color.FromArgb(0,103,192),Line=Color.FromArgb(225,225,225);
    public static int Unit(Control c,int value){return (int)Math.Round(value*c.Font.Height/16.0);}
    public static GraphicsPath Round(Rectangle r,int radius){
        var path=new GraphicsPath();int d=Math.Max(1,Math.Min(radius*2,Math.Min(r.Width,r.Height)));
        path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d,r.Y,d,d,270,90);path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.X,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
    public static void Header(Form form,string subtitle){
        var header=new DeskPagesBrand{Dock=DockStyle.Top,Height=82,Text=subtitle};
        form.Controls.Add(header);
    }
    public static void Tabs(TabControl tabs){
        // Native sizing owns the tab header. Never send TCM_SETITEMSIZE from
        // Resize: that can trigger another native layout/handle recreation.
        tabs.DrawMode=TabDrawMode.Normal;tabs.SizeMode=TabSizeMode.Normal;
        tabs.Padding=new Point(9,7);tabs.ShowToolTips=true;
    }
    public static void Apply(Control root){
        foreach(Control c in root.Controls){
            if(!(c is Label))c.ForeColor=Ink;
            if(c is TabPage || c is FlowLayoutPanel)c.BackColor=Canvas;
            var list=c as ListView;if(list!=null){list.BorderStyle=BorderStyle.None;list.BackColor=Color.White;list.GridLines=false;list.ShowItemToolTips=true;}
            var box=c as TextBox;if(box!=null){box.BorderStyle=box.Parent is DeskPagesField || box.ReadOnly?BorderStyle.None:BorderStyle.FixedSingle;box.BackColor=Color.White;}
            if(c is CheckBox){c.Margin=new Padding(4,7,4,7);c.Cursor=Cursors.Hand;}
            if(c is ComboBox)c.BackColor=Color.White;
            Apply(c);
        }
    }
}

public sealed class DeskPagesActionButton:Button {
    private bool hover,pressed;
    public bool Primary;
    public DeskPagesActionButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;BackColor=Color.White;ForeColor=DeskPagesTheme.Ink;Cursor=Cursors.Hand;Padding=new Padding(12,6,12,6);MinimumSize=new Size(70,34);SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){pressed=true;Invalidate();base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.Clear(Parent==null?DeskPagesTheme.Canvas:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        var r=new Rectangle(0,0,Math.Max(1,Width-1),Math.Max(1,Height-1));
        Color bg=Primary?(pressed?Color.FromArgb(0,74,137):(hover?Color.FromArgb(0,90,158):DeskPagesTheme.Accent)):(pressed?Color.FromArgb(237,237,237):(hover?Color.FromArgb(249,249,249):Color.White));
        using(var path=DeskPagesTheme.Round(r,DeskPagesTheme.Unit(this,4)))using(var fill=new SolidBrush(Enabled?bg:DeskPagesTheme.Canvas))using(var border=new Pen(Primary?bg:DeskPagesTheme.Line)){e.Graphics.FillPath(fill,path);e.Graphics.DrawPath(border,path);}
        TextFormatFlags flags=TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis;
        if(!ShowKeyboardCues)flags|=TextFormatFlags.HidePrefix;if(RightToLeft==RightToLeft.Yes)flags|=TextFormatFlags.RightToLeft;
        TextRenderer.DrawText(e.Graphics,Text,Font,r,Enabled?(Primary?Color.White:DeskPagesTheme.Ink):DeskPagesTheme.Muted,flags);
        if(Focused && ShowFocusCues){r.Inflate(-4,-4);ControlPaint.DrawFocusRectangle(e.Graphics,r,Primary?Color.White:DeskPagesTheme.Accent,bg);}
    }
}

public sealed class DeskPagesBrand:Control {
    public DeskPagesBrand(){DoubleBuffered=true;BackColor=DeskPagesTheme.Canvas;}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;bool rtl=RightToLeft==RightToLeft.Yes;
        Func<int,int> u=delegate(int value){return DeskPagesTheme.Unit(this,value);};
        int size=u(40),x=rtl?Width-size-u(8):u(8),y=u(18);
        using(var p=DeskPagesTheme.Round(new Rectangle(x,y,size,size),u(11)))using(var b=new SolidBrush(DeskPagesTheme.Accent))e.Graphics.FillPath(b,p);
        using(var pen=new Pen(Color.White,Math.Max(1,u(2)))){e.Graphics.DrawRectangle(pen,x+u(10),y+u(10),u(17),u(19));e.Graphics.DrawLine(pen,x+u(15),y+u(7),x+u(30),y+u(7));e.Graphics.DrawLine(pen,x+u(30),y+u(7),x+u(30),y+u(25));}
        var title=new Rectangle(rtl?u(8):x+size+u(14),u(12),Math.Max(1,Width-size-u(36)),u(30));var sub=new Rectangle(title.X,u(43),title.Width,u(26));
        var flags=TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|(rtl?TextFormatFlags.Right|TextFormatFlags.RightToLeft:TextFormatFlags.Left);
        using(var font=new Font("Segoe UI",18,FontStyle.Bold))TextRenderer.DrawText(e.Graphics,"DeskPages",font,title,DeskPagesTheme.Ink,flags);
        TextRenderer.DrawText(e.Graphics,Text,Font,sub,DeskPagesTheme.Muted,flags);
    }
}

public sealed class DeskPagesSwitch:CheckBox {
    public DeskPagesSwitch(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Cursor=Cursors.Hand;}
    public override Size GetPreferredSize(Size proposedSize){Size text=TextRenderer.MeasureText(Text,Font);return new Size(text.Width+DeskPagesTheme.Unit(this,55),Math.Max(text.Height,DeskPagesTheme.Unit(this,24)));}
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.Clear(BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        int w=DeskPagesTheme.Unit(this,34),h=DeskPagesTheme.Unit(this,20),gap=DeskPagesTheme.Unit(this,12);bool rtl=RightToLeft==RightToLeft.Yes;
        var track=new Rectangle(rtl?Width-w-1:1,(Height-h)/2,w,h);Color color=Enabled?(Checked?DeskPagesTheme.Accent:Color.FromArgb(96,96,96)):DeskPagesTheme.Line;
        using(var path=DeskPagesTheme.Round(track,h/2))using(var fill=new SolidBrush(Checked?color:Color.White))using(var border=new Pen(color)){e.Graphics.FillPath(fill,path);e.Graphics.DrawPath(border,path);}
        int dot=h-6;int cx=(Checked!=rtl)?track.Right-dot-3:track.Left+3;using(var knob=new SolidBrush(Checked?Color.White:DeskPagesTheme.Muted))e.Graphics.FillEllipse(knob,cx,track.Top+3,dot,dot);
        var text=new Rectangle(rtl?0:w+gap,0,Math.Max(1,Width-w-gap),Height);
        var flags=TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|(rtl?TextFormatFlags.Right|TextFormatFlags.RightToLeft:TextFormatFlags.Left);
        if(!ShowKeyboardCues)flags|=TextFormatFlags.HidePrefix;
        TextRenderer.DrawText(e.Graphics,Text,Font,text,Enabled?DeskPagesTheme.Ink:DeskPagesTheme.Muted,flags);
        if(Focused && ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,text);
    }
    protected override void OnCheckedChanged(EventArgs e){base.OnCheckedChanged(e);Invalidate();}
}
