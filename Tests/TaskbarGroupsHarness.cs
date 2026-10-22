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
        var screen=new Rectangle(0,0,1920,1080);Check(!GroupLayoutPolicy.Fullscreen(screen,screen,"Progman"),"Desktop focus hid the add button");
        Check(GroupLayoutPolicy.ShellSurface("Progman") && GroupLayoutPolicy.ShellSurface("WorkerW") && GroupLayoutPolicy.ShellSurface("Shell_TrayWnd") && !GroupLayoutPolicy.ShellSurface("CabinetWClass"),"Explorer taskbar grouping included a desktop/taskbar Shell surface");
        Check(GroupLayoutPolicy.Fullscreen(screen,screen,"Chrome_WidgetWin_1"),"Real full-screen application did not hide the overlay");
        Check(!GroupLayoutPolicy.Fullscreen(new Rectangle(0,0,1920,1032),screen,"Chrome_WidgetWin_1"),"Ordinary maximized app hid the add button");
    }
    private static void Native(){
        var group=new GroupModel(Guid.NewGuid().ToString("N")).Add();
        using(var anchor=new GroupAnchor(group,delegate{}))using(var taskbar=new GroupTaskbar()){
            WindowShell.Set(anchor.Handle,5,group.AppId);
            Check(WindowShell.Get(anchor.Handle,5)==group.AppId,"Group identity was not set before first show");
            anchor.Show();Application.DoEvents();taskbar.Add(anchor.Handle);
            long style=GetWindowLongPtrW(anchor.Handle,-20).ToInt64();
            Check(anchor.ShowInTaskbar && anchor.Owner==null && (style&0x40000)!=0 && (style&0x08000080)==0,"Numbered group is not eligible for a native taskbar button");
            Check(WindowShell.IsWindowVisible(anchor.Handle) && IsIconic(anchor.Handle),"Empty group must remain visible to Explorer and minimized");
            taskbar.Remove(anchor.Handle);anchor.Finish();
        }
        using(var normal=GroupAddButton.Surface(36,36,0,false))using(var pressed=GroupAddButton.Surface(36,36,1,true)){
            var background=normal.GetPixel(8,8);Check(background.A>0 && background.A<255 && background.R>=253 && background.R==background.G && background.G==background.B,"Add background is not translucent white: "+background);
            Check(normal.GetPixel(18,18).A==255,"Plus glyph lost its opaque contrast");
            Check(normal.GetPixel(2,18)!=pressed.GetPixel(2,18),"Press animation did not change the button shape");
        }
        int clicks=0;
        using(var button=new GroupAddButton(delegate{clicks++;})){
            button.Show();Application.DoEvents();
            var down=typeof(GroupAddButton).GetMethod("OnMouseDown",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            var up=typeof(GroupAddButton).GetMethod("OnMouseUp",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            var inside=new MouseEventArgs(MouseButtons.Left,1,16,16,0);var outside=new MouseEventArgs(MouseButtons.Left,1,-10,-10,0);
            up.Invoke(button,new object[]{inside});Check(clicks==0,"Unarmed mouse-up added a group");
            down.Invoke(button,new object[]{inside});up.Invoke(button,new object[]{outside});Check(clicks==0,"Release outside added a group");
            down.Invoke(button,new object[]{inside});up.Invoke(button,new object[]{inside});Check(clicks==1,"Button press did not add exactly one group");
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
    [DllImport("user32.dll")]private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd,int index);
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern bool PostMessageW(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
}
