using System;

public interface IDeskPagesDragImageSink {
    void Enter(IntPtr target,object data,int x,int y,uint effect);
    void Over(int x,int y,uint effect);
    void Leave();
    void Drop(object data,int x,int y,uint effect);
}
// OLE identity stays with the original data object. No screenshot or simulated cursor.
public sealed class DeskPagesDragImageSession {
    private readonly IDeskPagesDragImageSink sink;
    private readonly Action<string> log;
    private IntPtr owner;
    public DeskPagesDragImageSession(IDeskPagesDragImageSink api,Action<string> trace){sink=api;log=trace;}
    public void Enter(IntPtr target,object data,int x,int y,uint effect){
        if(target==IntPtr.Zero || data==null)return;
        if(owner!=IntPtr.Zero)Cancel();
        try{sink.Enter(target,data,x,y,effect);owner=target;}catch(Exception ex){owner=IntPtr.Zero;log("drag image enter unavailable: "+ex.Message);}
    }
    public void Over(IntPtr target,int x,int y,uint effect){if(owner!=target || owner==IntPtr.Zero)return;try{sink.Over(x,y,effect);}catch(Exception ex){log("drag image over unavailable: "+ex.Message);}}
    public void Leave(IntPtr target){if(owner==target)Cancel();}
    public void Drop(IntPtr target,object data,int x,int y){
        if(owner!=target || owner==IntPtr.Zero)return;
        owner=IntPtr.Zero;
        // Final NONE must be preserved: this is an icon membership operation, not a filesystem move.
        try{sink.Drop(data,x,y,0);}catch(Exception ex){log("drag image completion unavailable: "+ex.Message);}
    }
    public void Cancel(){if(owner==IntPtr.Zero)return;owner=IntPtr.Zero;try{sink.Leave();}catch(Exception ex){log("drag image cleanup unavailable: "+ex.Message);}}
}
