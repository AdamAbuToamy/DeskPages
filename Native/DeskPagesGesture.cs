using System;

// Pure recognition logic, exercised without Windows input injection.
public sealed class DeskPagesGesture
{
    public int WheelThreshold=40,PointerThreshold=350;
    private long last=-10000,emitted=-10000;
    private int total,lastDelta,lastSource;
    private bool used;
    private bool tracking,pointerUsed;
    private double x0,y0;
    public int Wheel(int delta,long milliseconds,int source)
    {
        if(milliseconds-last>450) {used=false;total=0;}
        // The same physical wheel packet can arrive from hook, raw and window routes.
        bool duplicate=source!=lastSource && delta==lastDelta && milliseconds-last<12;
        lastDelta=delta;lastSource=source;last=milliseconds;
        if(used || duplicate || milliseconds-emitted<300) return 0;
        total+=delta;
        if(Math.Abs(total)<WheelThreshold) return 0;
        used=true;emitted=milliseconds;return Math.Sign(total);
    }
    public int Pointer(int contacts,double x,double y,long milliseconds)
    {
        if(contacts!=2) {tracking=false;pointerUsed=false;return 0;}
        if(!tracking) {tracking=true;pointerUsed=false;x0=x;y0=y;return 0;}
        double dx=x-x0,dy=y-y0;
        if(pointerUsed || milliseconds-emitted<300 || Math.Abs(dx)<PointerThreshold || Math.Abs(dx)<Math.Abs(dy)*1.5) return 0;
        pointerUsed=true;used=true;total=0;last=milliseconds;emitted=milliseconds;
        return Math.Sign(dx);
    }
    public void Cancel() {tracking=false;pointerUsed=false;total=0;used=false;}
}
