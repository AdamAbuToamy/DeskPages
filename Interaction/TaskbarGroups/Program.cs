using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;
[assembly:AssemblyTitle("DeskPages open taskbar groups")]
[assembly:AssemblyProduct("DeskPages")]
[assembly:AssemblyCompany("Adam Abu Toamy")]
[assembly:AssemblyVersion("0.8.37.0")]
[assembly:AssemblyFileVersion("0.8.37.0")]
internal static class TaskbarGroupsProgram {
    [STAThread]private static int Main(string[] args){
        try{
            if(new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))return 3;
            if(args.Length==4 && args[0]=="--watch")return GroupRecovery.Watch(args[1],int.Parse(args[2],CultureInfo.InvariantCulture),long.Parse(args[3],CultureInfo.InvariantCulture));
            if(args.Length==3 && args[0]=="--group"){
                Guid token;int number;if(!Guid.TryParseExact(args[1],"N",out token) || !int.TryParse(args[2],out number) || number<1 || number>99)return 2;
                IntPtr anchor=WindowShell.FindWindowW(null,"DeskPages · "+number.ToString("00"));if(anchor!=IntPtr.Zero && WindowShell.Get(anchor,5)=="DeskPages.LiveGroups."+args[1]+"."+number)PostMessageW(anchor,0x8031,IntPtr.Zero,IntPtr.Zero);return 0;
            }
            bool create=args.Length==1 && args[0]=="--add";
            if(args.Length!=0 && !create)return 2;
            using(var request=new GroupCreateSignal()){
            if(create)request.Request();
            bool fresh;using(var mutex=new Mutex(true,"Local\\DeskPages.TaskbarGroups."+WindowsIdentity.GetCurrent().User.Value,out fresh)){
                if(!fresh)return 0;GroupRecovery.RecoverOrphans();try{SetThreadDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){ }
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                // Idle phase keeps the shortcut alive without UI Automation, drag hooks,
                // taskbar scans, 40 ms polling or a second guardian process.
                if(!create){var idle=new GroupIdleContext(request);Application.Run(idle);if(!idle.Activated)return 0;request.Request();}
                Application.Run(new GroupController(request));
            }}return 0;
        }catch(Exception ex){MessageBox.Show(ex.Message,"DeskPages taskbar groups");return 1;}
    }
    [DllImport("user32.dll")]private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")]private static extern bool PostMessageW(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
}

internal sealed class GroupIdleContext:ApplicationContext {
    private readonly GroupCreateSignal request;
    private readonly GroupCommandWindow commands;
    private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=300};
    private readonly EventWaitHandle stop;
    internal bool Activated{get;private set;}
    internal GroupIdleContext(GroupCreateSignal signal){
        request=signal;
        stop=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\DeskPagesTaskbarGroupsStop-"+WindowsIdentity.GetCurrent().User.Value);
        stop.Reset();
        commands=new GroupCommandWindow(Activate);
        var handle=commands.Handle; // Register Win+Alt+Plus without starting GroupController.
        timer.Tick+=delegate{if(stop.WaitOne(0)){ExitThread();return;}if(request.Take())Activate();};
        timer.Start();
    }
    private void Activate(){if(Activated)return;Activated=true;ExitThread();}
    protected override void ExitThreadCore(){
        timer.Stop();timer.Dispose();commands.Dispose();stop.Dispose();base.ExitThreadCore();
    }
}
