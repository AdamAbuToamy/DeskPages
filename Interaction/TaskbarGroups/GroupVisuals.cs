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
    private readonly Action open;private bool leaving;
    public GroupAnchor(LiveGroup group,Action show){
        open=show;Text="DeskPages · "+group.Number.ToString("00");Icon=GroupVisuals.Number(group.Number);ShowInTaskbar=true;FormBorderStyle=FormBorderStyle.FixedSingle;StartPosition=FormStartPosition.Manual;Size=new Size(280,150);WindowState=FormWindowState.Minimized;
    }
    protected override bool ShowWithoutActivation{get{return true;}}
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle=(p.ExStyle & ~0x08000080)|0x00040000;return p;}}
    protected override void OnActivated(EventArgs e){base.OnActivated(e);if(!leaving){WindowState=FormWindowState.Minimized;open();}}
    protected override void WndProc(ref Message m){if(m.Msg==0x8031){open();return;}base.WndProc(ref m);}
    protected override void OnFormClosing(FormClosingEventArgs e){if(!leaving){e.Cancel=true;open();}base.OnFormClosing(e);}
    public void Finish(){if(leaving)return;leaving=true;var icon=Icon;Close();icon.Dispose();Dispose();}
}
// A per-pixel layered surface keeps the white background translucent while
// preserving the plus glyph's contrast. No opacity is applied to the whole form.
internal sealed class GroupAddButton:Form {
    private readonly Action add;
    private readonly System.Windows.Forms.Timer animation=new System.Windows.Forms.Timer{Interval=15};
    private bool armed,hover;private float progress;
    public GroupAddButton(Action action){
        add=action;Text="DeskPages groups";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;Size=new Size(34,34);Cursor=Cursors.Hand;
        animation.Tick+=delegate{float target=armed && ClientRectangle.Contains(PointToClient(Cursor.Position))?1:0;progress+=(target-progress)*0.35f;if(Math.Abs(target-progress)<0.015f){progress=target;animation.Stop();}Render();};
    }
    protected override bool ShowWithoutActivation{get{return true;}}
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08080080;return p;}}
    protected override void OnShown(EventArgs e){base.OnShown(e);Render();}
    protected override void OnLocationChanged(EventArgs e){base.OnLocationChanged(e);Render();}
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Render();}
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hover=true;Render();}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=false;animation.Start();Render();}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(armed)animation.Start();}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;armed=true;Capture=true;animation.Start();}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button!=MouseButtons.Left)return;bool invoke=armed && ClientRectangle.Contains(e.Location);armed=false;Capture=false;animation.Start();if(invoke)add();}
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture && armed){armed=false;animation.Start();}}
    internal static Bitmap Surface(int width,int height,float pressed,bool over){
        var bitmap=new Bitmap(width,height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(bitmap)){
            g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.Transparent);
            float inset=1+pressed*2,offset=pressed;var r=new RectangleF(inset,inset+offset,width-inset*2-1,height-inset*2-1-offset);float radius=8;
            using(var path=new GraphicsPath()){
                path.AddArc(r.Left,r.Top,radius,radius,180,90);path.AddArc(r.Right-radius,r.Top,radius,radius,270,90);path.AddArc(r.Right-radius,r.Bottom-radius,radius,radius,0,90);path.AddArc(r.Left,r.Bottom-radius,radius,radius,90,90);path.CloseFigure();
                using(var fill=new SolidBrush(Color.FromArgb(over?210:175,255,255,255)))g.FillPath(fill,path);
                using(var edge=new Pen(Color.FromArgb(155,255,255,255)))g.DrawPath(edge,path);
            }
            float cx=width/2f,cy=height/2f+offset,arm=width*0.22f*(1-pressed*0.14f);
            using(var pen=new Pen(Color.FromArgb(255,35,45,62),1.8f)){pen.StartCap=pen.EndCap=LineCap.Round;g.DrawLine(pen,cx,cy-arm,cx,cy+arm);g.DrawLine(pen,cx-arm,cy,cx+arm,cy);}
        }return bitmap;
    }
    private void Render(){
        if(!IsHandleCreated || !Visible || Width<4 || Height<4)return;
        using(var bitmap=Surface(Width,Height,progress,hover)){
            IntPtr screen=GetDC(IntPtr.Zero),dc=IntPtr.Zero,h=IntPtr.Zero,old=IntPtr.Zero;
            try{dc=CreateCompatibleDC(screen);h=bitmap.GetHbitmap(Color.FromArgb(0));old=SelectObject(dc,h);var at=Location;var size=Size;var origin=Point.Empty;var blend=new Blend{Alpha=255,Format=1};if(!UpdateLayeredWindow(Handle,screen,ref at,ref size,dc,ref origin,0,ref blend,2))throw new System.ComponentModel.Win32Exception();}
            finally{if(old!=IntPtr.Zero)SelectObject(dc,old);if(h!=IntPtr.Zero)DeleteObject(h);if(dc!=IntPtr.Zero)DeleteDC(dc);if(screen!=IntPtr.Zero)ReleaseDC(IntPtr.Zero,screen);}
        }
    }
    protected override void Dispose(bool disposing){if(disposing)animation.Dispose();base.Dispose(disposing);}
    [StructLayout(LayoutKind.Sequential,Pack=1)]private struct Blend{public byte Operation,Flags,Alpha,Format;}
    [DllImport("user32.dll")]private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")]private static extern int ReleaseDC(IntPtr h,IntPtr dc);
    [DllImport("gdi32.dll")]private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]private static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")]private static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll",SetLastError=true)]private static extern bool UpdateLayeredWindow(IntPtr h,IntPtr dc,ref Point at,ref Size size,IntPtr source,ref Point origin,int key,ref Blend blend,int flags);
}
internal sealed class GroupDropCue:Form {
    private bool ready;
    public GroupDropCue(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;Enabled=false;BackColor=Color.Magenta;TransparencyKey=Color.Magenta;SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint,true);}
    protected override bool ShowWithoutActivation{get{return true;}}
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x080000A0;return p;}}
    public void Present(Rectangle rect,bool accepted){if(rect.IsEmpty){Hide();return;}Bounds=rect;ready=accepted;if(!Visible)Show();Invalidate();}
    protected override void OnPaint(PaintEventArgs e){using(var p=new Pen(ready?Color.FromArgb(83,222,171):Color.FromArgb(145,181,244),3))e.Graphics.DrawRectangle(p,2,2,Width-5,Height-5);if(ready)using(var font=new Font("Segoe UI",10,FontStyle.Bold))TextRenderer.DrawText(e.Graphics,"✓",font,ClientRectangle,Color.FromArgb(83,222,171),TextFormatFlags.Right|TextFormatFlags.Top);}
}
