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
[assembly:AssemblyVersion("0.8.23.0")]
[assembly:AssemblyFileVersion("0.8.23.0")]
internal static class TaskbarGroupsProgram {
    [STAThread]private static int Main(string[] args){
        try{
            if(new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))return 3;
            if(args.Length==4 && args[0]=="--watch")return GroupRecovery.Watch(args[1],int.Parse(args[2],CultureInfo.InvariantCulture),long.Parse(args[3],CultureInfo.InvariantCulture));
            if(args.Length==3 && args[0]=="--group"){
                Guid token;int number;if(!Guid.TryParseExact(args[1],"N",out token) || !int.TryParse(args[2],out number) || number<1 || number>99)return 2;
                IntPtr anchor=WindowShell.FindWindowW(null,"DeskPages · "+number.ToString("00"));if(anchor!=IntPtr.Zero && WindowShell.Get(anchor,5)=="DeskPages.LiveGroups."+args[1]+"."+number)PostMessageW(anchor,0x8031,IntPtr.Zero,IntPtr.Zero);return 0;
            }
            if(args.Length!=0)return 2;
            bool fresh;using(var mutex=new Mutex(true,"Local\\DeskPages.TaskbarGroups."+WindowsIdentity.GetCurrent().User.Value,out fresh)){
                if(!fresh)return 0;GroupRecovery.RecoverOrphans();try{SetThreadDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){ }
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new GroupController());
            }return 0;
        }catch(Exception ex){MessageBox.Show(ex.Message,"DeskPages taskbar groups");return 1;}
    }
    [DllImport("user32.dll")]private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")]private static extern bool PostMessageW(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
}
