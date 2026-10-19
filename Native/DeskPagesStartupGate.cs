using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;

// Shared by the startup guardian and the in-session agent. This owns only the
// temporary global icon-visibility setting, never file membership or layouts.
public static class DeskPagesStartupGate
{
    [StructLayout(LayoutKind.Sequential)] private struct ShellState {
        public uint Flags,Unused1,Unused2;public int Sort,Direction;public uint Version,Unused3,Flags2;
    }
    [DllImport("shell32.dll")] private static extern void SHGetSetSettings(ref ShellState state,uint mask,[MarshalAs(UnmanagedType.Bool)] bool set);
    private delegate bool EnumCallback(IntPtr hwnd,IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr FindWindowExW(IntPtr parent,IntPtr after,string className,string title);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr SendMessageTimeoutW(IntPtr hwnd,uint message,IntPtr wp,string lp,uint flags,uint timeout,out IntPtr result);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hwnd,int command);
    [DllImport("user32.dll",EntryPoint="SendMessageTimeoutW")] private static extern IntPtr ReadMessage(IntPtr hwnd,uint message,IntPtr wp,IntPtr lp,uint flags,uint timeout,out IntPtr result);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr hwnd,IntPtr update,IntPtr region,uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    internal interface IPlatform {string Store{get;}string Account{get;}bool GetHidden();void PutHidden(bool hidden);bool CanArm();}
    private sealed class WindowsPlatform:IPlatform {
        public string Store{get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPages","HiddenPages");}}
        public string Account{get{return WindowsIdentity.GetCurrent().User.Value;}}
        public bool GetHidden(){return NativeHidden;}
        public void PutHidden(bool hidden){SetNativeHidden(hidden);}
        public bool CanArm(){
            if(!File.Exists(EnabledPath))return false;
            string guardian=File.ReadAllText(EnabledPath).Trim();
            return DeskPagesInstallLocation.IsProtectedGuardian(guardian);
        }
    }
    internal static IPlatform Platform=new WindowsPlatform();
    public static string Store {get{return Platform.Store;}}
    public static string MarkerPath {get{return Path.Combine(Store,"startup-gate.txt");}}
    public static string ReadyPath {get{return Path.Combine(Store,"startup-ready.txt");}}
    public static string EnabledPath {get{return Path.Combine(Store,"startup-enabled.txt");}}
    public sealed class Lease {
        public string Token,Phase;public bool OriginalHidden;public int Owner;public long Started;
    }
    private sealed class GateLock:IDisposable {
        private readonly Mutex mutex;
        public GateLock(){
            mutex=new Mutex(false,"Local\\DeskPages-IconGate-"+Platform.Account);
            bool owned=false;try{owned=mutex.WaitOne(1500);}catch(AbandonedMutexException){owned=true;}
            if(!owned){mutex.Dispose();throw new TimeoutException("Startup gate is busy.");}
        }
        public void Dispose(){mutex.ReleaseMutex();mutex.Dispose();}
    }
    private static bool NativeHidden {
        get {var state=new ShellState();SHGetSetSettings(ref state,0x4000,false);return (state.Flags&0x1000)!=0;}
    }
    private static void SetNativeHidden(bool hidden) {
        var state=new ShellState();state.Flags=hidden?0x1000U:0;
        SHGetSetSettings(ref state,0x4000,true);
        // Keep Explorer's persisted Advanced preference in agreement with the
        // documented shell option, including before the next Explorer starts.
        using(var key=Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced")){
            key.SetValue("HideIcons",hidden?1:0,RegistryValueKind.DWord);key.Flush();
        }
        if(NativeHidden!=hidden)throw new InvalidOperationException("Windows did not accept the desktop icon visibility setting.");
        // Notify existing desktop views as well as persisting the preference.
        // At logon this occurs behind the guardian's wallpaper-only cover.
        EnumWindows(delegate(IntPtr root,IntPtr unused){
            IntPtr view=FindWindowExW(root,IntPtr.Zero,"SHELLDLL_DefView",null);
            if(view!=IntPtr.Zero){
                IntPtr result;
                SendMessageTimeoutW(root,0x1a,IntPtr.Zero,"ShellState",2,100,out result);
                SendMessageTimeoutW(view,0x1a,IntPtr.Zero,"ShellState",2,100,out result);
                ShowWindowAsync(view,hidden?0:8);
                IntPtr icons=FindWindowExW(view,IntPtr.Zero,"SysListView32",null);
                if(icons!=IntPtr.Zero)ShowWindowAsync(icons,hidden?0:8);
            }
            return true;
        },IntPtr.Zero);
    }
    public static bool Hidden{get{return Platform.GetHidden();}}
    public static void SetHidden(bool hidden){Platform.PutHidden(hidden);}
    public static void PaintOriginalView(int expectedCount){
        IntPtr icons=IntPtr.Zero;
        EnumWindows(delegate(IntPtr root,IntPtr unused){
            IntPtr view=FindWindowExW(root,IntPtr.Zero,"SHELLDLL_DefView",null);
            if(view!=IntPtr.Zero)icons=FindWindowExW(view,IntPtr.Zero,"SysListView32",null);
            return icons==IntPtr.Zero;
        },IntPtr.Zero);
        IntPtr count;
        if(icons==IntPtr.Zero || ReadMessage(icons,0x1004,IntPtr.Zero,IntPtr.Zero,2,400,out count)==IntPtr.Zero || count.ToInt64()!=expectedCount)
            throw new InvalidOperationException("Startup icon view does not match the restored page.");
        if(!IsWindowVisible(icons))throw new InvalidOperationException("Windows has not revealed the original icon view behind the startup cover.");
        if(!RedrawWindow(icons,IntPtr.Zero,IntPtr.Zero,0x1|0x4|0x80|0x100))throw new InvalidOperationException("Startup desktop paint failed.");
        Marshal.ThrowExceptionForHR(DwmFlush());
    }
    public static void AtomicText(string path,string contents) {
        Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                byte[] bytes=System.Text.Encoding.UTF8.GetBytes(contents);stream.Write(bytes,0,bytes.Length);stream.Flush(true);
            }
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static Lease Read() {
        string text;
        try{using(var stream=new FileStream(MarkerPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(stream))text=reader.ReadToEnd();}
        catch(FileNotFoundException){return null;}catch(DirectoryNotFoundException){return null;}
        string[] lines=text.Split('\n');Guid id;int owner;long started;bool original;
        if(lines.Length!=6 || lines[0]!="DeskPagesGate1" || !Guid.TryParseExact(lines[1],"N",out id) ||
           !bool.TryParse(lines[2],out original) || !int.TryParse(lines[3],out owner) ||
           !long.TryParse(lines[4],out started) || (lines[5]!="next-logon" && lines[5]!="restoring"))
            throw new InvalidDataException("Invalid startup guard ownership record; manual recovery may be needed.");
        return new Lease{Token=lines[1],OriginalHidden=original,Owner=owner,Started=started,Phase=lines[5]};
    }
    public static void Write(Lease lease){AtomicText(MarkerPath,"DeskPagesGate1\n"+lease.Token+"\n"+lease.OriginalHidden+"\n"+lease.Owner+"\n"+lease.Started+"\n"+lease.Phase);}
    public static Lease BeginRestore() {
        using(new GateLock()){
        var old=Read();
        // Do not claim a pre-existing user choice to hide every desktop icon.
        if(old==null && Hidden)return null;
        var process=Process.GetCurrentProcess();
        var lease=new Lease{Token=Guid.NewGuid().ToString("N"),OriginalHidden=old!=null?old.OriginalHidden:false,
            Owner=process.Id,Started=process.StartTime.ToUniversalTime().Ticks,Phase="restoring"};
        Write(lease);return lease;}
    }
    public static void SetForLease(string token,bool hidden){using(new GateLock()){
        var lease=Read();if(lease==null || lease.Token!=token)throw new InvalidOperationException("Startup gate lease expired.");SetHidden(hidden);
    }}
    public static string ArmForNextLogon() {
        using(new GateLock()){
        if(!Platform.CanArm())return null;
        var old=Read();if(old==null && Hidden)return null;
        var lease=new Lease{Token=Guid.NewGuid().ToString("N"),OriginalHidden=old!=null?old.OriginalHidden:false,Owner=0,Started=0,Phase="next-logon"};
        Write(lease); // Ownership must reach disk before changing the shell.
        try{SetHidden(true);Log("next-logon icon gate armed");}
        catch{Release(lease.Token);throw;}
        return lease.Token;}
    }
    public static bool IsOwnerAlive(Lease lease) {
        if(lease.Owner<=0)return false;
        try {using(var p=Process.GetProcessById(lease.Owner))return !p.HasExited && p.StartTime.ToUniversalTime().Ticks==lease.Started;}
        catch{return false;}
    }
    public static void SignalReady(string token) {
        if(string.IsNullOrEmpty(token))return;
        var lease=Read();if(lease!=null && lease.Token==token && lease.Phase=="restoring")AtomicText(ReadyPath,token);
    }
    public static bool Ready(string token){try{return File.Exists(ReadyPath) && File.ReadAllText(ReadyPath).Trim()==token;}catch(IOException){return false;}}
    public static bool Release(string token) {
        using(new GateLock()){
        var lease=Read();if(lease==null || lease.Token!=token)return false;
        SetHidden(lease.OriginalHidden);
        // A newer shutdown/startup owner must never be removed by an old guard.
        var current=Read();if(current!=null && current.Token==token){File.Delete(MarkerPath);if(File.Exists(ReadyPath))File.Delete(ReadyPath);}
        return true;}
    }
    public static void RecoverOwnedGate(){var lease=Read();if(lease!=null)Release(lease.Token);}
    public static void Log(string message){try{Directory.CreateDirectory(Store);File.AppendAllText(Path.Combine(Store,"agent.log"),DateTime.UtcNow.ToString("o")+" startup: "+message+Environment.NewLine);}catch{}}
}
