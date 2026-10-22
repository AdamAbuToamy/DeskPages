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
        var model=new GroupModel(Guid.NewGuid().ToString("N"));var first=model.Add();var second=model.Add();
        Check(model.Prune(w=>true).Length==0,"New empty group vanished before receiving an app");
        var window=new GroupWindow{Handle=10,ProcessId=20,Started=30,Title="Work",AppId="Chrome"};
        model.Put(first,window);model.Put(second,window);Check(first.Windows.Count==0 && second.Windows.Count==1,"A window belongs to two groups");
        Check(model.Prune(w=>true).Single()==first,"Previously populated empty group was not removed");
        var reused=new GroupWindow{Handle=10,ProcessId=20,Started=31};Check(model.Owner(reused)==null,"A restarted process inherited membership");
        model.Remove(window);Check(model.Prune(w=>true).Single()==second,"Drag-out did not remove the empty group");
        var dwell=new GroupDropDwell();Check(!dwell.Observe("a",0) && !dwell.Observe("a",1199) && dwell.Observe("a",1200),"Dwell accepted an early drop");
        Check(!dwell.Observe("b",1201),"Moving over another group reused the old dwell");dwell.Reset();Check(!dwell.Observe("b",5000),"Cancelled drag reused acceptance");
        Check(TaskbarAccess.Resolve(new TaskButton{AppId="Chrome"},new GroupWindow[0]).Length==0,"Closed pinned button was accepted");
        Check(TaskbarAccess.Resolve(new TaskButton{AppId="Chrome"},new[]{window}).Length==1,"Exact native AppID was not resolved");
        Check(TaskbarAccess.Resolve(new TaskButton{Name="Work"},new[]{window,new GroupWindow{Title="Work"}}).Length==0,"Ambiguous thumbnail selected an arbitrary window");
        var classic=new GroupWindow{ImagePath="C:\\Apps\\Code.exe",Title="Project"};
        var aliases=new Dictionary<string,HashSet<string>>{{"Microsoft.VisualStudioCode",new HashSet<string>(StringComparer.OrdinalIgnoreCase){classic.ImagePath}}};
        Check(TaskbarAccess.ResolveShortcut(new TaskButton{AppId="Microsoft.VisualStudioCode"},new[]{classic},aliases).Length==1,"Classic process-only AppID shortcut mapping failed");
        classic.AppId="Another.Profile";Check(TaskbarAccess.ResolveShortcut(new TaskButton{AppId="Microsoft.VisualStudioCode"},new[]{classic},aliases).Length==0,"Shortcut alias overrode a distinct explicit window profile");
        classic.AppId=null;aliases.Add("Another.Process.Profile",new HashSet<string>(StringComparer.OrdinalIgnoreCase){classic.ImagePath});
        Check(TaskbarAccess.ResolveShortcut(new TaskButton{AppId="Microsoft.VisualStudioCode"},new[]{classic},aliases).Length==0,"Ambiguous process-only shortcut profiles were combined");
        var original=new[]{new TaskButton{AppId="group",Bounds=new Rectangle(200,0,48,48)}};
        var shifted=new TaskbarSnapshot{Buttons=new[]{new TaskButton{AppId="Chrome",Bounds=new Rectangle(200,0,48,48)},new TaskButton{AppId="group",Bounds=new Rectangle(152,0,48,48)}}};
        Check(TaskbarAccess.DropTarget(shifted,original,new Point(220,20),"Chrome",id=>id=="group").AppId=="group","Explorer's native reorder lost the accepted group");
        Check(TaskbarAccess.DropTarget(shifted,original,new Point(220,20),"AnotherApp",id=>id=="group")==null,"An unrelated app at the old position was accepted");
        shifted.Buttons[1].Bounds=new Rectangle(0,0,48,48);Check(TaskbarAccess.DropTarget(shifted,original,new Point(220,20),"Chrome",id=>id=="group")==null,"A far-away group was accepted after rearrangement");
        var source=new GroupDragSource{Button=new TaskButton{AppId="Chrome",Bounds=new Rectangle(100,0,48,48)},Windows=new[]{window}};
        var inputMap=new GroupInputMap{Taskbar=new IntPtr(123),Created=100,Groups=original,Sources=new[]{source}};
        var gate=new GroupDragGate();GroupDragGesture begun=null,ended=null;Action<GroupDragGesture> begin=g=>begun=g,end=g=>ended=g;
        Check(!gate.Process(0x201,new Point(220,20),false,false,inputMap.Taskbar,inputMap,101,begin,end),"Dragging the numbered group was intercepted instead of native reorder");
        Check(!gate.Process(0x201,new Point(120,20),true,false,inputMap.Taskbar,inputMap,101,begin,end),"Injected/native replay input was intercepted");
        Check(!gate.Process(0x201,new Point(120,20),false,true,inputMap.Taskbar,inputMap,101,begin,end),"Modified taskbar click was intercepted");
        Check(!gate.Process(0x201,new Point(120,20),false,false,new IntPtr(999),inputMap,101,begin,end),"Overlapping non-taskbar window was intercepted");
        Check(!gate.Process(0x201,new Point(120,20),false,false,inputMap.Taskbar,inputMap,3101,begin,end),"Expired taskbar coordinates were used");
        Check(gate.Process(0x201,new Point(120,20),false,false,inputMap.Taskbar,inputMap,101,begin,end) && begun!=null,"Live-app drag was not owned before Explorer button-down");
        Check(!gate.Process(0x200,new Point(220,20),false,false,inputMap.Taskbar,inputMap,102,begin,end),"Pointer motion was blocked");
        Check(original[0].Bounds==new Rectangle(200,0,48,48),"The original group target moved");
        Check(gate.Process(0x202,new Point(220,20),false,false,inputMap.Taskbar,inputMap,103,begin,end) && ended==begun && ended.Released,"Owned release leaked into Explorer or lost the gesture");
        Check(!gate.Process(0x202,new Point(220,20),false,false,inputMap.Taskbar,inputMap,104,begin,end),"Release was consumed twice");
        begun=null;ended=null;gate.Process(0x201,new Point(120,20),false,false,inputMap.Taskbar,inputMap,105,begin,end);gate.Cancel();
        gate.Process(0x202,new Point(220,20),false,false,inputMap.Taskbar,inputMap,106,begin,end);Check(ended!=null && ended.Cancelled,"Escape/cancel lost cancellation before release");
        begun=null;ended=null;gate.Process(0x201,new Point(120,20),false,false,inputMap.Taskbar,inputMap,107,begin,end);gate.Abort(end);
        Check(ended!=null && ended.Cancelled && ended.Released,"Abandoned drag was not cleared");
        Check(gate.Process(0x201,new Point(120,20),false,false,inputMap.Taskbar,inputMap,108,begin,end),"Abandoned drag blocked the next source press");gate.Abort(end);
        int refreshes=0;gate.Process(0x201,new Point(220,20),false,false,inputMap.Taskbar,inputMap,109,begin,end);
        Check(!gate.Process(0x202,new Point(120,20),false,false,inputMap.Taskbar,inputMap,110,begin,end,delegate{refreshes++;}) && refreshes==1,"Native group reorder did not invalidate old input regions");
        var monitor=new Rectangle(0,0,1920,1080);var bar=new Rectangle(0,1032,1920,48);
        var fallback=GroupLayoutPolicy.AddBounds(bar,Rectangle.Empty,monitor,new Rectangle[0],false);
        Check(!fallback.IsEmpty && fallback.Bottom<bar.Top,"Native taskbar fallback hid '+' or covered unknown app buttons before UIA was ready");
        Check(GroupLayoutPolicy.AddBounds(bar,new Rectangle(0,1032,120,48),monitor,new Rectangle[0],true).Left==128,"Weather-adjacent placement changed");
        var screen=new Rectangle(0,0,1920,1080);Check(!GroupLayoutPolicy.Fullscreen(screen,screen,"Progman"),"Desktop focus hid the add button");
        Check(GroupLayoutPolicy.ShellSurface("Progman") && GroupLayoutPolicy.ShellSurface("WorkerW") && GroupLayoutPolicy.ShellSurface("Shell_TrayWnd") && !GroupLayoutPolicy.ShellSurface("CabinetWClass"),"Explorer taskbar grouping included a desktop/taskbar Shell surface");
        Check(GroupLayoutPolicy.Fullscreen(screen,screen,"Chrome_WidgetWin_1"),"Real full-screen application did not hide the overlay");
        Check(!GroupLayoutPolicy.Fullscreen(new Rectangle(0,0,1920,1032),screen,"Chrome_WidgetWin_1"),"Ordinary maximized app hid the add button");
    }
    private static void Native(){
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
        PopupLifecycle();
        ReplayNativeFixture();
        int clicks=0;
        using(var button=new GroupAddButton(delegate{clicks++;})){
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
            Console.WriteLine("PASS: foreign-process window AppID/icon/relaunch properties; visible live app; persisted guardian restoration; exact unset-value restoration; stale identity rejection.");
        }finally{
            if(recovery!=null){recovery.RestoreAll();Directory.Delete(recovery.Folder,true);}
            if(child!=null){if(!child.HasExited){PostMessageW(hwnd,0x10,IntPtr.Zero,IntPtr.Zero);if(!child.WaitForExit(5000))child.Kill();}child.Dispose();}
        }
    }
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
                menu.Items[1].PerformClick();Check(removed==0,"Group deletion ran inside the ToolStrip click handler");
                menu.Close();PumpUntil(delegate{return removed==1;});
                Check(anchor.IsDisposed && !menu.IsDisposed,"Deleting the anchor disposed its stable-owner popup");
                var next=model.Add();popup.Show(next,owner.PointToScreen(new Point(8,8)));Application.DoEvents();menu.Close();Application.DoEvents();
                Check(!menu.IsDisposed,"Popup could not be reused after deleting a group");
            }
            owner.Close();
        }
        if(!anchor.IsDisposed)anchor.Finish();
        Console.WriteLine("PASS: actual popup show/close repeated 40 times; delete deferred outside click; anchor disposed; stable popup reused; no disposed-menu access.");
    }
    private static void ReplayNativeFixture(){
        Point original=Cursor.Position;
        try{
            using(var form=new Form{Text="DeskPages native replay fixture",StartPosition=FormStartPosition.Manual,Location=new Point(200,200),ClientSize=new Size(320,120),TopMost=true}){
                int downs=0,ups=0;Point lastDown=Point.Empty,lastUp=Point.Empty;
                form.MouseDown+=delegate(object sender,MouseEventArgs e){downs++;lastDown=e.Location;};form.MouseUp+=delegate(object sender,MouseEventArgs e){ups++;lastUp=e.Location;};
                form.Show();form.Activate();Application.DoEvents();
                Point start=form.PointToScreen(new Point(30,30)),finish=form.PointToScreen(new Point(180,30));
                var button=new TaskButton{AppId="Fixture",Bounds=new Rectangle(start.X-10,start.Y-10,40,40)};
                var source=new GroupDragSource{Button=button,Windows=new GroupWindow[0]};
                var snapshot=new TaskbarSnapshot{Handle=form.Handle,Buttons=new[]{button,new TaskButton{AppId="OtherFixture",Bounds=new Rectangle(finish.X-10,finish.Y-10,40,40)}}};
                Cursor.Position=start;Application.DoEvents();
                Check(GroupDragInput.Replay(new GroupDragGesture{Source=source,Press=start,Release=start,Taskbar=form.Handle},snapshot),"Native click replay batch was rejected");
                PumpUntil(delegate{return ups==1;});Check(downs==1 && ups==1,"Native replay did not deliver exactly one press/release");
                Cursor.Position=finish;Application.DoEvents();
                Check(GroupDragInput.Replay(new GroupDragGesture{Source=source,Press=start,Release=finish,Taskbar=form.Handle},snapshot),"Native drag replay batch was rejected");
                PumpUntil(delegate{return ups==2;});Check(downs==2 && ups==2 && Math.Abs(lastDown.X-30)<=1 && Math.Abs(lastUp.X-180)<=1,"Native drag replay changed its source/release coordinates");
                form.Close();
            }
            Console.WriteLine("PASS: actual SendInput click/drag batches deliver one down/up and original coordinates to an owned fixture window; not a Windows 11 taskbar gesture test.");
        }finally{Cursor.Position=original;}
    }
    private static void PumpUntil(Func<bool> complete){var watch=Stopwatch.StartNew();while(!complete() && watch.ElapsedMilliseconds<2000){Application.DoEvents();System.Threading.Thread.Sleep(5);}Check(complete(),"Native input fixture timed out");}
    [DllImport("user32.dll")]private static extern IntPtr SendMessageW(IntPtr h,uint message,IntPtr w,IntPtr l);
    [DllImport("user32.dll")]private static extern bool ShowWindow(IntPtr h,int command);
    [DllImport("user32.dll")]private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd,int index);
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern bool PostMessageW(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
}
