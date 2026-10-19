using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Independent no-activate, click-through HUD. It is hidden before desktop capture
// and shown only after the native transition retires. No hooks or file access.
public sealed class DeskPagesIndicator:Form {
    private readonly System.Windows.Forms.Timer timer;
    private readonly DeskPagesIndicatorPolicy timing=new DeskPagesIndicatorPolicy();
    private int page;
    private const int ExtendedStyles=0x08000000|0x00000020|0x00000080|0x00080000;
    public DeskPagesIndicator(){
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        ClientSize=new Size(76,76);BackColor=Color.FromArgb(28,36,49);ForeColor=Color.White;
        StartPosition=FormStartPosition.Manual;Opacity=0;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        timer=new System.Windows.Forms.Timer{Interval=25};
        timer.Tick+=delegate{double value=timing.Opacity(Now());if(value<=0){timer.Stop();Hide();return;}Opacity=value;};
    }
    protected override bool ShowWithoutActivation{get{return true;}}
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=ExtendedStyles;return p;}}
    protected override void WndProc(ref Message message){
        if(message.Msg==0x21){message.Result=new IntPtr(3);return;} // MA_NOACTIVATE
        if(message.Msg==0x84){message.Result=new IntPtr(-1);return;} // HTTRANSPARENT
        base.WndProc(ref message);
    }
    public void Present(int id){
        page=id;Rectangle area=Screen.FromPoint(Cursor.Position).WorkingArea;
        Location=new Point(area.Left+(area.Width-Width)/2,area.Bottom-Height-28);
        timing.Start(Now());Opacity=0.01;Invalidate();
        Show();SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x0001|0x0002|0x0010|0x0040);timer.Start();
    }
    public void Dismiss(){timer.Stop();timing.Cancel();Hide();}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var font=new Font("Segoe UI",26,FontStyle.Bold))TextRenderer.DrawText(e.Graphics,page.ToString(),font,ClientRectangle,ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding);
    }
    private static long Now(){return (long)(Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency);}
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
}
