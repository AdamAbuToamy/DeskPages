using System;
using System.Drawing;
// Pure geometry shared by the broker and monitor simulator. Coordinates are physical pixels.
public static class DeskPagesMonitorPolicy {
    public static bool InternalEdge(Rectangle area,Rectangle[] others,bool left){
        int edge=left?area.Left:area.Right;
        foreach(var r in others){if(r==area)continue;bool neighbour=left?r.Right<=edge+2 && r.Right>=edge-2:r.Left<=edge+2 && r.Left>=edge-2;
            if(neighbour && Math.Min(area.Bottom,r.Bottom)>Math.Max(area.Top,r.Top))return true;}
        return false;
    }
    public static int EdgeDirection(int x,int y,Rectangle area,Rectangle[] others,int zone,bool primaryOnly,bool primary){
        if(primaryOnly && !primary || y<area.Top || y>=area.Bottom)return 0;
        if(x>=area.Left && x<area.Left+zone && !InternalEdge(area,others,true))return -1;
        if(x<area.Right && x>=area.Right-zone && !InternalEdge(area,others,false))return 1;
        return 0;
    }
}
