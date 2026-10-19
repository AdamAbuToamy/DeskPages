using System.Threading;

// Input hooks only mark generations. Explorer/PowerShell calls stay on the STA.
public sealed class DeskPagesCheckpointPolicy
{
    private long generation,saved,lastChange,nextAttempt;
    public long Generation {get{return Interlocked.Read(ref generation);}}
    public void Changed(long now){Interlocked.Exchange(ref lastChange,now);Interlocked.Increment(ref generation);}
    public bool Due(long now){return Generation>Interlocked.Read(ref saved) && now-Interlocked.Read(ref lastChange)>=120 && now>=Interlocked.Read(ref nextAttempt);}
    public void Completed(long captured,bool success,long now){
        if(success)Interlocked.Exchange(ref saved,captured);
        Interlocked.Exchange(ref nextAttempt,success?0:now+500);
    }
}
