using System;
using System.Runtime.InteropServices;
using System.Threading;

internal static class GroupRefreshPolicy {
    public static bool Due(long now,long previous,bool changed,bool onTaskbar,bool dragging){
        int interval=dragging?80:changed?(onTaskbar?150:1000):onTaskbar?1000:2000;
        return now-previous>=interval;
    }
    public static bool ResolveWindows(long now,long previous,bool interacting,bool changed){return interacting && (changed || now-previous>=10000);}
}
// Callbacks only mark a revision. UI Automation and window enumeration remain
// outside the hook, coalesced and rate-limited by GroupRefreshPolicy.
internal sealed class GroupWindowEvents:IDisposable {
    delegate void Callback(IntPtr hook,uint evt,IntPtr window,int obj,int child,uint thread,uint time);
    readonly Callback callback;IntPtr hook;long revision=1;
    public long Revision{get{return Interlocked.Read(ref revision);}}
    public GroupWindowEvents(){callback=Changed;hook=SetWinEventHook(0x8000,0x800c,IntPtr.Zero,callback,0,0,2);}
    void Changed(IntPtr h,uint evt,IntPtr window,int obj,int child,uint thread,uint time){
        if(window==IntPtr.Zero || obj!=0 || child!=0)return;
        if(evt==0x8000 || evt==0x8001 || evt==0x8002 || evt==0x8003 || evt==0x800c)Interlocked.Increment(ref revision);
    }
    public void Dispose(){if(hook!=IntPtr.Zero){UnhookWinEvent(hook);hook=IntPtr.Zero;}}
    [DllImport("user32.dll")]static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr dll,Callback callback,uint pid,uint tid,uint flags);
    [DllImport("user32.dll")]static extern bool UnhookWinEvent(IntPtr hook);
}
