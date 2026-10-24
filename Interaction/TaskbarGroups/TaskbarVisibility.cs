using System;
using System.Drawing;
using System.Runtime.InteropServices;

internal static class TaskbarVisibility {
    public static bool Exposed(bool visible,Rectangle bar,Rectangle monitor){
        if(!visible || bar.IsEmpty)return false;
        var shown=Rectangle.Intersect(bar,monitor);
        return !shown.IsEmpty && (bar.Width>=bar.Height?shown.Height:shown.Width)>=8;
    }
    public static bool Read(IntPtr bar,Rectangle bounds,Rectangle monitor){
        // Start/Search backdrops intercept hit tests even while Explorer draws
        // the bar. Visibility comes only from the bar, never from another app.
        return bar!=IntPtr.Zero && Exposed(WindowShell.IsWindowVisible(bar),bounds,monitor);
    }
    public static bool Follow(IntPtr button,IntPtr bar){
        if(button==IntPtr.Zero || bar==IntPtr.Zero)return false;
        // Share Explorer's Z-order band and sit immediately above the bar.
        // Fullscreen windows covering Explorer then cover the button as well;
        // revealing Explorer reveals both, without classifying any application.
        bool topmost=Topmost(bar);
        const uint flags=0x0213; // NOACTIVATE | NOOWNERZORDER | NOMOVE | NOSIZE
        if(Topmost(button)!=topmost && !SetWindowPos(button,new IntPtr(topmost?-1:-2),0,0,0,0,flags))return false;
        var previous=GetWindow(bar,3); // GW_HWNDPREV
        if(previous==button)return true;
        if(previous==IntPtr.Zero || (!topmost && Topmost(previous)))previous=new IntPtr(topmost?-1:0);
        return SetWindowPos(button,previous,0,0,0,0,flags);
    }
    private static bool Topmost(IntPtr window){return (GetWindowLongPtrW(window,-20).ToInt64()&8)!=0;}
    [DllImport("user32.dll")]private static extern IntPtr GetWindow(IntPtr hwnd,uint command);
    [DllImport("user32.dll")]private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd,int index);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
}
