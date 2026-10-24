using System;
using System.Drawing;

// One physical capture rectangle shared by warming, cache lookup and rendering.
public static class DeskPagesCapturePlan {
    public static Rectangle Bounds(Rectangle primary,Rectangle[] areas,bool primaryOnly){
        if(primary.Width<=0 || primary.Height<=0)throw new ArgumentException("Invalid primary working area.");
        if(primaryOnly)return primary;
        var result=primary;foreach(var area in areas)if(area.Width>0 && area.Height>0)result=Rectangle.Union(result,area);
        return result;
    }
    public static Rectangle Intersection(Rectangle area,Rectangle bounds){
        var result=Rectangle.Intersect(area,bounds);
        return result.Width>0 && result.Height>0?result:Rectangle.Empty;
    }
}
