using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class GroupVisuals {
    public static Icon Number(int number){
        using(var image=new Bitmap(64,64))using(var g=Graphics.FromImage(image)){
            g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.Transparent);
            using(var shape=new GraphicsPath()){
                shape.AddArc(3,3,20,20,180,90);shape.AddArc(41,3,20,20,270,90);shape.AddArc(41,41,20,20,0,90);shape.AddArc(3,41,20,20,90,90);shape.CloseFigure();
                using(var fill=new LinearGradientBrush(new Rectangle(3,3,58,58),Color.FromArgb(40,52,73),Color.FromArgb(13,20,34),90f))g.FillPath(fill,shape);
                using(var edge=new Pen(Color.FromArgb(112,146,193),1.6f))g.DrawPath(edge,shape);
            }
            using(var font=new Font("Segoe UI Semibold",number<10?31:25,FontStyle.Bold,GraphicsUnit.Pixel))using(var brush=new SolidBrush(Color.FromArgb(229,239,255)))using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString(number.ToString("00"),font,brush,new RectangleF(0,0,64,62),format);
            var h=image.GetHicon();try{return (Icon)Icon.FromHandle(h).Clone();}finally{DestroyIcon(h);}
        }
    }
    [DllImport("user32.dll")]private static extern bool DestroyIcon(IntPtr icon);
}
internal sealed class GroupAnchor:Form {
    private readonly Action open;private bool leaving,opening;
    public GroupAnchor(LiveGroup group,Action show){
        open=show;Text="DeskPages · "+group.Number.ToString("00");Icon=GroupVisuals.Number(group.Number);ShowInTaskbar=true;FormBorderStyle=FormBorderStyle.FixedSingle;StartPosition=FormStartPosition.Manual;Size=new Size(280,150);WindowState=FormWindowState.Minimized;
    }
    protected override bool ShowWithoutActivation{get{return true;}}
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle=(p.ExStyle & ~0x08000080)|0x00040000;return p;}}
    protected override void OnActivated(EventArgs e){base.OnActivated(e);if(!leaving && WindowState!=FormWindowState.Minimized)WindowState=FormWindowState.Minimized;}
    private void RequestOpen(){if(leaving || opening)return;opening=true;BeginInvoke((Action)delegate{opening=false;if(!leaving)open();});}
    protected override void WndProc(ref Message m){
        // Reject restoration before a normal application window can be displayed.
        if(!leaving && (m.Msg==0x13 || m.Msg==0x8031 || (m.Msg==0x112 && (m.WParam.ToInt64()&0xFFF0)==0xF120))){RequestOpen();m.Result=IntPtr.Zero;return;}
        base.WndProc(ref m);
    }
    protected override void OnFormClosing(FormClosingEventArgs e){if(!leaving){e.Cancel=true;open();}base.OnFormClosing(e);}
    public void Finish(){if(leaving)return;leaving=true;var icon=Icon;Close();icon.Dispose();Dispose();}
}
internal sealed class GroupDropCue:Form {
    private bool ready;
    public GroupDropCue(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;Enabled=false;BackColor=Color.Magenta;TransparencyKey=Color.Magenta;SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint,true);}
    protected override bool ShowWithoutActivation{get{return true;}}
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x080000A0;return p;}}
    public void Present(Rectangle rect,bool accepted){if(rect.IsEmpty){Hide();return;}Bounds=rect;ready=accepted;if(!Visible)Show();Invalidate();}
    protected override void OnPaint(PaintEventArgs e){using(var p=new Pen(ready?Color.FromArgb(83,222,171):Color.FromArgb(145,181,244),3))e.Graphics.DrawRectangle(p,2,2,Width-5,Height-5);if(ready)using(var font=new Font("Segoe UI",10,FontStyle.Bold))TextRenderer.DrawText(e.Graphics,"✓",font,ClientRectangle,Color.FromArgb(83,222,171),TextFormatFlags.Right|TextFormatFlags.Top);}
}

internal sealed class GroupDragGhost:Form {
    private Icon image;
    public GroupDragGhost(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;Enabled=false;Size=new Size(36,36);BackColor=Color.Magenta;TransparencyKey=Color.Magenta;SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint,true);}
    protected override bool ShowWithoutActivation{get{return true;}}
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x080000A0;return p;}}
    public void Begin(GroupDragSource source){
        if(image!=null){image.Dispose();image=null;}
        try{if(source.Windows.Length>0 && !string.IsNullOrEmpty(source.Windows[0].ImagePath))image=Icon.ExtractAssociatedIcon(source.Windows[0].ImagePath);}catch(Exception){}
        if(image==null)image=(Icon)SystemIcons.Application.Clone();
    }
    public void Present(Point point){Location=new Point(point.X+12,point.Y-42);if(!Visible)Show();Invalidate();}
    protected override void OnPaint(PaintEventArgs e){if(image!=null)e.Graphics.DrawIcon(image,new Rectangle(2,2,32,32));}
    protected override void Dispose(bool disposing){if(disposing && image!=null){image.Dispose();image=null;}base.Dispose(disposing);}
}
