using System;

// Synchronize the live shell view with an explicitly visible Shell preference.
// A forced ShowWindow is not evidence that FWF_NOICONS was cleared internally.
public static class DeskPagesVisibilitySync
{
    public const uint NoIcons=0x1000;
    public static bool RevealIfStale(bool shellHidden,Func<uint> read,Action<uint,uint> write){
        if(shellHidden)return false; // Preserve a real user's Hide icons choice.
        uint before=read();
        if((before&NoIcons)==0)return false;
        write(NoIcons,0); // One-bit mask only; never replace all folder flags.
        uint after=read();
        if((after&NoIcons)!=0 || (after&~NoIcons)!=(before&~NoIcons))
            throw new InvalidOperationException("Explorer icon visibility synchronization was not verified; arrange/grid flags must be preserved.");
        return true;
    }
}
