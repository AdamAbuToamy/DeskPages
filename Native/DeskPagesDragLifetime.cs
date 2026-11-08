using System;
using System.Diagnostics;
using System.IO;

// A stale heartbeat is not evidence that a process died (sleep, a busy STA or
// access restrictions can all delay it). Only a verified dead/reused PID may
// trigger recovery. Unknown liveness must never replace a live drag operation.
public static class DeskPagesDragLifetime {
    public static bool? Alive(int pid,long started){
        try{using(var process=Process.GetProcessById(pid))return !process.HasExited && process.StartTime.ToUniversalTime().Ticks==started;}
        catch(ArgumentException){return false;}
        catch(InvalidOperationException){return false;}
        catch(System.ComponentModel.Win32Exception){return null;}
    }
    public static bool? ReadPeer(string path){
        int pid;long started;return TryIdentity(path,out pid,out started)?Alive(pid,started):null;
    }
    public static bool TryIdentity(string path,out int pid,out long started){
        pid=0;started=0;
        try{string[] lines;return DeskPagesDragProtocol.TryReadLines(path,out lines) && lines.Length==2 && int.TryParse(lines[0],out pid) && pid>0 && long.TryParse(lines[1],out started) && started>0;}
        catch(UnauthorizedAccessException){return false;}
    }
    public static string Identity(){using(var process=Process.GetCurrentProcess())return process.Id+"\n"+process.StartTime.ToUniversalTime().Ticks;}
}

public sealed class DeskPagesDragSupervisor {
    private readonly string peerPath;
    private readonly long startupUntil;
    private long nextCheck,deadSince=-1;
    private int peerPid;private long peerStarted;
    public DeskPagesDragSupervisor(string token,long now){peerPath=Path.Combine(DeskPagesDragProtocol.SessionPath(token),"broker-owner.txt");startupUntil=now+15000;}
    public bool ShouldRestart(long now,bool operationPending,bool buttonHeld){
        if(now<nextCheck)return false;nextCheck=now+1000;
        int pid;long started;
        if(DeskPagesDragLifetime.TryIdentity(peerPath,out pid,out started)){peerPid=pid;peerStarted=started;}
        return Observe(now,peerPid>0?DeskPagesDragLifetime.Alive(peerPid,peerStarted):null,operationPending,buttonHeld);
    }
    public bool Observe(long now,bool? alive,bool operationPending,bool buttonHeld){
        if(alive!=false){deadSince=-1;return false;}
        if(deadSince<0){deadSince=now;return false;}
        // Allow the host to drain the dead peer's final packet exactly once.
        if(now<startupUntil || now-deadSince<2000 || operationPending || buttonHeld)return false;
        deadSince=now;nextCheck=now+15000;return true;
    }
}
