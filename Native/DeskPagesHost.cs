using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// No desktop replacement. Only a notification icon, a hidden input receiver,
// and an optional short-lived transition surface exist.
public sealed class DeskPagesHost : ApplicationContext
{
    private static DeskPagesHost active;
    private readonly Action<int> dispatch;
    private sealed class HostCommand {public int Code;public bool ExplicitUi;}
    private readonly ConcurrentQueue<HostCommand> commands = new ConcurrentQueue<HostCommand>();
    private bool explicitUiCommand;
    private readonly NotifyIcon tray;
    private DeskPagesUpdates updates;
    private readonly ContextMenuStrip menu;
    private readonly System.Windows.Forms.Timer timer;
    private readonly Thread inputThread;
    private InputPump input;
    private bool busy;
    private DeskPagesSnapshotSession snapshots;
    private sealed class ContextClick {public int X,Y;public long At;}
    private ContextClick contextClick;
    private bool placementWatch;
    private long nextPlacementPoll;
    public static int[] TakeDesktopContextClick(){
        if(active==null)return null;
        var click=Interlocked.Exchange(ref active.contextClick,null);
        if(click==null || ElapsedMs()-click.At>1500)return null;
        return new int[]{click.X,click.Y};
    }
    public static bool CreationInputBusy {get{return (GetAsyncKeyState(1)&0x8000)!=0 || (GetAsyncKeyState(2)&0x8000)!=0;}}
    public static void WatchNewItem(bool enabled){if(active!=null)active.placementWatch=enabled;}

    private volatile bool swipe = true, reverse, winArrows = true;
    private bool animate=true;
    private bool showPageIndicator=true;
    private long wheelMessages, rawMessages, rawNonZero, desktopPackets, blockedPackets, recognizedRaw;
    private int lastDelta;
    private string lastHit="none";
    private string touchpadStatus="not initialized";
    private volatile string hidStatus="HID input not initialized";
    private long hidBlockedFrames;
    private string inputError = "";
    private string animationStatus = "native DirectComposition; awaiting first transition";

    private int slideDirection;
    private bool slideRequested;
    private bool targetConfirmed;
    private IntPtr transitionForeground;
    private bool wasTransitionVisible;
    private static bool enableAnimationOnLaunch;
    public static void EnableAnimationOnLaunch(){enableAnimationOnLaunch=true;}
    public static void SetStartupToken(string token){startupToken=token;}
    public static void CheckpointSaved(){if(active!=null){active.checkpointAcknowledged=true;active.checkpoints.Completed(active.checkpointGeneration,true,ElapsedMs());}}
    private bool exitRequested;
    private int recoveryTicks;
    private int autosaveTicks;
    private readonly DeskPagesCheckpointPolicy checkpoints=new DeskPagesCheckpointPolicy();
    private SessionWindow sessionWindow;
    private readonly DeskPagesSessionPolicy session=new DeskPagesSessionPolicy();
    private static string startupToken;
    private static string installReadyToken;
    public static void SetInstallReadyToken(string token){
        if(!System.Text.RegularExpressions.Regex.IsMatch(token,"^[a-f0-9]{32}$"))throw new ArgumentException("Invalid readiness token");
        installReadyToken=token;
    }
    private long checkpointGeneration;
    private bool checkpointAcknowledged;
    private bool readinessLogged;
    private IntPtr lastDesktopRoot;
    private readonly ToolStripMenuItem pagesMenu;
    private DeskPagesPanel panel;
    private DeskPagesIndicatorController indicator;
    private readonly DeskPagesFeedbackLifecycle feedback=new DeskPagesFeedbackLifecycle();
    private DeskPagesPageNames pageNames;
    private int[] knownPages=new int[0];
    private int currentPage;
    private string panelSnapshot="";
    private static string dragSessionToken;
    private DeskPagesDragMailbox dragMailbox;
    private bool edgeDrag=true;
    public static void ConfigureDragSession(string token){dragSessionToken=DeskPagesDragStartupPolicy.NormalizeToken(token);}
    public static DeskPagesDragRequest DragRequest{get{return active==null || active.dragMailbox==null?null:active.dragMailbox.Pending;}}
    public static void CompleteDragRequest(bool success,int page){if(active!=null && active.dragMailbox!=null)active.dragMailbox.Complete(success,page);}
    public static void BeginContentUpdate(){DeskPagesAnimation.SnapshotSourceKey="";DeskPagesAnimation.SnapshotTargetKey="";if(active!=null && active.snapshots!=null)active.snapshots.Invalidate();BeginAnimation(1);if(active!=null)active.slideRequested=false;}

    private static string applicationIconPath;
    private readonly Icon applicationIcon;

