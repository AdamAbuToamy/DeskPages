using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

internal static class TaskbarVisibility {
    public static bool Exposed(bool visible,Rectangle bar,Rectangle monitor,bool hit){
        if(!visible || bar.IsEmpty || !hit)return false;
        var shown=Rectangle.Intersect(bar,monitor);
        return !shown.IsEmpty && (bar.Width>=bar.Height?shown.Height:shown.Width)>=8;
    }
    public static bool Read(IntPtr bar,Rectangle bounds,Rectangle monitor){return Read(bar,bounds,monitor,ShellOverlay);}
    internal static bool Read(IntPtr bar,Rectangle bounds,Rectangle monitor,Func<IntPtr,bool> shellOverlay){
        if(bar==IntPtr.Zero || !WindowShell.IsWindowVisible(bar) || bounds.IsEmpty)return false;
        var shown=Rectangle.Intersect(bounds,monitor);if(shown.IsEmpty)return false;
        // IsWindowVisible stays true when a fullscreen window covers Explorer.
        // Start/Search use monitor-sized transparent Shell backdrops. Those hits
        // do not mean Explorer hid the bar; only trusted Shell surfaces qualify.
        // Ordinary fullscreen applications must still suppress the button.
        bool hit=false;bool horizontal=bounds.Width>=bounds.Height;
        foreach(double fraction in new[]{0.05,0.35,0.65,0.95}){
            var point=horizontal?new Point(shown.Left+(int)(shown.Width*fraction),shown.Top+shown.Height/2):new Point(shown.Left+shown.Width/2,shown.Top+(int)(shown.Height*fraction));
            var window=WindowShell.WindowFromPoint(point);
            if(window==bar || IsChild(bar,window) || WindowShell.GetAncestor(window,2)==bar || WindowShell.GetAncestor(window,3)==bar || shellOverlay(WindowShell.GetAncestor(window,2))){hit=true;break;}
        }
        return Exposed(true,bounds,monitor,hit);
    }
    private static IntPtr cachedWindow;private static uint cachedPid;private static long cachedUntil;private static bool cachedOverlay;
    private static readonly Stopwatch clock=Stopwatch.StartNew();
    private static bool ShellOverlay(IntPtr window){
        if(window==IntPtr.Zero)return false;
        uint pid;WindowShell.GetWindowThreadProcessId(window,out pid);
        long now=clock.ElapsedMilliseconds;
        if(window==cachedWindow && pid==cachedPid && now<cachedUntil)return cachedOverlay;
        bool overlay=false;
        try{overlay=ShellOverlay(WindowShell.Class(window),WindowShell.ProcessPath(pid),Environment.GetFolderPath(Environment.SpecialFolder.Windows));}
        catch(System.ComponentModel.Win32Exception){}
        cachedWindow=window;cachedPid=pid;cachedUntil=now+2000;cachedOverlay=overlay;return overlay;
    }
    internal static bool ShellOverlay(string windowClass,string image,string windows){
        if(string.IsNullOrEmpty(image) || string.IsNullOrEmpty(windows))return false;
        string root=windows.TrimEnd('\\','/')+"\\";
        if(string.Equals(image,root+"explorer.exe",StringComparison.OrdinalIgnoreCase))
            return windowClass=="XamlExplorerHostIslandWindow" || windowClass=="Windows.UI.Composition.DesktopWindowContentBridge" || windowClass=="ControlCenterWindow";
        if(!image.StartsWith(root+"SystemApps\\",StringComparison.OrdinalIgnoreCase))return false;
        string name=Path.GetFileName(image);
        return string.Equals(name,"StartMenuExperienceHost.exe",StringComparison.OrdinalIgnoreCase) || string.Equals(name,"SearchHost.exe",StringComparison.OrdinalIgnoreCase) || string.Equals(name,"SearchApp.exe",StringComparison.OrdinalIgnoreCase) || string.Equals(name,"SearchUI.exe",StringComparison.OrdinalIgnoreCase) || string.Equals(name,"ShellExperienceHost.exe",StringComparison.OrdinalIgnoreCase);
    }
    [DllImport("user32.dll")]static extern bool IsChild(IntPtr parent,IntPtr child);
}
