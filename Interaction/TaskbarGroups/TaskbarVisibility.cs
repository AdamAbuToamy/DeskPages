using System;
using System.Drawing;
using System.Runtime.InteropServices;

internal static class TaskbarVisibility {
    public static bool Exposed(bool visible,Rectangle bar,Rectangle monitor,bool hit){
        if(!visible || bar.IsEmpty || !hit)return false;
        var shown=Rectangle.Intersect(bar,monitor);
        return !shown.IsEmpty && (bar.Width>=bar.Height?shown.Height:shown.Width)>=8;
    }
    public static bool Read(IntPtr bar,Rectangle bounds,Rectangle monitor){
        if(bar==IntPtr.Zero || !WindowShell.IsWindowVisible(bar) || bounds.IsEmpty)return false;
        var shown=Rectangle.Intersect(bounds,monitor);if(shown.IsEmpty)return false;
        // IsWindowVisible stays true when a fullscreen window covers Explorer.
        // Probe several native taskbar pixels, avoiding dependence on UIA/focus.
        bool hit=false;bool horizontal=bounds.Width>=bounds.Height;
        foreach(double fraction in new[]{0.05,0.35,0.65,0.95}){
            var point=horizontal?new Point(shown.Left+(int)(shown.Width*fraction),shown.Top+shown.Height/2):new Point(shown.Left+shown.Width/2,shown.Top+(int)(shown.Height*fraction));
            var window=WindowShell.WindowFromPoint(point);
            if(window==bar || IsChild(bar,window) || WindowShell.GetAncestor(window,2)==bar || WindowShell.GetAncestor(window,3)==bar){hit=true;break;}
        }
        return Exposed(true,bounds,monitor,hit);
    }
    [DllImport("user32.dll")]static extern bool IsChild(IntPtr parent,IntPtr child);
}
