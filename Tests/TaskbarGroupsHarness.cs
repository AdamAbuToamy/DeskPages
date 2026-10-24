using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public static class TaskbarGroupsHarness {
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    [STAThread]public static int Main(string[] args){
        try{
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            if(args.Length==1 && args[0]=="--window"){
                using(var f=new Form{Text="DeskPages grouping fixture",Size=new Size(320,180)}){
                    WindowShell.Set(f.Handle,5,"DeskPages.Fixture.Original");f.Shown+=delegate{Console.WriteLine(f.Handle.ToInt64());Console.Out.Flush();};Application.Run(f);
                }return 0;
            }
            Model();if(Environment.OSVersion.Platform==PlatformID.Win32NT)Native();
            Console.WriteLine("PASS: taskbar group model, dwell, closed-button rejection and exact window matching.");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    private static void Model(){
        Check(!GroupRefreshPolicy.Due(999,0,true,false,false) && GroupRefreshPolicy.Due(1000,0,true,false,false),"Window events were not coalesced");
        Check(!GroupRefreshPolicy.Due(1999,0,false,false,false) && GroupRefreshPolicy.Due(2000,0,false,false,false),"Idle UIA fallback changed");
        Check(!GroupRefreshPolicy.Due(79,0,false,true,true) && GroupRefreshPolicy.Due(80,0,false,true,true),"Active taskbar drag lost fast region refresh");
        Check(!GroupRefreshPolicy.ResolveWindows(30000,0,false,true),"Idle window event enumerated every open app");
        Check(GroupRefreshPolicy.ResolveWindows(100,0,true,true) && !GroupRefreshPolicy.ResolveWindows(9999,0,true,false) && GroupRefreshPolicy.ResolveWindows(10000,0,true,false),"Interactive identity refresh/fallback changed");
        int idleReads=0;long last=0;for(long now=40;now<=30000;now+=40)if(GroupRefreshPolicy.Due(now,last,false,false,false)){last=now;idleReads++;}Check(idleReads==15,"Idle refresh budget exceeded");

        var model=new GroupModel(Guid.NewGuid().ToString("N"));var first=model.Add();var second=model.Add();
        Check(model.Prune(w=>true).Length==0,"New empty group vanished before receiving an app");
        var window=new GroupWindow{Handle=10,ProcessId=20,Started=30,Title="Work",AppId="Chrome"};
        model.Put(first,window);model.Put(second,window);Check(first.Windows.Count==0 && second.Windows.Count==1,"A window belongs to two groups");
        Check(model.Prune(w=>true).Single()==first,"Previously populated empty group was not removed");
        var reused=new GroupWindow{Handle=10,ProcessId=20,Started=31};Check(model.Owner(reused)==null,"A restarted process inherited membership");
        model.Remove(window);Check(model.Prune(w=>true).Single()==second,"Drag-out did not remove the empty group");
        var dwell=new GroupDropDwell();Check(dwell.Observe("a",0) && dwell.Observe("b",1),"Dropping on a group still requires a dwell");dwell.Reset();Check(!dwell.Observe(null,2),"No target was accepted");
        Check(TaskbarAccess.Resolve(new TaskButton{AppId="Chrome"},new GroupWindow[0]).Length==0,"Closed pinned button was accepted");
        Check(TaskbarAccess.Resolve(new TaskButton{AppId="Chrome"},new[]{window}).Length==1,"Exact native AppID was not resolved");
        Check(TaskbarAccess.Resolve(new TaskButton{Name="Work"},new[]{window,new GroupWindow{Title="Work"}}).Length==0,"Ambiguous thumbnail selected an arbitrary window");
        var classic=new GroupWindow{ImagePath="C:\\Apps\\Code.exe",Title="Project"};
        var aliases=new Dictionary<string,HashSet<string>>{{"Microsoft.VisualStudioCode",new HashSet<string>(StringComparer.OrdinalIgnoreCase){classic.ImagePath}}};
        Check(TaskbarAccess.ResolveShortcut(new TaskButton{AppId="Microsoft.VisualStudioCode"},new[]{classic},aliases).Length==1,"Classic process-only AppID shortcut mapping failed");
        classic.AppId="Another.Profile";Check(TaskbarAccess.ResolveShortcut(new TaskButton{AppId="Microsoft.VisualStudioCode"},new[]{classic},aliases).Length==0,"Shortcut alias overrode a distinct explicit window profile");
        classic.AppId=null;aliases.Add("Another.Process.Profile",new HashSet<string>(StringComparer.OrdinalIgnoreCase){classic.ImagePath});
        Check(TaskbarAccess.ResolveShortcut(new TaskButton{AppId="Microsoft.VisualStudioCode"},new[]{classic},aliases).Length==0,"Ambiguous process-only shortcut profiles were combined");
        Check(TaskbarAccess.ResolveShortcut(new TaskButton{AppId="Microsoft.VisualStudioCode"},new[]{classic},aliases,new[]{"Microsoft.VisualStudioCode"}).Length==1,"Unused shortcut profiles blocked VS Code's live button");
        Check(TaskbarAccess.ResolveShortcut(new TaskButton{AppId="Microsoft.VisualStudioCode"},new[]{classic},aliases,new[]{"Microsoft.VisualStudioCode","Another.Process.Profile"}).Length==0,"Two live profiles were merged");
        var original=new[]{new TaskButton{AppId="group",Bounds=new Rectangle(200,0,48,48)}};
        var shifted=new TaskbarSnapshot{Buttons=new[]{new TaskButton{AppId="Chrome",Bounds=new Rectangle(200,0,48,48)},new TaskButton{AppId="group",Bounds=new Rectangle(152,0,48,48)}}};
        Check(TaskbarAccess.DropTarget(shifted,original,new Point(220,20),"Chrome",id=>id=="group").AppId=="group","Explorer's native reorder lost the accepted group");
        Check(TaskbarAccess.DropTarget(shifted,original,new Point(220,20),"AnotherApp",id=>id=="group")==null,"An unrelated app at the old position was accepted");
        shifted.Buttons[1].Bounds=new Rectangle(0,0,48,48);Check(TaskbarAccess.DropTarget(shifted,original,new Point(220,20),"Chrome",id=>id=="group")==null,"A far-away group was accepted after rearrangement");
        var source=new GroupDragSource{Button=new TaskButton{AppId="Chrome",Bounds=new Rectangle(100,0,48,48)},Windows=new[]{window}};
        var directEnd=GroupDragGeometry.NativeEnd(new GroupDragGesture{Source=source,Groups=original,Press=new Point(120,20),LastNative=new Point(120,20)},4);
        Check(directEnd.HasValue && source.Button.Bounds.Contains(directEnd.Value) && Math.Abs(directEnd.Value.X-120)+Math.Abs(directEnd.Value.Y-20)>4,"Direct source-to-group movement could complete as an application click");
        var inputMap=new GroupInputMap{Taskbar=new IntPtr(123),Created=100,Groups=original,Sources=new[]{source}};
        var gate=new GroupDragGate();GroupDragGesture begun=null,ended=null;int takes=0,resumes=0,refreshes=0;
        Action<GroupDragGesture> begin=g=>begun=g,end=g=>ended=g;
        Func<GroupDragGesture,Point,bool> take=(g,p)=>{takes++;return true;},resume=(g,p)=>{resumes++;return true;};
        Check(!gate.Process(0x201,new Point(220,20),false,false,inputMap.Taskbar,inputMap,101,begin,end),"Group press was intercepted");
        gate.Process(0x202,new Point(220,20),false,false,inputMap.Taskbar,inputMap,102,begin,end);
        Check(!gate.Process(0x201,new Point(120,20),true,false,inputMap.Taskbar,inputMap,103,begin,end),"Own injected replay was observed as a source");
        Check(!gate.Process(0x201,new Point(120,20),false,true,inputMap.Taskbar,inputMap,104,begin,end),"Modified source press intercepted");
        Check(!gate.Process(0x201,new Point(120,20),false,false,new IntPtr(999),inputMap,105,begin,end),"Foreign root observed as taskbar");
        Check(!gate.Process(0x201,new Point(120,20),false,false,inputMap.Taskbar,inputMap,3101,begin,end),"Stale regions intercepted a press");
        // Normal reordering is native from press through release, with no replay.
        Check(!gate.Process(0x201,new Point(120,20),false,false,inputMap.Taskbar,inputMap,110,begin,end),"Source down no longer reaches Explorer");
        Check(!gate.Process(0x200,new Point(170,20),false,false,inputMap.Taskbar,inputMap,111,begin,end,null,take,resume),"Ordinary reorder motion was suppressed");
        Check(!gate.Process(0x202,new Point(170,20),false,false,inputMap.Taskbar,inputMap,112,begin,end,()=>refreshes++),"Ordinary reorder release suppressed");
        Check(begun==null && ended==null && takes==0 && refreshes==1,"Normal reorder used a captured/replayed gesture");
        // Group entry owns only the final segment. Release has no dwell.
        gate.Process(0x201,new Point(120,20),false,false,inputMap.Taskbar,inputMap,120,begin,end);
        Check(gate.Process(0x200,new Point(220,20),false,false,inputMap.Taskbar,inputMap,121,begin,end,null,take,resume) && begun!=null && takes==1,"Target entry did not take ownership");
        Check(gate.Process(0x202,new Point(220,20),false,false,inputMap.Taskbar,inputMap,122,begin,end) && ended==begun && ended.TargetAppId=="group" && ended.Released,"Immediate release lost its target");
        Check(!gate.Process(0x202,new Point(220,20),false,false,inputMap.Taskbar,inputMap,123,begin,end),"Release consumed twice");
        begun=null;ended=null;gate.Process(0x201,new Point(120,20),false,false,inputMap.Taskbar,inputMap,125,begin,end);
        Check(gate.Process(0x202,new Point(220,20),false,false,inputMap.Taskbar,inputMap,126,begin,end,null,take,resume) && begun!=null && ended==begun && ended.TargetAppId=="group","A coalesced release before its move lost the group target");
        begun=null;ended=null;gate.Process(0x201,new Point(120,20),false,false,inputMap.Taskbar,inputMap,130,begin,end);
        gate.Process(0x200,new Point(220,20),false,false,inputMap.Taskbar,inputMap,131,begin,end,null,take,resume);
        Check(gate.Process(0x200,new Point(170,20),false,false,inputMap.Taskbar,inputMap,132,begin,end,null,take,resume) && resumes==1 && ended!=null && !ended.Released && ended.NativeActive,"Leaving a group did not resume native reordering before release");
        Check(!gate.Process(0x202,new Point(170,20),false,false,inputMap.Taskbar,inputMap,133,begin,end),"Resumed native release suppressed");
        // A transient inability to resume must keep ownership, never press a guessed source.
        ended=null;gate.Process(0x201,new Point(120,20),false,false,inputMap.Taskbar,inputMap,140,begin,end);
        gate.Process(0x200,new Point(220,20),false,false,inputMap.Taskbar,inputMap,141,begin,end,null,take,resume);
        Check(gate.Process(0x200,new Point(170,20),false,false,inputMap.Taskbar,inputMap,142,begin,end,null,take,(g,p)=>false) && ended==null,"Failed resume leaked a native drag");
        gate.Cancel();gate.Process(0x202,new Point(220,20),false,false,inputMap.Taskbar,inputMap,143,begin,end);Check(ended.Cancelled,"Escape lost cancellation");
        gate.Process(0x201,new Point(120,20),false,false,inputMap.Taskbar,inputMap,150,begin,end);gate.Abort(end);
        Check(!gate.Process(0x201,new Point(120,20),false,false,inputMap.Taskbar,inputMap,151,begin,end),"Aborted native drag blocked another press");gate.Abort(end);
        source.Windows=new GroupWindow[0];Check(inputMap.SourceAt(new Point(120,20),160)!=null,"Unresolved metadata blocked normal native dragging");
        var monitor=new Rectangle(0,0,1920,1080);var bar=new Rectangle(0,1032,1920,48);
        Check(GroupLayoutPolicy.AddBounds(Rectangle.Empty,Rectangle.Empty,monitor,new Rectangle[0],false).IsEmpty,"Missing taskbar left a floating '+'");
        Check(GroupLayoutPolicy.AddBounds(new Rectangle(0,1079,1920,48),Rectangle.Empty,monitor,new Rectangle[0],false).IsEmpty,"Auto-hidden taskbar left a floating '+'");
        Check(!TaskbarVisibility.Exposed(true,bar,monitor,false) && !TaskbarVisibility.Exposed(false,bar,monitor,true),"Occluded or hidden taskbar exposed '+'");
        Check(TaskbarVisibility.Exposed(true,bar,monitor,true),"Visible taskbar lost '+'");
        var moving=GroupLayoutPolicy.AddBounds(new Rectangle(0,1042,1920,48),new Rectangle(0,1042,120,48),monitor,new Rectangle[0],true);
        Check(moving.Y==GroupLayoutPolicy.AddBounds(bar,new Rectangle(0,1032,120,48),monitor,new Rectangle[0],true).Y+10,"Button was clamped instead of following native bar motion");
        var fallback=GroupLayoutPolicy.AddBounds(bar,Rectangle.Empty,monitor,new Rectangle[0],false);
        Check(!fallback.IsEmpty && fallback.Bottom<bar.Top,"Native taskbar fallback hid '+' or covered unknown app buttons before UIA was ready");
        Check(GroupLayoutPolicy.AddBounds(bar,new Rectangle(0,1032,120,48),monitor,new Rectangle[0],true).Left==128,"Weather-adjacent placement changed");
        var controls=new[]{new Rectangle(0,1032,450,48),new Rectangle(450,1032,100,48),new Rectangle(1700,1032,220,48)};
        var leftPlus=GroupLayoutPolicy.AddBounds(bar,new Rectangle(0,1032,48,48),monitor,controls,true);
        Check(!controls.Any(b=>b.IntersectsWith(leftPlus)) && monitor.Contains(leftPlus),"Left-aligned Start/apps/tray overlap persistent +");
        var fullPlus=GroupLayoutPolicy.AddBounds(bar,Rectangle.Empty,monitor,new[]{bar},true);
        Check(fullPlus.Bottom<bar.Top && monitor.Contains(fullPlus),"Full taskbar did not move + into a safe visible fallback");
        Check(GroupLayoutPolicy.ShellSurface("Progman") && GroupLayoutPolicy.ShellSurface("WorkerW") && GroupLayoutPolicy.ShellSurface("Shell_TrayWnd") && !GroupLayoutPolicy.ShellSurface("CabinetWClass"),"Explorer taskbar grouping included a desktop/taskbar Shell surface");
    }
    private static void Native(){
        TaskbarVisibilityFixture();
        var group=new GroupModel(Guid.NewGuid().ToString("N")).Add();
        int opens=0;
        using(var anchor=new GroupAnchor(group,delegate{opens++;}))using(var taskbar=new GroupTaskbar()){
            WindowShell.Set(anchor.Handle,5,group.AppId);
            Check(WindowShell.Get(anchor.Handle,5)==group.AppId,"Group identity was not set before first show");
            anchor.Show();Application.DoEvents();taskbar.Add(anchor.Handle);
            long style=GetWindowLongPtrW(anchor.Handle,-20).ToInt64();
            Check(anchor.ShowInTaskbar && anchor.Owner==null && (style&0x40000)!=0 && (style&0x08000080)==0,"Numbered group is not eligible for a native taskbar button");
            Check(WindowShell.IsWindowVisible(anchor.Handle) && IsIconic(anchor.Handle),"Empty group must remain visible to Explorer and minimized");
            SendMessageW(anchor.Handle,0x112,new IntPtr(0xF120),IntPtr.Zero);Application.DoEvents();
            Check(IsIconic(anchor.Handle) && opens==1,"Taskbar restore opened a normal group window");
            ShowWindow(anchor.Handle,9);Application.DoEvents();Check(IsIconic(anchor.Handle),"Direct native restore displayed the group as an application");
            taskbar.Remove(anchor.Handle);anchor.Finish();
        }
        using(var normal=GroupAddButton.Surface(36,36,0,false))using(var pressed=GroupAddButton.Surface(36,36,1,true)){
            var background=normal.GetPixel(8,8);Check(background.A>0 && background.A<255 && background.R>=253 && background.R==background.G && background.G==background.B,"Add background is not translucent white: "+background);
            Check(normal.GetPixel(18,18).A==255,"Plus glyph lost its opaque contrast");
            Check(normal.GetPixel(2,18)!=pressed.GetPixel(2,18),"Press animation did not change the button shape");
        }
        using(var listener=new GroupDragInput(delegate{},delegate{})){
            listener.Publish(new GroupInputMap());
        }
        Console.WriteLine("PASS: input hook thread installs/uninstalls; native group restore is rejected before showing a client window.");
        HookOwnershipFixture();
        PopupLifecycle();
        int clicks=0;
        using(var button=new GroupAddButton(delegate{clicks++;})){
            button.Present(new Rectangle(150,150,36,36),true);Application.DoEvents();Check(button.Visible,"Visible taskbar did not show '+'");
            button.Present(Rectangle.Empty,false);Application.DoEvents();Check(!button.Visible,"Hidden taskbar left '+' visible");
            button.Show();Application.DoEvents();
            var down=typeof(GroupAddButton).GetMethod("OnMouseDown",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            var up=typeof(GroupAddButton).GetMethod("OnMouseUp",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            var inside=new MouseEventArgs(MouseButtons.Left,1,16,16,0);var outside=new MouseEventArgs(MouseButtons.Left,1,-10,-10,0);
            up.Invoke(button,new object[]{inside});Check(clicks==0,"Unarmed mouse-up added a group");
            down.Invoke(button,new object[]{inside});up.Invoke(button,new object[]{outside});Check(clicks==0,"Release outside added a group");
            down.Invoke(button,new object[]{inside});up.Invoke(button,new object[]{inside});Check(clicks==1,"Button press did not add exactly one group");
            button.Hide();button.Size=new Size(36,36);button.Location=new Point(150,150);button.Show();Application.DoEvents();
            Check(WindowShell.IsWindowVisible(button.Handle),"Layered add button did not redraw after hidden resize/show");
            button.Hide();
        }
        Console.WriteLine("PASS: layered button show and click-once/cancel semantics.");
        Console.WriteLine("PASS: minimized unowned APPWINDOW anchor, pre-show AppID, native AddTab/RemoveTab; white per-pixel alpha and press surface.");
        Process child=null;GroupRecovery recovery=null;IntPtr hwnd=IntPtr.Zero;
        try{
            child=Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--window"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true});
            var line=child.StandardOutput.ReadLineAsync();Check(line.Wait(20000),"Fixture window did not become ready");hwnd=new IntPtr(long.Parse(line.Result));
            var window=WindowShell.Identity(hwnd);Check(window!=null && window.ProcessId==child.Id,"Cannot identify the foreign fixture window");
            string session=Guid.NewGuid().ToString("N");recovery=new GroupRecovery(session);
            uint[] keys={2,3,4,5};string[] original=keys.Select(k=>WindowShell.Get(hwnd,k)).ToArray();
            string icon=Path.Combine(recovery.Folder,"test.ico");using(var picture=GroupVisuals.Number(1))using(var output=File.Create(icon))picture.Save(output);
            string[] assigned={"fixture-command",icon+",0","DeskPages · 01","DeskPages.LiveGroups."+session+".1"};
            recovery.Assign(window,assigned);
            Check(!child.HasExited && WindowShell.IsWindowVisible(hwnd),"Grouping hid or closed the application");
            for(int i=0;i<4;i++)Check(WindowShell.Get(hwnd,keys[i])==assigned[i],"Foreign window Shell property did not change");
            string[] second={"second-command",icon+",0","DeskPages · 02","DeskPages.LiveGroups."+session+".2"};
            recovery.Assign(window,second);
            Check(WindowShell.Get(hwnd,5)==second[3],"Moving between groups did not update native membership");
            // Run the actual guardian recovery path against a dead owner. This
            // reloads the durable journal; it cannot rely on the original object.
            Check(GroupRecovery.Watch(session,int.MaxValue,1)==0,"Guardian could not recover its on-disk journal");
            for(int i=0;i<4;i++)Check(WindowShell.Get(hwnd,keys[i])==original[i],"Guardian changed an original property, including an unset one");
            var stale=new GroupWindow{Handle=window.Handle,ProcessId=window.ProcessId,Started=window.Started+1};
            bool rejected=false;try{recovery.Assign(stale,assigned);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Stale HWND/process identity was accepted");
            PickerAssignment(window);
            Console.WriteLine("PASS: foreign-process window AppID/icon/relaunch properties; visible live app; persisted guardian restoration; exact unset-value restoration; stale identity rejection.");
        }finally{
            if(recovery!=null){recovery.RestoreAll();Directory.Delete(recovery.Folder,true);}
            if(child!=null){if(!child.HasExited){PostMessageW(hwnd,0x10,IntPtr.Zero,IntPtr.Zero);if(!child.WaitForExit(5000))child.Kill();}child.Dispose();}
        }
    }
    private static void TaskbarVisibilityFixture(){
        var area=Screen.PrimaryScreen.WorkingArea;var monitor=new Rectangle(area.Left+20,area.Top+20,Math.Min(520,area.Width-40),300);
        using(var bar=new Form{FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,TopMost=true,StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(monitor.Left,monitor.Bottom-48,monitor.Width,48)})
        using(var fullscreen=new Form{FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,TopMost=true,StartPosition=FormStartPosition.Manual,Bounds=monitor}){
            bar.Show();Application.DoEvents();Check(TaskbarVisibility.Read(bar.Handle,bar.Bounds,monitor),"Exposed owned taskbar fixture was hidden");
            fullscreen.Show();fullscreen.BringToFront();Application.DoEvents();
            Check(WindowShell.IsWindowVisible(bar.Handle) && !TaskbarVisibility.Read(bar.Handle,bar.Bounds,monitor),"Visible-style taskbar covered by a fullscreen fixture still exposed '+'");
            fullscreen.Hide();Application.DoEvents();Check(TaskbarVisibility.Read(bar.Handle,bar.Bounds,monitor),"Taskbar reappearance did not restore '+' eligibility");
            bar.Bounds=new Rectangle(monitor.Left,monitor.Bottom-1,monitor.Width,48);Application.DoEvents();Check(!TaskbarVisibility.Read(bar.Handle,bar.Bounds,monitor),"Auto-hidden taskbar strip exposed '+'");
        }
        Console.WriteLine("PASS: native taskbar exposure follows owned-window fullscreen occlusion, reappearance and auto-hide strip; real Shell taskbar configuration unchanged.");
    }
    private static void HookOwnershipFixture(){
        var original=Cursor.Position;
        try{
            using(var form=new Form{Text="DeskPages native drag fixture",StartPosition=FormStartPosition.Manual,Location=new Point(100,100),ClientSize=new Size(460,120),TopMost=true}){
                int nativeDowns=0,nativeUps=0,begins=0,ends=0,leftMovesOnGroup=0,leftMovesBetweenApps=0,nativeGroupMoves=0;bool nativeDragging=false;var trace=new List<string>();GroupDragGesture received=null;
                form.Show();form.Activate();Application.DoEvents();
                Point source=form.PointToScreen(new Point(30,30)),other=form.PointToScreen(new Point(140,30)),target=form.PointToScreen(new Point(330,30));
                var button=new TaskButton{AppId="FixtureApp",Bounds=new Rectangle(source.X-15,source.Y-15,40,40)};
                var group=new TaskButton{AppId="FixtureGroup",Bounds=new Rectangle(target.X-20,target.Y-20,48,48)};
                form.MouseDown+=delegate{nativeDowns++;nativeDragging=true;trace.Add("down");};form.MouseUp+=delegate{nativeUps++;nativeDragging=false;trace.Add("up");};
                form.MouseMove+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){var point=form.PointToScreen(e.Location);if(group.Bounds.Contains(point)){leftMovesOnGroup++;trace.Add("groupMove:drag="+nativeDragging);if(nativeDragging)nativeGroupMoves++;}if(nativeDragging && Math.Abs(point.X-other.X)<4)leftMovesBetweenApps++;}};
                using(var listener=new GroupDragInput(g=>System.Threading.Interlocked.Increment(ref begins),g=>{received=g;System.Threading.Interlocked.Increment(ref ends);})){
                    Func<GroupInputMap> map=()=>new GroupInputMap{Taskbar=form.Handle,Bounds=form.Bounds,Created=Clock(),Groups=new[]{group},Sources=new[]{new GroupDragSource{Button=button,Windows=new GroupWindow[0]}}};
                    Action<string,Func<bool>> wait=(stage,done)=>{try{PumpUntil(done);}catch(Exception ex){throw new Exception(stage+"; downs="+nativeDowns+"; ups="+nativeUps+"; begins="+begins+"; ends="+ends+"; targetMoves="+nativeGroupMoves+"; trace="+string.Join(",",trace)+"; "+listener.Diagnostics,ex);}};
                    // Existing application movement is real native down/move/up.
                    listener.Publish(map());SendFixturePart(source,2);wait("native ordinary press",delegate{return nativeDowns==1;});SendFixturePart(other,0);wait("native ordinary motion",delegate{return leftMovesBetweenApps>0;});SendFixturePart(other,4);wait("native ordinary release",delegate{return nativeUps==1;});
                    Check(nativeDowns==1 && begins==0 && ends==0 && leftMovesBetweenApps>0,"Ordinary reorder was captured instead of reaching the native window live");
                    listener.Publish(map());SendFixturePart(source,2);wait("second native press",delegate{return nativeDowns==2;});SendFixturePart(target,4);wait("immediate target drop",delegate{return ends==1 && nativeUps==2;});Application.DoEvents();
                    Check(begins==1 && nativeDowns==2 && nativeGroupMoves==0 && received.Released && received.TargetAppId=="FixtureGroup","Immediate target: downs="+nativeDowns+", ups="+nativeUps+", begins="+begins+", ends="+ends+", targetMoves="+nativeGroupMoves+", leftFlags="+leftMovesOnGroup+", trace="+string.Join(",",trace)+", released="+received.Released+", target="+received.TargetAppId+"; "+listener.Diagnostics);
                    // Leaving the target resumes native movement while still held.
                    listener.Publish(map());SendFixturePart(source,2);wait("third native press",delegate{return nativeDowns==3;});SendFixturePart(target,0);wait("second target entry",delegate{return begins==2 && nativeUps==3;});
                    PumpFor(120);listener.Publish(map());SendFixturePart(other,0);wait("resume before release",delegate{return ends==2 && nativeDowns==4;});
                    Check(!received.Released && received.NativeActive,"Native resume waited for physical release");
                    SendFixturePart(other,4);wait("resumed native release",delegate{return nativeUps==4;});
                    Check(begins==2 && ends==2 && nativeGroupMoves==0,"Own continuation recursively entered input or moved the native target");
                    listener.Publish(map());SendFixturePart(target,2);wait("native group-source press",delegate{return nativeDowns==5;});SendFixturePart(other,4);wait("native group-source release",delegate{return nativeUps==5;});
                    Check(nativeDowns==5 && begins==2,"Dragging the numbered group itself was intercepted");
                }
                form.Close();
            }
            Console.WriteLine("PASS: actual low-level hook passes ordinary down/move/up live, transfers only at group entry, accepts immediate release, resumes native drag on exit while held, and leaves group-source dragging native. Owned fixture, not physical Win11 taskbar.");
        }finally{Cursor.Position=original;}
    }
    private static long Clock(){return (long)(Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency);}
    private static void PumpFor(int milliseconds){var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<milliseconds){Application.DoEvents();System.Threading.Thread.Sleep(5);}}
    private static void SendFixturePart(Point point,uint button){
        var screen=SystemInformation.VirtualScreen;
        var move=new FixtureInput{Mouse=new FixtureMouse{X=(int)Math.Round((point.X-screen.Left)*65535.0/(screen.Width-1)),Y=(int)Math.Round((point.Y-screen.Top)*65535.0/(screen.Height-1)),Flags=0xE001}};
        var batch=button==0?new[]{move}:new[]{move,new FixtureInput{Mouse=new FixtureMouse{Flags=button}}};
        Check(SendInput((uint)batch.Length,batch,40)==batch.Length,"Fixture native input failed");
    }
    private static void SendFixtureInput(Point start,Point finish){
        var screen=SystemInformation.VirtualScreen;
        Func<Point,FixtureInput> move=p=>new FixtureInput{Mouse=new FixtureMouse{X=(int)Math.Round((p.X-screen.Left)*65535.0/(screen.Width-1)),Y=(int)Math.Round((p.Y-screen.Top)*65535.0/(screen.Height-1)),Flags=0xE001}};
        var batch=new[]{move(start),new FixtureInput{Mouse=new FixtureMouse{Flags=2}},move(finish),new FixtureInput{Mouse=new FixtureMouse{Flags=4}}};
        Check(SendInput((uint)batch.Length,batch,40)==batch.Length,"Fixture SendInput failed");
    }
    private static void PickerAssignment(GroupWindow window){
        var session=Guid.NewGuid().ToString("N");var recovery=new GroupRecovery(session);bool committed=false;
        try{
            using(var picker=new GroupWindowPicker(1,chosen=>{Check(chosen.Length==1 && chosen[0].Key==window.Key,"Picker changed the selected live window");recovery.Assign(chosen[0],new[]{"fixture-command","fixture-icon,0","Fixture group","DeskPages.Fixture.Picker"});committed=true;},()=>new[]{window})){
                picker.Show();PumpUntil(delegate{return picker.Apps.Items.Count==1;});picker.Apps.Items[0].Checked=true;Application.DoEvents();picker.Add.PerformClick();
                Check(committed && WindowShell.Get(new IntPtr(window.Handle),5)=="DeskPages.Fixture.Picker","Picker did not assign the real foreign live window");
            }
            Console.WriteLine("PASS: open-window picker selection commits the exact foreign HWND through the real Shell-property recovery adapter.");
        }finally{recovery.RestoreAll();Directory.Delete(recovery.Folder,true);}
    }
    [StructLayout(LayoutKind.Sequential)]private struct FixtureMouse{public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Explicit,Size=40)]private struct FixtureInput{[FieldOffset(0)]public uint Type;[FieldOffset(8)]public FixtureMouse Mouse;}
    [DllImport("user32.dll")]private static extern uint SendInput(uint count,FixtureInput[] input,int size);
    private static void PopupLifecycle(){
        var model=new GroupModel(Guid.NewGuid().ToString("N"));var group=model.Add();int removed=0;var anchor=new GroupAnchor(group,delegate{});anchor.Show();
        using(var owner=new Form{Text="DeskPages popup fixture",StartPosition=FormStartPosition.Manual,Location=new Point(100,100),Size=new Size(200,100),TopMost=true}){
            owner.Show();owner.Activate();var handle=owner.Handle;
            using(var popup=new GroupPopup(owner,g=>model.Find(g.AppId)==g,g=>{model.DeleteEmpty(g);anchor.Finish();removed++;},delegate{},delegate{})){
                var menu=popup.Menu;
                for(int i=0;i<40;i++){
                    popup.Show(group,owner.PointToScreen(new Point(8,8)));Application.DoEvents();
                    popup.Menu.Close();Application.DoEvents();
                    Check(!menu.IsDisposed && popup.Menu==menu,"Closing the group menu disposed it while WinForms still owned its events");
                }
                popup.Show(group,owner.PointToScreen(new Point(8,8)));Application.DoEvents();
                menu.Items.Cast<ToolStripItem>().Single(i=>i.Text=="Delete group").PerformClick();Check(removed==0,"Group deletion ran inside the ToolStrip click handler");
                menu.Close();PumpUntil(delegate{return removed==1;});
                Check(anchor.IsDisposed && !menu.IsDisposed,"Deleting the anchor disposed its stable-owner popup");
                var next=model.Add();model.Put(next,new GroupWindow{Handle=1,ProcessId=2,Started=3,Title="Populated app"});popup.Show(next,owner.PointToScreen(new Point(8,8)));Application.DoEvents();menu.Close();Application.DoEvents();
                Check(!menu.IsDisposed && menu.Items.Cast<ToolStripItem>().Any(i=>i.Text=="Delete group"),"Populated group lost its own Delete action");
            }
            owner.Close();
        }
        if(!anchor.IsDisposed)anchor.Finish();
        Console.WriteLine("PASS: actual popup show/close repeated 40 times; delete deferred outside click; anchor disposed; stable popup reused; no disposed-menu access.");
    }
    private static void PumpUntil(Func<bool> complete){var watch=Stopwatch.StartNew();while(!complete() && watch.ElapsedMilliseconds<2000){Application.DoEvents();System.Threading.Thread.Sleep(5);}Check(complete(),"Native input fixture timed out");}
    [DllImport("user32.dll")]private static extern IntPtr SendMessageW(IntPtr h,uint message,IntPtr w,IntPtr l);
    [DllImport("user32.dll")]private static extern bool ShowWindow(IntPtr h,int command);
    [DllImport("user32.dll")]private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd,int index);
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern bool PostMessageW(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
}
