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
    }
    private static void Native(){
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
    [DllImport("user32.dll")]private static extern bool PostMessageW(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
}
