using System;
using System.Collections.Concurrent;
using System.Threading;

public sealed class DeskPagesInputCommand {
    public int Code;public bool ExplicitUi;public long ReceivedAt;
    public bool Navigation {get{return Code==-1 || Code==1 || (Code>1000 && Code<=1099);}}
}
public sealed class DeskPagesCommandQueue {
    readonly ConcurrentQueue<DeskPagesInputCommand> queue=new ConcurrentQueue<DeskPagesInputCommand>();
    public bool IsEmpty {get{return queue.IsEmpty;}}
    public bool Enqueue(int code,bool ui,long received){
        if(queue.Count>=3)return false;
        queue.Enqueue(new DeskPagesInputCommand{Code=code,ExplicitUi=ui,ReceivedAt=received});return true;
    }
    public bool TryDequeue(out DeskPagesInputCommand command){return queue.TryDequeue(out command);}
}
// Raise only the current dispatch thread and restore even after a failed switch.
// The process, renderer, input hooks and background workers retain their priorities.
public sealed class DeskPagesNavigationPriority:IDisposable {
    readonly Func<ThreadPriority> get;readonly Action<ThreadPriority> set;readonly ThreadPriority original;bool changed;
    public DeskPagesNavigationPriority(bool navigation):this(navigation,()=>Thread.CurrentThread.Priority,p=>Thread.CurrentThread.Priority=p){}
    public DeskPagesNavigationPriority(bool navigation,Func<ThreadPriority> read,Action<ThreadPriority> write){
        get=read;set=write;if(!navigation)return;
        try{original=get();if(original<ThreadPriority.AboveNormal){set(ThreadPriority.AboveNormal);changed=true;}}catch(Exception){}
    }
    public void Dispose(){if(!changed)return;changed=false;try{set(original);}catch(Exception){}}
}
