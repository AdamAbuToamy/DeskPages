using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Independent, no-activate, click-through HUD. Explorer HWND capture excludes
// this separate top-level window; it never becomes part of the page textures.
public sealed class DeskPagesIndicator:Form {
    private readonly System.Windows.Forms.Timer timer;
    private readonly DeskPagesIndicatorPolicy timing=new DeskPagesIndicatorPolicy();
    private int page,direction;
    private bool failed;
    private const int ExtendedStyles=0x08000000|0x00000020|0x00000080|0x00080000;
    public DeskPagesIndicator(){
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        ClientSize=new Size(128,78);BackColor=Color.FromArgb(29,33,41);ForeColor=Color.White;
        StartPosition=FormStartPosition.Manual;Opacity=0;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        timer=new System.Windows.Forms.Timer{Interval=16};
        timer.Tick+=delegate{double value=timing.Opacity(Now());if(value<=0){timer.Stop();Hide();return;}Opacity=value;if(timing.Pending)Invalidate();};
        UpdateShape();
    }
    public bool Pending{get{return timing.Pending;}}
    protected override bool ShowWithoutActivation{get{return true;}}
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=ExtendedStyles;return p;}}
    protected override void WndProc(ref Message message){
        if(message.Msg==0x21){message.Result=new IntPtr(3);return;}
        if(message.Msg==0x84){message.Result=new IntPtr(-1);return;}
        base.WndProc(ref message);
    }
    protected override void OnResize(EventArgs e){base.OnResize(e);UpdateShape();}
    private void UpdateShape(){
        if(Width<2 || Height<2)return;
        using(var path=Round(new Rectangle(0,0,Width,Height),Math.Max(8,Height/5))){var old=Region;Region=new Region(path);if(old!=null)old.Dispose();}
    }
    public void Begin(int id,int motion,Rectangle area){page=id;direction=Math.Sign(motion);failed=false;timing.Begin(Now());Place(area,true);}
    public void Complete(){timing.Complete(Now());Invalidate();}
    public void Fail(){if(!timing.Pending)return;failed=true;page=0;direction=0;timing.Fail(Now());Invalidate();}
    public void Present(int id){page=id;direction=0;failed=false;timing.Start(Now());Place(Screen.FromPoint(Cursor.Position).WorkingArea,false);}
    private void Place(Rectangle area,bool immediate){
        Location=new Point(area.Left+(area.Width-Width)/2,area.Bottom-Height-Math.Max(16,Height/3));
        Opacity=immediate?DeskPagesIndicatorPolicy.MaximumOpacity:0.01;Show();
        SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x0001|0x0002|0x0010|0x0040);
        Invalidate();Update();timer.Start();
    }
    public void Dismiss(){timer.Stop();timing.Cancel();Hide();}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        float sx=Width/128f,sy=Height/78f;g.ScaleTransform(sx,sy);
        using(var outline=Round(new Rectangle(1,1,126,76),15))using(var pen=new Pen(Color.FromArgb(90,106,126),1)){g.DrawPath(pen,outline);}
        using(var font=new Font("Segoe UI",25,FontStyle.Regular,GraphicsUnit.Pixel))using(var brush=new SolidBrush(failed?Color.FromArgb(255,200,155):ForeColor))using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center}){
            g.DrawString(failed?"!":page.ToString(),font,brush,new RectangleF(27,10,74,41),format);
        }
        if(direction!=0){float x=direction<0?19:109;using(var pen=new Pen(Color.FromArgb(211,220,234),1.8f)){pen.StartCap=pen.EndCap=LineCap.Round;g.DrawLine(pen,x-direction*3,26,x+direction*3,32);g.DrawLine(pen,x+direction*3,32,x-direction*3,38);}}
        using(var track=new Pen(Color.FromArgb(71,80,96),3)){track.StartCap=track.EndCap=LineCap.Round;g.DrawLine(track,20,61,108,61);}
        Color accent=failed?Color.FromArgb(255,190,135):Color.FromArgb(215,229,255);
        float start=20,end=108;
        if(timing.Pending){double p=timing.Progress(Now());double wave=0.5-0.5*Math.Cos(2*Math.PI*p);start=20+(float)(62*wave);end=start+26;}
        using(var line=new Pen(accent,3)){line.StartCap=line.EndCap=LineCap.Round;g.DrawLine(line,start,61,end,61);}
    }
    private static GraphicsPath Round(Rectangle r,int radius){var p=new GraphicsPath();int d=radius*2;p.AddArc(r.Left,r.Top,d,d,180,90);p.AddArc(r.Right-d,r.Top,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.Left,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    private static long Now(){return (long)(Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency);}
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
}

// A warm independent STA keeps painting while the agent is busy with Explorer,
// file verification or synchronous native capture. Only C# work runs here.
public sealed class DeskPagesIndicatorController:IDisposable {
    private readonly ConcurrentQueue<Action<DeskPagesIndicator>> jobs=new ConcurrentQueue<Action<DeskPagesIndicator>>();
    private readonly Thread worker;
    private DeskPagesIndicator view;
    private readonly Action<string> logger;
    private long generation,current;
    private int closed;
    public DeskPagesIndicatorController(Action<string> log=null){logger=log;worker=new Thread(Run);worker.IsBackground=true;worker.Name="DeskPages feedback";worker.SetApartmentState(ApartmentState.STA);worker.Start();}
    private void Run(){
        try{
            try{SetThreadDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){}
            using(var hud=new DeskPagesIndicator()){
                IntPtr handle=hud.Handle;Volatile.Write(ref view,hud);
                if(Volatile.Read(ref closed)!=0)return;
                Drain(hud);Application.Run();
            }
        }catch(Exception ex){Log("page feedback unavailable: "+ex.Message);}
    }
    private void Log(string message){try{if(logger!=null)logger(message);}catch{}}
    private void Drain(DeskPagesIndicator hud){Action<DeskPagesIndicator> job;while(jobs.TryDequeue(out job)){try{job(hud);}catch(Exception ex){Log("page feedback skipped: "+ex.Message);}}}
    private void Post(Action<DeskPagesIndicator> job){
        if(Volatile.Read(ref closed)!=0)return;jobs.Enqueue(job);
        var hud=Volatile.Read(ref view);if(hud==null)return;
        try{hud.BeginInvoke(new Action(delegate{Drain(hud);}));}catch(InvalidOperationException){}
    }
    public long Begin(int page,int direction,Rectangle area){long token=Interlocked.Increment(ref generation);Post(delegate(DeskPagesIndicator hud){current=token;hud.Begin(page,direction,area);});return token;}
    public void Complete(long token){Post(delegate(DeskPagesIndicator hud){if(current==token)hud.Complete();});}
    public void Fail(long token){Post(delegate(DeskPagesIndicator hud){if(current==token)hud.Fail();});}
    public void Present(int page){long token=Interlocked.Increment(ref generation);Post(delegate(DeskPagesIndicator hud){current=token;hud.Present(page);});}
    public void Dismiss(){long token=Interlocked.Increment(ref generation);Post(delegate(DeskPagesIndicator hud){current=token;hud.Dismiss();});}
    public void Dispose(){
        if(Interlocked.Exchange(ref closed,1)!=0)return;
        var hud=Volatile.Read(ref view);
        if(hud!=null){try{hud.BeginInvoke(new Action(delegate{Application.ExitThread();}));}catch(InvalidOperationException){}}
        worker.Join(1500);
    }
    [DllImport("user32.dll")]private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
