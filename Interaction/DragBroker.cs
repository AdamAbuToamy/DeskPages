using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly:AssemblyTitle("DeskPages drag interaction")]
[assembly:AssemblyProduct("DeskPages")]
[assembly:AssemblyCompany("Adam Abu Toamy")]
[assembly:AssemblyCopyright("Copyright 2026 Adam Abu Toamy")]
[assembly:AssemblyVersion("0.8.20.0")]
[assembly:AssemblyFileVersion("0.8.20.0")]

// Runs with Explorer's normal user token. An elevated process cannot accept
// Explorer OLE drops across UIPI; this process never changes files or attributes.
internal sealed class DragBroker:ApplicationContext {
    private readonly string folder;
    private readonly ShellDragImage dragImageApi=new ShellDragImage();
    private readonly DeskPagesDragImageSession dragImages;
    private int ownerProcessId;
    private volatile bool cancelRequested;
    private readonly DeskPagesDragInputState inputState=new DeskPagesDragInputState();
    private Thread inputThread;private uint inputThreadId;
    private readonly System.Windows.Forms.Timer timer;
    private readonly Surface left,right,canvas;
    private Surface activeSurface;
    private readonly Hook mouseHook,keyHook;
    private IntPtr mouseHandle,keyHandle;
    private readonly EdgeCue edgeCue=new EdgeCue();
    private readonly DeskPagesEdgeDragPolicy edges=new DeskPagesEdgeDragPolicy();
    private bool candidate,approved,enabled,overCanvas,cancelNext,acceptableData;
    private Point down;
    private string[] files;
    private DeskPagesDragRequest pending;
    private string pendingPacket,cueNavigationId;
    private long pendingAt,releaseAt=-1,started=Now();private bool pendingSlow;
    private Point? dropped;
    private Rectangle area;
    private bool closed,primaryOnly;
    private Rectangle[] displayAreas=new Rectangle[0];
    private int EdgeDirection(Point p){return DeskPagesMonitorPolicy.EdgeDirection(p.X,p.Y,area,displayAreas,edges.ZoneWidth,primaryOnly,Screen.FromRectangle(area).Primary);}
    [STAThread]private static int Main(string[] args){
        try{
            if(args.Length!=1)return 2;
            if(new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))return 3;
            try{SetThreadDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){}
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new DragBroker(args[0]));return 0;
        }catch{return 1;}
    }
    private DragBroker(string token){
        folder=DeskPagesDragProtocol.SessionPath(token);
        dragImages=new DeskPagesDragImageSession(dragImageApi,Trace);
        left=new Surface(this);right=new Surface(this);canvas=new Surface(this);
        mouseHook=OnMouse;keyHook=OnKey;
        StartInputHooks();
        timer=new System.Windows.Forms.Timer{Interval=40};timer.Tick+=delegate{Tick();};timer.Start();Trace("drag broker ready; version=0.8.20");
    }
    private long preferenceRead;
    private void Tick(){
        try{
            if(Now()-preferenceRead>2000 && !candidate && !approved){
                preferenceRead=Now();string[] settings;
                string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPages","HiddenPages","interaction.txt");
                if(DeskPagesDragProtocol.TryReadLines(path,out settings) && (settings.Length==3 || settings.Length==4) && settings[0]=="DeskPagesInteraction1"){
                    int width,hold;if(int.TryParse(settings[1],out width) && int.TryParse(settings[2],out hold) && width>=16 && width<=120 && hold>=150 && hold<=1500){edges.ZoneWidth=width;edges.HoldMilliseconds=hold;if(settings.Length==4)bool.TryParse(settings[3],out primaryOnly);}
                }
            }
            if(cancelRequested || (GetAsyncKeyState(0x1B)&0x8000)!=0){cancelRequested=false;Cancel("Escape");}
            string state=Path.Combine(folder,"state.txt");
            if(!File.Exists(state)){if(Now()-started>10000)ExitThread();return;}
            string[] lines;if(!DeskPagesDragProtocol.TryReadLines(state,out lines))return;long at;
            if(lines.Length!=5 || lines[0]!="DeskPagesDragState1" || !long.TryParse(lines[4],out at) || !OwnerAlive(at)){ExitThread();return;}
            int count;enabled=lines[2]=="True" && int.TryParse(lines[3],out count) && count>1;
            if(!enabled){Cancel("disabled or fewer than two pages");return;}
            int pressX,pressY;Point cursor=Cursor.Position;
            bool mouseDown=(GetAsyncKeyState(1)&0x8000)!=0;
            if(inputState.Observe(mouseDown,cursor.X,cursor.Y,out pressX,out pressY) && !candidate && !approved && pending==null){
                Point press=new Point(pressX,pressY);IntPtr hit=WindowFromPoint(press);
                string cls=ClassName(hit),root=ClassName(GetAncestor(hit,2));
                if((cls=="SysListView32" || cls=="SHELLDLL_DefView") && (root=="Progman" || root=="WorkerW")){Reset();candidate=true;down=press;}
            }
            if(cueNavigationId!=null){
                string[] visual;
                if(DeskPagesDragProtocol.TryReadLines(Path.Combine(folder,"visual-state.txt"),out visual) && DeskPagesDragProtocol.NavigationSettled(visual,cueNavigationId)){edgeCue.Hide();cueNavigationId=null;}
            }
            if(pending!=null){
                // A slow agent may already have committed the command. Keep its ID
                // and await the exact acknowledgement, then cancel safely. Exiting
                // here used to permanently disable edge dragging for this session.
                if(!pendingSlow && Now()-pendingAt>8000){pendingSlow=true;Trace("drag reply delayed: "+pending.Kind+"; waiting without replay");Cancel("delayed agent reply");}
                if(pendingPacket!=null){if(!DeskPagesDragProtocol.TryAtomicText(Path.Combine(folder,"request.txt"),pendingPacket))return;pendingPacket=null;}
                string reply=Path.Combine(folder,"response.txt");
                if(File.Exists(reply)){
                    string[] response;if(!DeskPagesDragProtocol.TryReadLines(reply,out response))return;
                    if(response.Length==3 && response[0]==pending.Id){
                        string kind=pending.Kind;bool ok=response[1]=="OK";Trace("request completed: "+kind+"; status="+response[1]);pending=null;pendingPacket=null;
                        if(!ok){Cancel("agent rejected "+kind);return;}
                        if(kind=="Arm"){approved=true;}
                        if(kind=="Drop" || kind=="Cancel"){Reset();return;}
                        if(cancelNext){cancelNext=false;Send("Cancel",0,0,null);return;}
                        if(dropped.HasValue){Point point=dropped.Value;dropped=null;Send("Drop",point.X,point.Y,null);return;}
                    }
                }
            }
            if(!mouseDown){
                if(candidate || approved){if(releaseAt<0)releaseAt=Now();if(Now()-releaseAt>350 && pending==null && !dropped.HasValue)Cancel("mouse released outside accepted drop");}
                return;
            }
            releaseAt=-1;
            if(candidate && !left.Visible && !canvas.Visible && Math.Abs(Cursor.Position.X-down.X)+Math.Abs(Cursor.Position.Y-down.Y)>SystemInformation.DragSize.Width){
                var display=Screen.FromPoint(down);if(primaryOnly && !display.Primary)return;
                area=display.WorkingArea;var areas=new List<Rectangle>();foreach(var screen in Screen.AllScreens)areas.Add(screen.WorkingArea);displayAreas=areas.ToArray();
                if(!DeskPagesMonitorPolicy.InternalEdge(area,displayAreas,true))left.Place(new Rectangle(area.Left,area.Top,edges.ZoneWidth,area.Height));
                if(!DeskPagesMonitorPolicy.InternalEdge(area,displayAreas,false))right.Place(new Rectangle(area.Right-edges.ZoneWidth,area.Top,edges.ZoneWidth,area.Height));
            }
            if(acceptableData && activeSurface!=null && WindowFromPoint(Cursor.Position)==activeSurface.Handle){
                int near=EdgeDirection(Cursor.Position);
                if(cueNavigationId==null){if(approved && pending==null && near!=0 && !edges.IsLatched)edgeCue.Present(area,near);else edgeCue.Hide();}
                int direction=edges.Observe(Now(),Cursor.Position.X,area.Left,area.Right,near!=0,approved && pending==null && edgeCue.Visible && Now()-edgeCue.PresentedAt>=80);
                if(direction!=0){
                    // Own the temporary desktop-only drop surface before hiding edge targets.
                    canvas.Place(area);left.Hide();right.Hide();Send("Navigate",direction,0,null);cueNavigationId=pending.Id;
                }
            }else{if(cueNavigationId==null)edgeCue.Hide();edges.Observe(Now(),Cursor.Position.X,area.Left,area.Right,false);}
        }catch(IOException){/* Sharing contention is retried on the next tick, not a physical drag cancellation. */}catch(UnauthorizedAccessException){Cancel("access denied");}catch(ArgumentException){Cancel("invalid drag state");}
    }
    private bool OwnerAlive(long heartbeat){
        try{string[] owner;if(!DeskPagesDragProtocol.TryReadLines(Path.Combine(folder,"owner.txt"),out owner))throw new IOException("Drag owner state temporarily unavailable");int pid;long startedAt;
            if(owner.Length!=2 || !int.TryParse(owner[0],out pid) || !long.TryParse(owner[1],out startedAt))return false;
            ownerProcessId=pid;
            using(var process=Process.GetProcessById(pid))return !process.HasExited && process.StartTime.ToUniversalTime().Ticks==startedAt;
        }catch(ArgumentException){return false;}catch(System.ComponentModel.Win32Exception){return DateTime.UtcNow.Ticks-heartbeat<TimeSpan.TicksPerMinute*2;}
    }
    private void Enter(Surface surface,DragEventArgs e){
        acceptableData=false;e.Effect=DragDropEffects.None;if(!candidate || !enabled)return;
        try{
            if(!e.Data.GetDataPresent(DataFormats.FileDrop))return;
            string[] paths=e.Data.GetData(DataFormats.FileDrop) as string[];
            if(paths==null || paths.Length<1 || paths.Length>256)return;
            if(files==null){files=(string[])paths.Clone();if(pending==null)Send("Arm",down.X,down.Y,files);}
            else if(!Same(paths,files))return;
            acceptableData=true;activeSurface=surface;overCanvas=surface==canvas;
            if(approved)e.Effect=(e.AllowedEffect&DragDropEffects.Move)!=0?DragDropEffects.Move:DragDropEffects.None;
        }catch{e.Effect=DragDropEffects.None;}
    }
    private void Over(Surface surface,DragEventArgs e){
        e.Effect=approved && acceptableData && (e.KeyState&1)!=0 && (e.AllowedEffect&DragDropEffects.Move)!=0?DragDropEffects.Move:DragDropEffects.None;
        activeSurface=surface;overCanvas=surface==canvas;
    }
    private void Drop(Surface surface,DragEventArgs e){
        // NEVER return MOVE from a completed drop: the Explorer source must not
        // delete/move anything. Only the elevated agent changes page membership.
        e.Effect=DragDropEffects.None;
        if(!approved || !acceptableData || surface!=canvas || !overCanvas){Cancel("drop outside approved canvas");return;}
        if(cueNavigationId==null)edgeCue.Hide();
        Trace("physical drop accepted by desktop canvas; awaiting membership commit");
        Point point=new Point(e.X,e.Y);left.Hide();right.Hide();canvas.Hide();
        candidate=false;
        if(pending==null)Send("Drop",point.X,point.Y,null);else dropped=point;
    }
    private static bool Same(string[] a,string[] b){
        if(a.Length!=b.Length)return false;var set=new HashSet<string>(a,StringComparer.OrdinalIgnoreCase);if(set.Count!=a.Length)return false;foreach(string path in b)if(!set.Contains(path))return false;return true;
    }
    private void Send(string kind,int x,int y,string[] paths){
        if(pending!=null)return;
        Trace("request sent: "+kind);
        pending=new DeskPagesDragRequest{Id=Guid.NewGuid().ToString("N"),Kind=kind,X=x,Y=y,Paths=paths};pendingAt=Now();pendingSlow=false;
        pendingPacket=DeskPagesDragProtocol.Encode(pending);
        if(DeskPagesDragProtocol.TryAtomicText(Path.Combine(folder,"request.txt"),pendingPacket))pendingPacket=null;
    }
    private void Cancel(string reason){
        edgeCue.Hide();cueNavigationId=null;
        if(candidate || approved || pending!=null || files!=null)Trace("drag cancelled: "+reason);
        dragImages.Cancel();left.Hide();right.Hide();canvas.Hide();candidate=false;edges.Reset();dropped=null;
        if(approved || files!=null){if(pending==null){Send("Cancel",0,0,null);}else cancelNext=true;}
        approved=false;files=null;releaseAt=-1;
    }
    private void Reset(){edgeCue.Hide();cueNavigationId=null;dragImages.Cancel();left.Hide();right.Hide();canvas.Hide();candidate=false;approved=false;files=null;acceptableData=false;activeSurface=null;overCanvas=false;dropped=null;cancelNext=false;releaseAt=-1;edges.Reset();}
    private IntPtr OnMouse(int code,IntPtr msg,IntPtr data){
        if(code>=0 && msg.ToInt32()==0x201){var value=(MouseData)Marshal.PtrToStructure(data,typeof(MouseData));inputState.Press(value.Point.X,value.Point.Y);}
        return CallNextHookEx(mouseHandle,code,msg,data);
    }
    private IntPtr OnKey(int code,IntPtr msg,IntPtr data){
        if(code>=0 && (msg.ToInt32()==0x100 || msg.ToInt32()==0x104) && Marshal.ReadInt32(data)==0x1B)cancelRequested=true;
        return CallNextHookEx(keyHandle,code,msg,data);
    }
    private void StartInputHooks(){
        var ready=new TaskCompletionSource<Exception>();
        inputThread=new Thread(delegate(){
            try{
                // Create the message queue before publishing the thread ID.
                NativeMessage message;PeekMessageW(out message,IntPtr.Zero,0,0,0);
                inputThreadId=GetCurrentThreadId();
                mouseHandle=SetWindowsHookExW(14,mouseHook,GetModuleHandleW(null),0);keyHandle=SetWindowsHookExW(13,keyHook,GetModuleHandleW(null),0);
                if(mouseHandle==IntPtr.Zero || keyHandle==IntPtr.Zero)throw new InvalidOperationException("Drag input unavailable");
                ready.TrySetResult(null);Application.Run();
            }catch(Exception ex){ready.TrySetResult(ex);Trace("drag input hook thread stopped; polling remains active: "+ex.Message);}
            finally{if(mouseHandle!=IntPtr.Zero)UnhookWindowsHookEx(mouseHandle);if(keyHandle!=IntPtr.Zero)UnhookWindowsHookEx(keyHandle);}
        });
        inputThread.IsBackground=true;inputThread.Name="DeskPages drag input";inputThread.SetApartmentState(ApartmentState.STA);inputThread.Start();
        if(!ready.Task.Wait(3000)){if(inputThreadId!=0)PostThreadMessageW(inputThreadId,0x12,UIntPtr.Zero,IntPtr.Zero);throw new InvalidOperationException("Drag input startup timed out");}
        if(ready.Task.Result!=null)throw new InvalidOperationException("Drag input unavailable",ready.Task.Result);
    }
    protected override void ExitThreadCore(){
        if(closed)return;closed=true;timer.Stop();timer.Dispose();if(inputThreadId!=0)PostThreadMessageW(inputThreadId,0x12,UIntPtr.Zero,IntPtr.Zero);
        dragImages.Cancel();dragImageApi.Dispose();edgeCue.Dispose();left.Dispose();right.Dispose();canvas.Dispose();base.ExitThreadCore();
    }
    private sealed class Surface:Form {
        private readonly DragBroker owner;
        public Surface(DragBroker broker){
            owner=broker;AllowDrop=true;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Opacity=0.015;BackColor=Color.Black;
            DragLeave+=delegate{owner.dragImages.Leave(Handle);if(owner.activeSurface==this){owner.activeSurface=null;owner.acceptableData=false;}};
            DragEnter+=delegate(object s,DragEventArgs e){owner.Enter(this,e);owner.dragImages.Enter(Handle,e.Data,e.X,e.Y,(uint)e.Effect);};DragOver+=delegate(object s,DragEventArgs e){owner.Over(this,e);owner.dragImages.Over(Handle,e.X,e.Y,(uint)e.Effect);};DragDrop+=delegate(object s,DragEventArgs e){owner.dragImages.Drop(Handle,e.Data,e.X,e.Y);owner.Drop(this,e);};
        }
        protected override bool ShowWithoutActivation{get{return true;}}
        protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000080;return p;}}
        protected override void WndProc(ref Message m){if(m.Msg==0x21){m.Result=new IntPtr(3);return;}base.WndProc(ref m);}
        public void Place(Rectangle bounds){
            IntPtr desktop=Desktop();if(desktop==IntPtr.Zero)return;
            if(owner.ownerProcessId<=0 || !SetPropW(Handle,"DeskPages.DragOwnerPid",new IntPtr(owner.ownerProcessId)))throw new InvalidOperationException("Cannot identify desktop drag surface");
            Bounds=bounds;Show();IntPtr after=GetWindow(desktop,3);if(after==Handle)after=GetWindow(Handle,3);
            SetWindowPos(Handle,after,bounds.X,bounds.Y,bounds.Width,bounds.Height,0x0010|0x0040);
        }
    }
    private sealed class EdgeCue:Form {
        private int edgeDirection;
        public long PresentedAt{get;private set;}
        public EdgeCue(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;BackColor=Color.FromArgb(233,240,250);Opacity=0.80;Enabled=false;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
        protected override bool ShowWithoutActivation{get{return true;}}
        protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x080000A0;return p;}}
        protected override void WndProc(ref Message m){if(m.Msg==0x84){m.Result=new IntPtr(-1);return;}if(m.Msg==0x21){m.Result=new IntPtr(3);return;}base.WndProc(ref m);}
        public void Present(Rectangle area,int direction){
            int width=Math.Max(16,(int)Math.Round(18.0*DeviceDpi/96));
            edgeDirection=direction;
            var bounds=new Rectangle(direction<0?area.Left:area.Right-width,area.Top,width,area.Height);
            if(Visible && Bounds==bounds)return;
            IntPtr desktop=Desktop();if(desktop==IntPtr.Zero)return;
            PresentedAt=Now();Bounds=bounds;Show();IntPtr after=GetWindow(desktop,3);if(after==Handle)after=GetWindow(Handle,3);
            SetWindowPos(Handle,after,bounds.X,bounds.Y,bounds.Width,bounds.Height,0x0010|0x0040);
        }
        protected override void OnPaint(PaintEventArgs e){
            base.OnPaint(e);if(Width<2 || Height<2)return;
            using(var fill=new LinearGradientBrush(ClientRectangle,Color.White,Color.FromArgb(213,226,246),LinearGradientMode.Horizontal)){
                if(edgeDirection>0)fill.LinearColors=new[]{Color.FromArgb(213,226,246),Color.White};
                e.Graphics.FillRectangle(fill,ClientRectangle);
            }
            int core=Math.Max(3,Width/4);
            using(var white=new SolidBrush(Color.White))e.Graphics.FillRectangle(white,edgeDirection<0?0:Width-core,0,core,Height);
            using(var border=new Pen(Color.FromArgb(148,166,190),1))e.Graphics.DrawLine(border,edgeDirection<0?Width-1:0,0,edgeDirection<0?Width-1:0,Height);
        }
    }
    private static IntPtr Desktop(){IntPtr found=IntPtr.Zero;EnumWindows(delegate(IntPtr h,IntPtr p){if(FindWindowExW(h,IntPtr.Zero,"SHELLDLL_DefView",null)==IntPtr.Zero)return true;found=h;return false;},IntPtr.Zero);return found;}
    private void Trace(string message){try{File.AppendAllText(Path.Combine(folder,"drag-visual.log"),DateTime.UtcNow.ToString("o")+" "+message+Environment.NewLine);}catch(IOException){}catch(UnauthorizedAccessException){}}
    private static string ClassName(IntPtr h){var s=new System.Text.StringBuilder(128);GetClassNameW(h,s,s.Capacity);return s.ToString();}
    private static long Now(){return (long)(Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency);}
    [StructLayout(LayoutKind.Sequential)]private struct MouseData{public Point Point;public uint Data,Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Sequential)]private struct NativeMessage{public IntPtr Hwnd;public uint Message;public UIntPtr WParam;public IntPtr LParam;public uint Time;public Point Point;public uint Private;}
    private delegate IntPtr Hook(int code,IntPtr msg,IntPtr data);
    private delegate bool EnumCallback(IntPtr h,IntPtr p);
    [DllImport("user32.dll")]private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]private static extern IntPtr SetWindowsHookExW(int id,Hook hook,IntPtr module,uint thread);
    [DllImport("user32.dll")]private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]private static extern IntPtr CallNextHookEx(IntPtr h,int c,IntPtr m,IntPtr d);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr GetModuleHandleW(string name);
    [DllImport("kernel32.dll")]private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]private static extern bool PeekMessageW(out NativeMessage message,IntPtr hwnd,uint first,uint last,uint remove);
    [DllImport("user32.dll")]private static extern bool PostThreadMessageW(uint thread,uint message,UIntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")]private static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")]private static extern IntPtr GetAncestor(IntPtr h,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetClassNameW(IntPtr h,System.Text.StringBuilder s,int size);
    [DllImport("user32.dll")]private static extern bool EnumWindows(EnumCallback c,IntPtr p);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr FindWindowExW(IntPtr p,IntPtr a,string c,string t);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool SetPropW(IntPtr hwnd,string name,IntPtr value);
    [DllImport("user32.dll")]private static extern IntPtr GetWindow(IntPtr h,uint command);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int height,uint f);
}
