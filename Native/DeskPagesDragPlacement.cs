using System;
using System.Collections.Generic;

// A selection is one rigid group. Translate every source icon by the same
// vector; do not independently repack its members into free cells.
public static class DeskPagesDragPlacement {
    public sealed class Item {public string Key;public int X,Y;}
    private sealed class Candidate {public int X,Y;public double Distance;}
    private static int Align(int minimum,int origin,int step){return minimum+((origin-minimum)%step+step)%step;}
    private static bool Collides(int x,int y,Item other,int sx,int sy){return Math.Abs((long)x-other.X)<sx-2 && Math.Abs((long)y-other.Y)<sy-2;}
    public static Item[] Plan(int[] geometry,int sx,int sy,bool snap,int gridX,int gridY,int grabX,int grabY,Item[] source,Item[] occupied){
        if(geometry==null || geometry.Length!=6 || sx<16 || sy<16 || source==null || source.Length<1 || source.Length>256)throw new ArgumentException("Invalid drag group geometry");
        if(occupied==null)occupied=new Item[0];
        var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int minX=int.MaxValue,minY=int.MaxValue,maxX=int.MinValue,maxY=int.MinValue;
        foreach(Item item in source){
            if(item==null || string.IsNullOrEmpty(item.Key) || !keys.Add(item.Key) || Math.Abs((long)item.X)>1000000 || Math.Abs((long)item.Y)>1000000)throw new ArgumentException("Invalid source group");
            minX=Math.Min(minX,item.X);maxX=Math.Max(maxX,item.X);minY=Math.Min(minY,item.Y);maxY=Math.Max(maxY,item.Y);
        }
        int left=checked(geometry[2]-minX),top=checked(geometry[3]-minY),right=checked(geometry[4]-sx-maxX),bottom=checked(geometry[5]-sy-maxY);
        if(right<left || bottom<top)return null;
        int desiredX=checked(geometry[0]-grabX),desiredY=checked(geometry[1]-grabY);
        int originX=snap?checked(gridX-source[0].X):Math.Max(left,Math.Min(right,desiredX));
        int originY=snap?checked(gridY-source[0].Y):Math.Max(top,Math.Min(bottom,desiredY));
        int firstX=Align(left,originX,sx),firstY=Align(top,originY,sy);
        if(firstX>right || firstY>bottom)return null;
        long cells=((long)(right-firstX)/sx+1)*((long)(bottom-firstY)/sy+1);
        if(cells>65536)throw new ArgumentException("Drag working area is too large");
        var candidates=new List<Candidate>();
        for(int x=firstX;x<=right;x+=sx)for(int y=firstY;y<=bottom;y+=sy){double dx=(double)x-desiredX,dy=(double)y-desiredY;candidates.Add(new Candidate{X=x,Y=y,Distance=dx*dx+dy*dy});}
        candidates.Sort(delegate(Candidate a,Candidate b){int order=a.Distance.CompareTo(b.Distance);if(order!=0)return order;order=a.X.CompareTo(b.X);return order!=0?order:a.Y.CompareTo(b.Y);});
        foreach(Candidate candidate in candidates){
            bool free=true;
            foreach(Item item in source){foreach(Item old in occupied){if(Collides(item.X+candidate.X,item.Y+candidate.Y,old,sx,sy)){free=false;break;}}if(!free)break;}
            if(!free)continue;
            var result=new Item[source.Length];for(int i=0;i<source.Length;i++)result[i]=new Item{Key=source[i].Key,X=source[i].X+candidate.X,Y=source[i].Y+candidate.Y};return result;
        }
        return null;
    }
}
