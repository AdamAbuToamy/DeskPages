using System;

// Display-only timing. Never dispatches commands or reads/writes page state.
public sealed class DeskPagesIndicatorPolicy {
    private long started=-1;
    public void Start(long now){started=now;}
    public void Cancel(){started=-1;}
    public double Opacity(long now){
        if(started<0 || now<started)return 0;
        long elapsed=now-started;
        if(elapsed>=1150)return 0;
        if(elapsed<120)return 0.86*Ease(elapsed/120.0);
        if(elapsed<850)return 0.86;
        return 0.86*(1-Ease((elapsed-850)/300.0));
    }
    private static double Ease(double value){return value*value*(3-2*value);}
}
