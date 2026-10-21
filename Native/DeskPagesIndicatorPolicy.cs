using System;

// Display-only lifecycle. Pending has no deadline: only a real completion or
// failure may retire accepted navigation feedback. No page/file operations.
public sealed class DeskPagesIndicatorPolicy {
    public const double MaximumOpacity=0.72;
    private long started=-1;
    private int phase; // 0 hidden, 1 legacy notice, 2 pending, 3 settled, 4 failed
    public bool Pending {get{return phase==2;}}
    public void Start(long now){started=now;phase=1;}
    public void Begin(long now){started=now;phase=2;}
    public void Complete(long now){if(phase==2)Cancel();}
    public void Fail(long now){if(phase==2){started=now;phase=4;}}
    public void Cancel(){started=-1;phase=0;}
    public double Progress(long now){return started<0?0:((Math.Max(0,now-started)%1100)/1100.0);}
    public double Opacity(long now){
        if(started<0 || now<started)return 0;
        long elapsed=now-started;
        if(phase==2)return MaximumOpacity;
        if(phase==3 || phase==4){long hold=phase==3?300:800;return elapsed<hold?MaximumOpacity:elapsed>=hold+200?0:MaximumOpacity*(1-Ease((elapsed-hold)/200.0));}
        if(elapsed>=1150)return 0;
        if(elapsed<120)return MaximumOpacity*Ease(elapsed/120.0);
        if(elapsed<850)return MaximumOpacity;
        return MaximumOpacity*(1-Ease((elapsed-850)/300.0));
    }
    private static double Ease(double value){return value*value*(3-2*value);}
}

// The renderer can retire before the transaction returns (no animation/fallback).
// Completion requires BOTH the successful transaction and a retired renderer.
public sealed class DeskPagesFeedbackLifecycle {
    public long Token{get;private set;}
    public bool Active{get{return Token>0;}}
    private bool committed;
    public void Begin(long token){Token=token;committed=false;}
    public void Commit(){if(Active)committed=true;}
    public void Cancel(){Token=0;committed=false;}
    public long TakeCompletion(bool rendererVisible){if(!Active || !committed || rendererVisible)return 0;long token=Token;Cancel();return token;}
}