    public DeskPagesHost(Action<int> action)
    {
        try {SetThreadDpiAwarenessContext(new IntPtr(-4));} catch(EntryPointNotFoundException) {}
        active=this; dispatch=action;
        SetProcessShutdownParameters(0x3ff,1);
        sessionWindow=new SessionWindow(this);
        LoadPreferences();
        try{indicator=new DeskPagesIndicatorController(DeskPagesStartupGate.Log);}catch(Exception ex){DeskPagesStartupGate.Log("page feedback unavailable: "+ex.Message);}
        if(DeskPagesWorkspaceApi.Input!=null)ApplyWorkspaceInput(DeskPagesWorkspaceApi.Input);
        if(dragSessionToken!=null){
            try{dragMailbox=new DeskPagesDragMailbox(dragSessionToken);}
            catch(Exception ex){dragMailbox=null;edgeDrag=false;DeskPagesStartupGate.Log("drag helper unavailable; core agent will continue: "+ex.Message);}
        }
        if(enableAnimationOnLaunch){animate=true;SavePreferences();}
        DeskPagesAnimation.Initialize();
        snapshots=new DeskPagesSnapshotSession();
        pageNames=new DeskPagesPageNames(Path.Combine(Path.GetDirectoryName(PreferencesPath()),"page-names.txt"));
        menu=new ContextMenuStrip();
        var openEntry=menu.Items.Add("Open DeskPages",null,delegate{OpenPanel();});
        var settingsEntry=menu.Items.Add("Settings",null,delegate{DeskPagesWorkspaceApi.OpenSettings();});
        var common=new ToolStripMenuItem("Selected desktop icons");common.DropDownItems.Add("Show on every page",null,delegate{DeskPagesWorkspaceApi.PinSelected(true);});common.DropDownItems.Add("Show only on their page",null,delegate{DeskPagesWorkspaceApi.PinSelected(false);});menu.Items.Add(common);
        pagesMenu=new ToolStripMenuItem("Pages");menu.Items.Add(pagesMenu);
        var previousEntry=menu.Items.Add("Previous page",null,delegate{Enqueue(-1);});
        var nextEntry=menu.Items.Add("Next page",null,delegate{Enqueue(1);});
        menu.Items.Add(new ToolStripSeparator());
        var exitEntry=menu.Items.Add("Exit and show all files",null,delegate{Enqueue(9002);});
        menu.Opening+=delegate{openEntry.Text=DeskPagesWorkspaceApi.L("Pages","الصفحات","דפים");settingsEntry.Text=DeskPagesWorkspaceApi.L("Settings","الإعدادات","הגדרות");pagesMenu.Text=DeskPagesWorkspaceApi.L("Switch page","تغيير الصفحة","מעבר דף");previousEntry.Text=DeskPagesWorkspaceApi.L("Previous page","الصفحة السابقة","הדף הקודם");nextEntry.Text=DeskPagesWorkspaceApi.L("Next page","الصفحة التالية","הדף הבא");exitEntry.Text=DeskPagesWorkspaceApi.L("Exit and show all files","خروج وإظهار كل الملفات","יציאה והצגת כל הקבצים");common.Text=DeskPagesWorkspaceApi.L("Selected desktop icons","أيقونات السطح المحددة","סמלים נבחרים בשולחן העבודה");common.DropDownItems[0].Text=DeskPagesWorkspaceApi.L("Show on every page","إظهار بكل الصفحات","הצגה בכל הדפים");common.DropDownItems[1].Text=DeskPagesWorkspaceApi.L("Show only on their page","إظهار في صفحتها فقط","הצגה בדף המקורי בלבד");};
        try {applicationIcon=new Icon(applicationIconPath,SystemInformation.SmallIconSize);}
        catch {applicationIcon=(Icon)SystemIcons.Application.Clone();}
        tray=new NotifyIcon {Icon=applicationIcon, ContextMenuStrip=menu,
            Text="DeskPages", Visible=true};
        updates=new DeskPagesUpdates(sessionWindow,delegate(DeskPagesUpdateRelease release){
            tray.BalloonTipTitle="DeskPages "+release.Version;tray.BalloonTipText=DeskPagesWorkspaceApi.L("An update is available. Click to review and download.","يتوفر تحديث جديد. اضغط لعرضه وتحميله.","עדכון זמין. לחצו לפרטים ולהורדה.");tray.ShowBalloonTip(10000);
        });
        DeskPagesWorkspaceApi.CheckUpdatesAction=updates.Show;
        tray.BalloonTipClicked+=delegate{updates.Show();};
        var updateEntry=new ToolStripMenuItem(DeskPagesWorkspaceApi.L("Check for updates","فحص التحديثات","בדיקת עדכונים"),null,delegate{updates.Show();});menu.Items.Insert(2,updateEntry);
        menu.Opening+=delegate{updateEntry.Text=updates.Available==null?DeskPagesWorkspaceApi.L("Check for updates","فحص التحديثات","בדיקת עדכונים"):DeskPagesWorkspaceApi.L("Update available: ","يتوفر تحديث: ","עדכון זמין: ")+updates.Available.Version;};
        tray.MouseClick+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left)OpenPanel();};
        inputThread=new Thread(delegate() {
            try {
                try {SetThreadDpiAwarenessContext(new IntPtr(-4));} catch(EntryPointNotFoundException) {}
                input=new InputPump(this); Application.Run(input);
            }
            catch(Exception ex) {inputError=ex.Message;}
        });
        inputThread.IsBackground=true; inputThread.SetApartmentState(ApartmentState.STA); inputThread.Start();
        timer=new System.Windows.Forms.Timer {Interval=40};
        timer.Tick+=delegate {
            if(session.Ending)return;
            if(!readinessLogged){DeskPagesStartupGate.Log("Desktop host ready; event loop active.");readinessLogged=true;DeskPagesWorkspaceApi.MaybeWelcome();}
            if(startupToken!=null){
                try{DeskPagesStartupGate.SignalReady(startupToken);startupToken=null;}
                catch(Exception ex){DeskPagesStartupGate.Log("ready signal failed: "+ex.Message);}
            }
            if(installReadyToken!=null){
                string token=installReadyToken;
                string receipt=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPages","HiddenPages","install-ready-"+token+".txt");
                try{DeskPagesStartupGate.AtomicText(receipt,"DeskPagesInstallReady1\n"+token+"\nAnimation="+animate);installReadyToken=null;}
                catch(Exception ex){DeskPagesStartupGate.Log("installer readiness receipt failed: "+ex.Message);}
            }
            bool shown=DeskPagesAnimation.IsVisible;
            if(wasTransitionVisible && !shown)DeskPagesAnimation.Trace("renderer: "+DeskPagesAnimation.Metrics);
            wasTransitionVisible=shown;
            // Publish completion from the actual renderer lifetime, before the
            // visible-animation early return. Cue tracking never blocks OLE.
            if(dragMailbox!=null){try{dragMailbox.PublishVisualState(shown);}catch(IOException){}catch(UnauthorizedAccessException){}}
            long completedFeedback=feedback.TakeCompletion(shown);
            if(completedFeedback>0){
                try{if(indicator!=null)indicator.Complete(completedFeedback);}catch(Exception ex){DeskPagesStartupGate.Log("page feedback completion skipped: "+ex.Message);}

            }
            if(busy || DeskPagesAnimation.IsVisible) return;
            try{snapshots.Poll();}catch(Exception ex){DeskPagesStartupGate.Log("resource collector request: "+ex.Message);}
            if(dragMailbox!=null){
                try{if(dragMailbox.Poll(currentPage,edgeDrag,knownPages.Length)){
                    busy=true;explicitUiCommand=true;
                    try{dispatch(9600);}finally{busy=false;explicitUiCommand=false;}
                }}catch(Exception ex){DeskPagesStartupGate.Log("drag request rejected: "+ex.Message);dragMailbox.Complete(false,currentPage);}
            }
            if(DeskPagesAnimation.IsVisible)return;
            if(contextClick!=null || (placementWatch && ElapsedMs()>=nextPlacementPoll)){
                busy=true;
                try{dispatch(contextClick!=null?9500:9501);}
                catch(Exception ex){placementWatch=false;DeskPagesStartupGate.Log("new-item placement skipped: "+ex.Message);}
                finally{busy=false;nextPlacementPoll=ElapsedMs()+200;}
            }
            if(++autosaveTicks>=125) {
                autosaveTicks=0;
                IntPtr root=DesktopRoot();
                if(root!=IntPtr.Zero && lastDesktopRoot!=IntPtr.Zero && root!=lastDesktopRoot) Enqueue(9102);
                else if(commands.IsEmpty)RunCheckpoint();
                lastDesktopRoot=root;
            }
            if(commands.IsEmpty && checkpoints.Due(ElapsedMs()))RunCheckpoint();
            if(++recoveryTicks>=10) {
                recoveryTicks=0;
                string request=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPages","HiddenPages","recovery.request");
                try {if(File.Exists(request)) {File.Delete(request);Enqueue(9000);}} catch(IOException) {}
                request=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPages","HiddenPages","exit-save.request");
                try {if(File.Exists(request)) {File.Delete(request);Enqueue(9101);}} catch(IOException) {}
            }
            if(animate && !DeskPagesWorkspaceApi.ReduceMotion && !CreationInputBusy && !DeskPagesLayout.IsLabelEditing() && snapshots.ShouldWarm()){
                busy=true;try{dispatch(9750);}catch(Exception ex){DeskPagesStartupGate.Log("idle snapshot: "+ex.Message);}finally{busy=false;}
            }
            HostCommand command; if(!commands.TryDequeue(out command)) return;
            busy=true;explicitUiCommand=command.ExplicitUi;
            try {dispatch(command.Code);}
            catch(Exception ex) {AbortAnimation(); ShowError(ex.Message);}
            finally {busy=false;explicitUiCommand=false;}
            if(exitRequested) ExitThread();
        };
        Enqueue(9004); timer.Start();
    }
    private void OpenPanel(){if(DeskPagesWorkspaceApi.Request!=null){DeskPagesWorkspaceApi.Open();return;}OpenQuickPanel();}
    public static string[] WorkspaceCommand(Func<string[]> action){
        if(active==null)return action();
        bool previousBusy=active.busy,previousUi=active.explicitUiCommand;active.busy=true;active.explicitUiCommand=true;
        try{return action();}finally{active.busy=previousBusy;active.explicitUiCommand=previousUi;}
    }
    public static void ApplyWorkspaceInput(bool[] values){
        if(active==null || values==null || values.Length!=6)return;
        active.swipe=values[0];active.reverse=values[1];active.winArrows=values[2];active.animate=values[3];active.showPageIndicator=values[4];active.edgeDrag=values[5];
        if(!values[4]){active.feedback.Cancel();if(active.indicator!=null)active.indicator.Dismiss();}
        active.SavePreferences();
    }
    public static void InputSettings(){DeskPagesWorkspaceApi.OpenSettings();}
    private void OpenQuickPanel(){
        if(panel==null){
            panel=new DeskPagesPanel(EnqueuePanel,DeskPagesWorkspaceApi.Name,delegate(int id,string value){if(DeskPagesWorkspaceApi.Request!=null)DeskPagesWorkspaceApi.Request("rename",new[]{id.ToString(),value});else pageNames.Rename(id,value);UpdatePages(knownPages,currentPage);},
                new[]{swipe,reverse,winArrows,animate,showPageIndicator,edgeDrag},delegate(int setting,bool value){if(setting==0)swipe=value;else if(setting==1)reverse=value;else if(setting==2)winArrows=value;else if(setting==3)animate=value;else if(setting==4){showPageIndicator=value;if(!value){feedback.Cancel();if(indicator!=null)indicator.Dismiss();}}else edgeDrag=value;SavePreferences();},
                delegate(string file){try{Process.Start("notepad.exe","\""+Path.Combine(Path.GetDirectoryName(applicationIconPath),"..",file)+"\"");}catch(Exception ex){ShowError(ex.Message);}});
        }
        panel.SetPages(knownPages,currentPage);panel.OpenNearPointer();
    }
    private static long ElapsedMs() {return (long)(Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency);}
    private void RunCheckpoint(){
        if(busy || session.Ending || DeskPagesAnimation.IsVisible)return;
        checkpointGeneration=checkpoints.Generation;checkpointAcknowledged=false;busy=true;
        try{dispatch(9100);}catch(Exception ex){DeskPagesStartupGate.Log("checkpoint skipped: "+ex.Message);}
        finally{busy=false;if(!checkpointAcknowledged)checkpoints.Completed(checkpointGeneration,false,ElapsedMs());}
    }
    private void EndSession(bool ending,long flags){
        session.End(ending,flags,busy,delegate{checkpointGeneration=checkpoints.Generation;dispatch(9300);},
            DeskPagesStartupGate.ArmForNextLogon,delegate(string token){DeskPagesStartupGate.Release(token);},DeskPagesStartupGate.Log);
    }
    private sealed class SessionWindow:NativeWindow,IDisposable{
        private readonly DeskPagesHost owner;
        public SessionWindow(DeskPagesHost host){owner=host;CreateHandle(new CreateParams{Caption="DeskPages session checkpoint",ExStyle=0x80|0x08000000});}
        protected override void WndProc(ref Message m){
            if(m.Msg==0x7e || m.Msg==0x02e0){if(owner.snapshots!=null)owner.snapshots.Invalidate();owner.Enqueue(9701);}
            if(m.Msg==0x1a || m.Msg==0x31a || m.Msg==0x320 || m.Msg==0x218 || m.Msg==0x11){if(owner.snapshots!=null)owner.snapshots.Invalidate();}
            if(m.Msg==0x11){m.Result=owner.session.Query()?new IntPtr(1):IntPtr.Zero;return;}
            if(m.Msg==0x16){owner.EndSession(m.WParam!=IntPtr.Zero,m.LParam.ToInt64());m.Result=IntPtr.Zero;return;}
            base.WndProc(ref m);
        }
        public void Dispose(){DestroyHandle();}
    }
    private void Enqueue(int code) {QueueCommand(code,false);}
    private void EnqueuePanel(int code) {QueueCommand(code,true);}
    private void QueueCommand(int code,bool ui) {if(commands.Count<3) commands.Enqueue(new HostCommand{Code=code,ExplicitUi=ui});}
    private string PreferencesPath() {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPages","HiddenPages","input-settings.txt");}
    private void LoadPreferences()
    {
        try {
            string path=PreferencesPath();if(!File.Exists(path)) return;
            string[] lines=File.ReadAllLines(path);bool value;
            if(lines.Length>0 && bool.TryParse(lines[0],out value)) swipe=value;
            if(lines.Length>1 && bool.TryParse(lines[1],out value)) reverse=value;
            if(lines.Length>2 && bool.TryParse(lines[2],out value)) winArrows=value;
            if(lines.Length>3 && bool.TryParse(lines[3],out value)) animate=value;
            if(lines.Length>5 && bool.TryParse(lines[5],out value)) showPageIndicator=value;
            if(lines.Length>6 && bool.TryParse(lines[6],out value)) edgeDrag=value;
            // The former fifth preference controlled the removed input overlay.
        } catch(IOException) {}
    }
    private void SavePreferences()
    {
        try {
            string path=PreferencesPath();Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path+".tmp",new string[]{swipe.ToString(),reverse.ToString(),winArrows.ToString(),animate.ToString(),false.ToString(),showPageIndicator.ToString(),edgeDrag.ToString()});
            if(File.Exists(path)) File.Replace(path+".tmp",path,null); else File.Move(path+".tmp",path);
        } catch(IOException) {inputError="Could not save input preferences.";}
          catch(UnauthorizedAccessException) {inputError="Could not save input preferences.";}
    }
    public static void ConfigureRenderer(string baseDirectory) {
        DeskPagesAnimation.Configure(baseDirectory);
        applicationIconPath=Path.Combine(baseDirectory,"Assets","DeskPages.ico");
    }
    public static void ConfigureDpi() {try {SetThreadDpiAwarenessContext(new IntPtr(-4));} catch(EntryPointNotFoundException) {}}
    public static void Run(Action<int> action) {Application.Run(new DeskPagesHost(action));}
    public static void RequestExit() {if(active!=null) active.exitRequested=true;}
    public static void ShowError(string text) {MessageBox.Show(text,"DeskPages",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    public static void UpdatePages(int[] pages,int current)
    {
        if(active==null) return;
        if(active.knownPages.Length>0){foreach(int id in pages){if(Array.IndexOf(active.knownPages,id)<0){try{active.pageNames.Rename(id,id==1?"Main page":"Page "+id);}catch(Exception ex){DeskPagesStartupGate.Log("new page label reset failed: "+ex.Message);}}}}
        active.knownPages=(int[])pages.Clone();active.currentPage=current;if(active.snapshots!=null)active.snapshots.SetPage(current);
        var snapshot=new StringBuilder();snapshot.Append(current);
        foreach(int id in pages)snapshot.Append('|').Append(id).Append(':').Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(DeskPagesWorkspaceApi.Name(id))));
        string nextSnapshot=snapshot.ToString();
        if(active.panelSnapshot==nextSnapshot)return;
        active.panelSnapshot=nextSnapshot;
        if(active.panel!=null)active.panel.SetPages(pages,current);
        active.pagesMenu.DropDownItems.Clear();
        foreach(int id in pages) {
            int page=id;
            var item=new ToolStripMenuItem(DeskPagesWorkspaceApi.Name(page)); item.Checked=page==current;
            item.Click+=delegate {active.Enqueue(1000+page);};
            active.pagesMenu.DropDownItems.Add(item);
        }
        active.tray.Text="DeskPages | Page "+current+" | "+pages.Length+" pages";
    }
    public static string Diagnostics()
    {
        if(active==null) return "Host not running.";
        return "Build: 0.8.17\r\n"+active.hidStatus+
            "\r\nHID packets blocked by desktop/button/transition scope: "+Interlocked.Read(ref active.hidBlockedFrames)+
            "\r\nHorizontal hook events: "+Interlocked.Read(ref active.wheelMessages)+
            "\r\nHorizontal raw-input events: "+Interlocked.Read(ref active.rawMessages)+
            "\r\nNonzero raw deltas: "+Interlocked.Read(ref active.rawNonZero)+
            "\r\nDesktop packets: "+Interlocked.Read(ref active.desktopPackets)+
            "\r\nBlocked outside desktop: "+Interlocked.Read(ref active.blockedPackets)+
            "\r\nRaw gestures recognized: "+Interlocked.Read(ref active.recognizedRaw)+
            "\r\nLast raw delta: "+active.lastDelta+" | Hit: "+active.lastHit+
            "\r\nTouchpad route: "+active.touchpadStatus+
            "\r\nInput setup: "+(active.inputError.Length==0 ? "ready" : active.inputError)+
            "\r\nAnimation: "+active.animationStatus+" | "+DeskPagesAnimation.Metrics+
            "\r\nOS: "+Environment.OSVersion.Version+" | Input overlay: removed"+
            "\r\n\r\nSwipe only over the original desktop. If all counters stay zero,\r\n"+
            "this touchpad driver is not exposing horizontal input through these routes.\r\n"+
            "Keyboard fallback: Ctrl+Alt+Left/Right. Recovery: Ctrl+Alt+Home.";
    }
    protected override void ExitThreadCore()
    {
        DeskPagesWorkspaceApi.CheckUpdatesAction=null;if(updates!=null){updates.Dispose();updates=null;}
        DeskPagesWorkspaceApi.Close();
        if(snapshots!=null){snapshots.Dispose();snapshots=null;}
        AbortAnimation(); DeskPagesAnimation.Shutdown(); timer.Stop(); timer.Dispose();
        if(input!=null) input.Stop();
        inputThread.Join(1500);
        if(dragMailbox!=null){dragMailbox.Dispose();dragMailbox=null;}
        if(indicator!=null){indicator.Dispose();indicator=null;}
        if(panel!=null){panel.Dispose();panel=null;}
        tray.Visible=false; tray.Dispose();applicationIcon.Dispose(); menu.Dispose(); active=null;
        if(sessionWindow!=null){sessionWindow.Dispose();sessionWindow=null;}
        base.ExitThreadCore();
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point {public int X,Y;}
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("kernel32.dll")] private static extern bool SetProcessShutdownParameters(uint level,uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] private struct MouseData {public Point Position; public uint Data,Flags,Time; public UIntPtr Extra;}
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardData {public uint Key,Scan,Flags,Time; public UIntPtr Extra;}
    [StructLayout(LayoutKind.Sequential)] private struct RawDevice {public ushort Page,Usage; public uint Flags; public IntPtr Target;}
    private delegate IntPtr Hook(int code,IntPtr message,IntPtr data);
    private delegate bool EnumCallback(IntPtr hwnd,IntPtr parameter);
    [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr SetWindowsHookExW(int type,Hook callback,IntPtr module,uint thread);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string name);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassNameW(IntPtr hwnd,StringBuilder text,int length);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd,int id);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool RegisterRawInputDevices(RawDevice[] devices,uint count,uint size);
    [DllImport("user32.dll")] private static extern uint GetRawInputData(IntPtr data,uint command,IntPtr buffer,ref uint size,uint headerSize);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr FindWindowW(string cls,string title);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr FindWindowExW(IntPtr parent,IntPtr after,string cls,string title);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback,IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out Rect rectangle);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out int value,int size);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    private static string ClassName(IntPtr hwnd) {var s=new StringBuilder(128); GetClassNameW(hwnd,s,s.Capacity); return s.ToString();}
    private static bool DesktopWindow(IntPtr hwnd) {
        IntPtr root=GetAncestor(hwnd,2);
        string cls=ClassName(root);
        return cls=="Progman" || cls=="WorkerW" || (root!=IntPtr.Zero && root==DesktopRoot());
    }
    private static bool DesktopAt(Point point) {
        IntPtr hwnd=WindowFromPoint(point);
        if(DeskPagesAnimation.IsTransitionWindow(GetAncestor(hwnd,2)))return true;
        // Use the actual hit window/ancestor chain, not bounding rectangles of
        // transparent shell windows. There is no DeskPages input overlay now.
        for(int depth=0;hwnd!=IntPtr.Zero && depth<12;depth++) {
            string cls=ClassName(hwnd);
            if(cls=="SHELLDLL_DefView" || cls=="Progman" || cls=="WorkerW") return true;
            hwnd=GetParent(hwnd);
        }
        return DesktopWindow(WindowFromPoint(point));
    }

    private static bool DesktopSurfaceAt(Point point){
        IntPtr hit=WindowFromPoint(point),root=GetAncestor(hit,2);
        // GA_ROOT follows parents, not popup ownership. Never walk GetParent
        // through a popup menu/rename edit back into its Explorer owner.
        return DeskPagesSurfacePolicy.IsDesktopSurface(ClassName(hit),ClassName(root),root!=IntPtr.Zero && root==DesktopRoot());
    }

    private sealed class InputPump : ApplicationContext
    {
        private readonly DeskPagesHost owner;
        private readonly Receiver receiver;
        private readonly Hook mouseCallback,keyCallback;
        private IntPtr mouseHook,keyHook;
        private readonly DeskPagesGesture gestures=new DeskPagesGesture();
        private readonly DeskPagesTouchpad touchpad=new DeskPagesTouchpad();
        private Point dragStart;
        private bool dragging;
        private readonly HashSet<uint> swallowed=new HashSet<uint>();
        public InputPump(DeskPagesHost host)
        {
            owner=host; receiver=new Receiver(this);
            mouseCallback=OnMouse; keyCallback=OnKey;
            mouseHook=SetWindowsHookExW(14,mouseCallback,GetModuleHandleW(null),0);
            keyHook=SetWindowsHookExW(13,keyCallback,GetModuleHandleW(null),0);
            if(mouseHook==IntPtr.Zero || keyHook==IntPtr.Zero) owner.inputError="A global input hook could not be installed.";
            uint mods=0x4000|0x2|0x1;
            bool a=RegisterHotKey(receiver.Handle,1,mods,0x25);
            bool b=RegisterHotKey(receiver.Handle,2,mods,0x27);
            bool c=RegisterHotKey(receiver.Handle,3,mods,0x24);
            if(!a || !b || !c) owner.inputError+=" A fallback shortcut is already used by another app.";
            var device=new RawDevice {Page=1,Usage=2,Flags=0x100,Target=receiver.Handle};
            if(!RegisterRawInputDevices(new RawDevice[]{device},1,(uint)Marshal.SizeOf(typeof(RawDevice))))
                owner.inputError+=" Raw mouse input unavailable.";
            touchpad.Register(receiver.Handle);
            owner.hidStatus=touchpad.Diagnostics();
            owner.touchpadStatus="Precision Touchpad HID 0D/05; raw mouse wheel is a legacy fallback";
        }
        public void Stop() {PostMessageW(receiver.Handle,0x10,IntPtr.Zero,IntPtr.Zero);}
        private void Wheel(int delta,Point point,int source=0)
        {
            // Windows can expose HID contacts and wheel events for one movement.
            // Recent HID frames take priority so the same swipe cannot page twice.
            if(DeskPagesGesture.HidOwnsWheel(ElapsedMs()-touchpad.LastFrameMs,touchpad.Contacts,ElapsedMs()-touchpad.LastGestureMs)) {gestures.Suppress(ElapsedMs());return;}
            // Keep consuming the current gesture while the temporary transition
            // covers Explorer. Do not reset the latch and retrigger on its inertia.
            if((GetAsyncKeyState(1)&0x8000)!=0 || (GetAsyncKeyState(2)&0x8000)!=0 || (GetAsyncKeyState(4)&0x8000)!=0) return;
            bool desktop=DesktopAt(point);
            IntPtr hit=WindowFromPoint(point);
            owner.lastHit=ClassName(hit)+" / "+ClassName(GetAncestor(hit,2));
            if(desktop) Interlocked.Increment(ref owner.desktopPackets);else Interlocked.Increment(ref owner.blockedPackets);
            if(!owner.swipe || !desktop) {gestures.Cancel();return;}
            gestures.WheelThreshold=DeskPagesWorkspaceApi.WheelThreshold;
            int direction=gestures.Wheel(delta,ElapsedMs(),source);
            if(direction!=0) {Interlocked.Increment(ref owner.recognizedRaw);owner.Enqueue(owner.reverse?-direction:direction);}
        }
        private long lastContextAt;
        private void RememberContext(Point point){
            if(DeskPagesAnimation.IsVisible || !DesktopSurfaceAt(point))return;
            long now=ElapsedMs();
            // Low-level and Raw Input may deliver the same physical click.
            if(now-lastContextAt<250)return;
            lastContextAt=now;
            Interlocked.Exchange(ref owner.contextClick,new ContextClick{X=point.X,Y=point.Y,At=now});
        }
        private IntPtr OnMouse(int code,IntPtr message,IntPtr data)
        {
            if(code>=0) {
                int msg=message.ToInt32();
                var md=(MouseData)Marshal.PtrToStructure(data,typeof(MouseData));
                if(msg==0x202 && owner.snapshots!=null && DesktopAt(md.Position))owner.snapshots.RequestLayoutRefresh();
                // Capture BEFORE Explorer opens the menu. Precision touchpad
                // drivers may synthesize a right click; do not discard it here.
                if(msg==0x204)RememberContext(md.Position);
                if((md.Flags&1)==0) {
                    bool onDesktop=DesktopAt(md.Position);
                    if(msg==0x201 && onDesktop) {dragStart=md.Position;dragging=true;}
                    if(msg==0x202 && dragging) {
                        dragging=false;
                        if(Math.Abs(md.Position.X-dragStart.X)+Math.Abs(md.Position.Y-dragStart.Y)>6 && onDesktop){
                            owner.checkpoints.Changed(ElapsedMs());
                        }
                    }
                }
            }
            if(code>=0 && message.ToInt32()==0x020E) {
                var mouse=(MouseData)Marshal.PtrToStructure(data,typeof(MouseData));
                if((mouse.Flags&1)==0) {
                    Interlocked.Increment(ref owner.wheelMessages);
                    // Count hook delivery only; Raw Input is the single recognition source.
                }
            }
            return CallNextHookEx(mouseHook,code,message,data);
        }
        private IntPtr OnKey(int code,IntPtr message,IntPtr data)
        {
            if(code>=0) {
                var key=(KeyboardData)Marshal.PtrToStructure(data,typeof(KeyboardData));
                if((key.Flags&0x10)==0 && (key.Key==0x25 || key.Key==0x27)) {
                    int msg=message.ToInt32(); bool down=msg==0x100||msg==0x104; bool up=msg==0x101||msg==0x105;
                    if(up && swallowed.Remove(key.Key)) return new IntPtr(1);
                    bool win=(GetAsyncKeyState(0x5B)&0x8000)!=0 || (GetAsyncKeyState(0x5C)&0x8000)!=0;
                    bool other=(GetAsyncKeyState(0x10)&0x8000)!=0 || (GetAsyncKeyState(0x11)&0x8000)!=0 || (GetAsyncKeyState(0x12)&0x8000)!=0;
                    if(down && owner.winArrows && win && !other && DesktopWindow(GetForegroundWindow())) {
                        if(swallowed.Add(key.Key)) {
                            // Prevent bare-Win Start-menu activation after a consumed arrow.
                            keybd_event(0x11,0,0,UIntPtr.Zero); keybd_event(0x11,0,2,UIntPtr.Zero);
                            owner.Enqueue(key.Key==0x27?1:-1);
                        }
                        return new IntPtr(1);
                    }
                }
            }
            return CallNextHookEx(keyHook,code,message,data);
        }
        private void Raw(IntPtr handle)
        {
            uint size=0,header=(uint)(IntPtr.Size==8?24:16);
            if(GetRawInputData(handle,0x10000003,IntPtr.Zero,ref size,header)==uint.MaxValue || size<header || size>1048576) return;
            IntPtr memory=Marshal.AllocHGlobal((int)size);
            try {
                uint got=GetRawInputData(handle,0x10000003,memory,ref size,header);
                if(got!=size) return;
                int type=Marshal.ReadInt32(memory);
                if(type==2) {
                    var cursor=System.Windows.Forms.Cursor.Position;
                    var point=new Point {X=cursor.X,Y=cursor.Y};
                    bool allowed=owner.swipe && DesktopAt(point) &&
                        (GetAsyncKeyState(1)&0x8000)==0 && (GetAsyncKeyState(2)&0x8000)==0 && (GetAsyncKeyState(4)&0x8000)==0;
                    if(!allowed)Interlocked.Increment(ref owner.hidBlockedFrames);
                    touchpad.DistanceThreshold=0.050*DeskPagesWorkspaceApi.PointerThreshold/350.0;
                    int direction=touchpad.Process(memory,size,ElapsedMs(),allowed);
                    owner.hidStatus=touchpad.Diagnostics();
                    IntPtr hit=WindowFromPoint(point);owner.lastHit=ClassName(hit)+" / "+ClassName(GetAncestor(hit,2));
                    if(direction!=0){gestures.Suppress(ElapsedMs());owner.Enqueue(owner.reverse?-direction:direction);}
                    return;
                }
                if(type!=0 || size<header+24)return;
                int flags=(ushort)Marshal.ReadInt16(memory,(int)header+4);
                if((flags&0x4)!=0){ // RI_MOUSE_RIGHT_BUTTON_DOWN, before menu
                    var cursor=System.Windows.Forms.Cursor.Position;
                    RememberContext(new Point{X=cursor.X,Y=cursor.Y});
                }
                if((flags&0x800)!=0) {
                    Interlocked.Increment(ref owner.rawMessages);
                    int delta=Marshal.ReadInt16(memory,(int)header+6);
                    owner.lastDelta=delta;
                    if(delta==0) return;
                    Interlocked.Increment(ref owner.rawNonZero);
                    var cursor=System.Windows.Forms.Cursor.Position; Wheel(delta,new Point {X=cursor.X,Y=cursor.Y},1);
                }
            } finally {Marshal.FreeHGlobal(memory);}
        }
        protected override void ExitThreadCore()
        {
            touchpad.Dispose();
            if(mouseHook!=IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
            if(keyHook!=IntPtr.Zero) UnhookWindowsHookEx(keyHook);
            for(int id=1;id<=3;id++) UnregisterHotKey(receiver.Handle,id);
            var device=new RawDevice {Page=1,Usage=2,Flags=1,Target=IntPtr.Zero};
            RegisterRawInputDevices(new RawDevice[]{device},1,(uint)Marshal.SizeOf(typeof(RawDevice)));
            receiver.DestroyHandle(); base.ExitThreadCore();
        }
        private sealed class Receiver : NativeWindow
        {
            private readonly InputPump owner;
            public Receiver(InputPump pump) {owner=pump;CreateHandle(new CreateParams {Caption="DeskPages input"});}
            protected override void WndProc(ref Message message)
            {
                if(message.Msg==0x10) {owner.ExitThread();return;}
                if(message.Msg==0x11) {owner.owner.Enqueue(9100);message.Result=new IntPtr(1);return;}
                if(message.Msg==0xFF) owner.Raw(message.LParam);
                if(message.Msg==0xFE && message.WParam.ToInt32()==2) owner.touchpad.Remove(message.LParam);
                if(message.Msg==0x312) {
                    int id=message.WParam.ToInt32();owner.owner.Enqueue(id==1?-1:id==2?1:9000);
                }
                base.WndProc(ref message);
            }
        }
    }

    public static string RunInputProbe() {
        using(var form=new TouchpadProbe()) {Application.Run(form);return form.Report();}
    }
    // No page changes. HID input remains visible to this probe in the background.
    private sealed class TouchpadProbe : Form {
        private readonly DeskPagesTouchpad touchpad=new DeskPagesTouchpad();
        private readonly System.Windows.Forms.Timer repaint;
        private int recognized;
        private string lastDirection="none";
        public TouchpadProbe() {
            Text="DeskPages 0.8.17 - HID touchpad check (no page changes)";
            Width=850;Height=500;StartPosition=FormStartPosition.CenterScreen;
            BackColor=Color.FromArgb(25,31,42);ForeColor=Color.White;DoubleBuffered=true;
            touchpad.Register(Handle);
            repaint=new System.Windows.Forms.Timer {Interval=150};repaint.Tick+=delegate {Invalidate();};repaint.Start();
        }
        public string Report() {
            return "DeskPages 0.8.17 HID input check (Digitizers 0D/05)\r\n"+touchpad.Diagnostics()+
                "\r\nRecognized horizontal gestures: "+recognized+"\r\nLast direction: "+lastDirection+
                "\r\nNo desktop page changes were performed.";
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            using(var title=new Font("Segoe UI",14)) using(var body=new Font("Segoe UI",11)) {
                e.Graphics.DrawString("Swipe LEFT and RIGHT with TWO fingers.\nAlso test after Win+D hides this window.\nAlt+Tab back, then close this window to save the report.",title,Brushes.White,20,20);
                e.Graphics.DrawString(Report(),body,Brushes.LightSkyBlue,new RectangleF(20,130,ClientSize.Width-40,ClientSize.Height-140));
            }
        }
        protected override void WndProc(ref Message message) {
            if(message.Msg==0xFE && message.WParam.ToInt32()==2)touchpad.Remove(message.LParam);
            if(message.Msg==0xFF) {
                uint size=0,header=(uint)(IntPtr.Size==8?24:16);
                if(GetRawInputData(message.LParam,0x10000003,IntPtr.Zero,ref size,header)!=uint.MaxValue && size>=header+8 && size<=1048576) {
                    IntPtr memory=Marshal.AllocHGlobal((int)size);
                    try {
                        if(GetRawInputData(message.LParam,0x10000003,memory,ref size,header)==size && Marshal.ReadInt32(memory)==2) {
                            int direction=touchpad.Process(memory,size,ElapsedMs(),true);
                            if(direction!=0){recognized++;lastDirection=direction>0?"right":"left";}
                        }
                    }finally{Marshal.FreeHGlobal(memory);}
                }
            }
            base.WndProc(ref message);
        }
        protected override void Dispose(bool disposing) {
            if(disposing){if(repaint!=null){repaint.Stop();repaint.Dispose();}touchpad.Dispose();}
            base.Dispose(disposing);
        }
    }

    private static IntPtr DesktopRoot()
    {
        IntPtr root=FindWindowW("Progman",null);
        if(FindWindowExW(root,IntPtr.Zero,"SHELLDLL_DefView",null)!=IntPtr.Zero) return root;
        IntPtr found=IntPtr.Zero;
        EnumWindows(delegate(IntPtr hwnd,IntPtr ignored) {
            if(FindWindowExW(hwnd,IntPtr.Zero,"SHELLDLL_DefView",null)==IntPtr.Zero) return true;
            found=hwnd;return false;
        },IntPtr.Zero);
        return found;
    }
    // Display-only acknowledgement, before any source layout/capture work.
    public static void BeginPageFeedback(int page,int direction){
        if(active==null || !active.showPageIndicator || page==active.currentPage)return;
        try{
            if(active.indicator==null)active.indicator=new DeskPagesIndicatorController(DeskPagesStartupGate.Log);
            if(direction==0)direction=Math.Sign(Array.IndexOf(active.knownPages,page)-Array.IndexOf(active.knownPages,active.currentPage));
            var request=DragRequest;if(request!=null && request.Kind=="Navigate")direction=request.X;
            var area=DeskPagesWorkspaceApi.PrimaryOnly?Screen.PrimaryScreen.WorkingArea:Screen.FromPoint(Cursor.Position).WorkingArea;
            active.feedback.Begin(active.indicator.Begin(page,direction,area));

        }catch(Exception ex){DeskPagesStartupGate.Log("page feedback acknowledgement skipped: "+ex.Message);}
    }
    private static void TrackFeedbackMotion(){
        if(active!=null && active.indicator!=null && active.feedback.Active)active.indicator.TrackMotion(active.feedback.Token,delegate{return DeskPagesAnimation.MotionComplete || !DeskPagesAnimation.IsVisible;});
    }
    public static void CompletePageFeedback(){if(active!=null)active.feedback.Commit();}
    public static void FailPageFeedback(){
        if(active==null || !active.feedback.Active)return;
        try{if(active.indicator!=null)active.indicator.Fail(active.feedback.Token);}catch(Exception ex){DeskPagesStartupGate.Log("page feedback failure skipped: "+ex.Message);}
        active.feedback.Cancel();
    }
    public static bool SnapshotCacheEnabled {get{return active!=null && active.snapshots!=null && active.snapshots.Enabled;}}
    public static void SetSnapshotMode(bool enabled){if(active==null || active.snapshots==null)throw new InvalidOperationException("Agent is not ready.");active.snapshots.SetEnabled(enabled);}
    public static void BeginNavigationSample(){if(active!=null && active.snapshots!=null)active.snapshots.BeginSample();}
    public static void EndNavigationSample(bool success){if(active!=null && active.snapshots!=null)active.snapshots.EndSample(success);}
    public static void SetSnapshotSource(string key){if(active!=null && active.snapshots!=null)active.snapshots.Source(key);}
    public static void SetSnapshotTarget(string key){if(active!=null && active.snapshots!=null)active.snapshots.Target(key);}
    public static void SnapshotSceneContext(string context){if(active!=null && active.snapshots!=null)active.snapshots.SceneContext(context);}
    public static string SavedSnapshotKey(int page){return active==null||active.snapshots==null?"":active.snapshots.KnownKey(page);}
    public static bool NeedsSnapshotCapture(string key){return active==null||active.snapshots==null||active.snapshots.NeedsCapture(key);}
    public static void WarmSnapshot(string key){if(!SnapshotCacheEnabled)return;Rectangle bounds=Screen.PrimaryScreen.WorkingArea;foreach(var screen in Screen.AllScreens)bounds=Rectangle.Union(bounds,screen.WorkingArea);bool ok=DeskPagesAnimation.WarmSnapshot(key,bounds);active.snapshots.Captured(key,ok);}
    private bool previewStarted;
    public static bool TryBeginCachedPreview(int direction,string source,string target){
        if(!SnapshotCacheEnabled || active==null || !active.animate || DeskPagesWorkspaceApi.ReduceMotion)return false;
        try{if(!active.snapshots.EnsureScene())return false;return StartAnimation(direction,true,source,target);}catch(Exception ex){DeskPagesAnimation.Trace("cached preview unavailable: "+ex.Message);AbortAnimation();return false;}
    }
    public static void BeginAnimation(int direction){StartAnimation(direction,false,null,null);}
    private static bool StartAnimation(int direction,bool preview,string source,string target)
    {
        if(active==null) return false;
        IntPtr foreground=GetForegroundWindow();
        string foregroundClass=ClassName(GetAncestor(foreground,2));
        Point pointer;bool pointerOnDesktop=GetCursorPos(out pointer) && DesktopAt(pointer);
        // A tray command may leave the taskbar as foreground after its menu closes.
        if(!DeskPagesNavigationPolicy.AllowPreparation(active.explicitUiCommand,DesktopWindow(foreground),foregroundClass=="Shell_TrayWnd" || foregroundClass=="Shell_SecondaryTrayWnd",pointerOnDesktop)) {
            active.animationStatus="not started: neither focus nor pointer is on the desktop";
            DeskPagesAnimation.Trace(active.animationStatus);
            throw new InvalidOperationException(active.animationStatus);
        }
        active.slideRequested=active.animate && !DeskPagesWorkspaceApi.ReduceMotion;
        active.targetConfirmed=false;
        active.transitionForeground=GetAncestor(foreground,2);
        AbortAnimation();active.previewStarted=false;
        try {
            Rectangle bounds=Screen.PrimaryScreen.WorkingArea;
            foreach(var screen in Screen.AllScreens)bounds=Rectangle.Union(bounds,screen.WorkingArea);
            active.slideDirection=direction;
            bool shown=preview?DeskPagesAnimation.BeginCachedPreview(bounds,source,target,direction):DeskPagesAnimation.BeginDesktop(bounds);
            active.animationStatus=shown?(preview?"resident images presented before Explorer preparation":"desktop source prepared below apps"):DeskPagesAnimation.Metrics;
            if(!shown){
                // No full-screen fallback: never animate a captured app window.
                AbortAnimation();DeskPagesAnimation.Trace("isolated capture unavailable; switching without animation: "+active.animationStatus);return false;
            }
            active.previewStarted=preview;
            if(preview && active.snapshots!=null)active.snapshots.MarkSlideStarted();if(preview)TrackFeedbackMotion();
            DeskPagesAnimation.Trace(preview?"cached slide submitted before Explorer preparation; zero screenshot capture":"source prepared before visibility changes");return true;
        } catch(Exception ex) {active.animationStatus="transition preparation failed: "+ex.Message;AbortAnimation();throw;}
    }
    public static void ConfirmAnimationTarget(int iconCount)
    {
        if(active==null || !DeskPagesAnimation.IsVisible)return;
        DeskPagesAnimation.ConfirmDesktopTarget(iconCount);
        active.targetConfirmed=true;
        DeskPagesAnimation.Trace("target membership/attributes/positions checked; native icon count="+iconCount);
    }
    public static void FinishAnimation()
    {
        if(active==null || !DeskPagesAnimation.IsVisible) return;
        if(!active.slideRequested) {active.animationStatus="update covered; slide disabled in saved preferences";DeskPagesAnimation.Trace(active.animationStatus);AbortAnimation();return;}
        IntPtr foreground=GetForegroundWindow();
        string foregroundClass=ClassName(GetAncestor(foreground,2));
        if(GetAncestor(foreground,2)!=active.transitionForeground && !DesktopWindow(foreground) && foregroundClass!="Shell_TrayWnd" && foregroundClass!="Shell_SecondaryTrayWnd") {
            active.animationStatus="transition cancelled: foreground changed during page preparation";DeskPagesAnimation.Trace(active.animationStatus);AbortAnimation();return;
        }
        try {
            if(!active.targetConfirmed)throw new InvalidOperationException("Target page was not confirmed; refusing to animate an unverified image.");
            if(active.previewStarted)DeskPagesAnimation.CompleteCachedPreview();else {DeskPagesAnimation.CaptureAndPlay(active.slideDirection);TrackFeedbackMotion();}
            active.animationStatus="native DirectComposition slide; desktop-window-only capture below apps";
            DeskPagesAnimation.Trace((active.previewStarted?"cached preview handed to verified live desktop: ":"slide submitted: ")+DeskPagesAnimation.Metrics);
        }catch(Exception ex){active.animationStatus="transition failed: "+ex.Message;DeskPagesAnimation.Trace(active.animationStatus);AbortAnimation();throw;}
    }
    public static void AbortAnimation() {DeskPagesAnimation.Abort();}
}
