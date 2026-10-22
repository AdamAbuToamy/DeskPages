using System;
using System.Drawing;
using System.Linq;
using System.Threading;

// Immutable, pre-resolved hit regions. The input thread never calls UI Automation,
// reads files or waits for the UI thread inside a low-level hook.
internal sealed class GroupDragSource {
    public TaskButton Button;public GroupWindow[] Windows;
}
internal sealed class GroupInputMap {
    public long Created;public int DragDistance=4;public IntPtr Taskbar;public Rectangle Bounds;public GroupDragSource[] Sources=new GroupDragSource[0];
    public TaskButton[] Groups=new TaskButton[0];
    public bool Fresh(long now){return now-Created>=0 && now-Created<=3000;}
    public GroupDragSource SourceAt(Point point,long now){
        if(!Fresh(now) || Groups.Length==0)return null;
        if(Groups.Any(g=>g.Bounds.Contains(point)))return null; // Native group reordering.
        return Sources.FirstOrDefault(s=>s.Button.Bounds.Contains(point));
    }
}
internal sealed class GroupDragGesture {
    public GroupDragSource Source;public TaskButton[] Groups;public Point Press,Release;public IntPtr Taskbar;
    public long Epoch,Started,TransferredAt;public Point LastNative,LastPhysical;public volatile bool NativeActive;public volatile string TargetAppId;public Rectangle TargetBounds;public volatile bool Cancelled,Released;
}
internal static class GroupDragGeometry {
    public static Point? NativeEnd(GroupDragGesture gesture,int distance){
        distance=Math.Max(12,distance*2);
        if(Math.Abs(gesture.LastNative.X-gesture.Press.X)+Math.Abs(gesture.LastNative.Y-gesture.Press.Y)>=distance)return gesture.LastNative;
        // A direct jump from press to target must still cross Windows' drag
        // threshold within the source button, so completing it cannot be a click.
        var box=gesture.Source.Button.Bounds;var p=gesture.Press;int gap=distance+2;
        foreach(var candidate in new[]{new Point(p.X+gap,p.Y),new Point(p.X-gap,p.Y),new Point(p.X,p.Y+gap),new Point(p.X,p.Y-gap)}){
            if(box.Contains(candidate) && !gesture.Groups.Any(g=>g.Bounds.Contains(candidate)))return candidate;
        }return null;
    }
}
// Observe the native drag; take ownership only before it enters a group.
// Ordinary clicks, app reordering and dragging a group itself pass through.
internal sealed class GroupDragGate {
    private GroupDragGesture active;private long epoch;
    public long Epoch{get{return Interlocked.Read(ref epoch);}}
    public string Describe(){var g=active;return g==null?"idle":"source="+g.Source.Button.AppId+", native="+g.NativeActive+", cancelled="+g.Cancelled+", last="+g.LastPhysical;}
    public GroupDragGesture Current{get{return active;}}
    public bool Owned{get{return active!=null && !active.NativeActive;}}
    public bool Process(int kind,Point point,bool injected,bool modified,IntPtr root,GroupInputMap map,long now,Action<GroupDragGesture> begin,Action<GroupDragGesture> end,Action nativeEnd=null,Func<GroupDragGesture,Point,bool> take=null,Func<GroupDragGesture,Point,bool> resume=null,Action<Point> move=null){
        if(injected)return false;
        if(kind==0x201){
            if(active!=null)return Owned;
            long current=Interlocked.Increment(ref epoch);var source=map.SourceAt(point,now);
            if(source!=null && !modified && root==map.Taskbar)active=new GroupDragGesture{Source=source,Groups=map.Groups,Press=point,LastNative=point,Taskbar=map.Taskbar,Epoch=current,Started=now,NativeActive=true};
            return false;
        }
        if(kind==0x200 && active!=null){
            var gesture=active;gesture.LastPhysical=point;
            if(gesture.Cancelled)return !gesture.NativeActive;
            bool moved=Math.Abs(point.X-gesture.Press.X)+Math.Abs(point.Y-gesture.Press.Y)>=map.DragDistance;
            var target=moved && map.Fresh(now) && map.Taskbar==gesture.Taskbar?TargetAt(map,gesture,point):null;
            if(gesture.NativeActive){
                if(target==null || take==null || !take(gesture,point)){gesture.LastNative=point;return false;}
                gesture.NativeActive=false;gesture.Started=now;gesture.TransferredAt=now;gesture.TargetAppId=target.AppId;gesture.TargetBounds=target.Bounds;begin(gesture);return true;
            }
            if(target!=null){gesture.TargetAppId=target.AppId;gesture.TargetBounds=target.Bounds;if(move!=null)move(point);return true;}
            if(resume!=null && resume(gesture,point)){
                gesture.NativeActive=true;gesture.TargetAppId=null;gesture.LastNative=point;end(gesture);return true;
            }
            gesture.TargetAppId=null;if(move!=null)move(point);return true;
        }
        if(kind==0x202 && active!=null){
            var gesture=active;
            // A coalesced/fast input batch can report release before its move.
            // Recognize the intended target from release, before Explorer sees up.
            if(gesture.NativeActive && !gesture.Cancelled && map.Fresh(now) && map.Taskbar==gesture.Taskbar && Math.Abs(point.X-gesture.Press.X)+Math.Abs(point.Y-gesture.Press.Y)>=map.DragDistance){
                var releasedTarget=map.Groups.FirstOrDefault(g=>g.Bounds.Contains(point));
                if(releasedTarget!=null && take!=null && take(gesture,point)){
                    gesture.NativeActive=false;gesture.LastPhysical=point;gesture.TargetAppId=releasedTarget.AppId;gesture.TargetBounds=releasedTarget.Bounds;gesture.Started=now;gesture.TransferredAt=now;begin(gesture);
                }
            }
            active=null;gesture.Release=gesture.NativeActive?point:gesture.LastPhysical;gesture.Released=true;point=gesture.Release;
            if(gesture.NativeActive){if(nativeEnd!=null)nativeEnd();return false;}
            // A move may be coalesced with release: use the current target region.
            var target=map.Fresh(now)?TargetAt(map,gesture,point):null;
            gesture.TargetAppId=target==null?null:target.AppId;
            if(target!=null)gesture.TargetBounds=target.Bounds;
            end(gesture);if(nativeEnd!=null)nativeEnd();return true;
        }
        if(kind==0x202 && nativeEnd!=null)nativeEnd();
        if((kind==0x204 || kind==0x207 || kind==0x20B) && active!=null)Cancel();
        return false;
    }
    private static TaskButton TargetAt(GroupInputMap map,GroupDragGesture gesture,Point point){
        var hit=map.Groups.FirstOrDefault(g=>g.Bounds.Contains(point));if(hit!=null)return hit;
        if(!gesture.NativeActive && gesture.TargetAppId!=null && gesture.TargetBounds.Contains(point)){
            var same=map.Groups.FirstOrDefault(g=>g.AppId==gesture.TargetAppId);
            if(same!=null && Math.Abs(same.Bounds.Left-gesture.TargetBounds.Left)<=2*Math.Max(same.Bounds.Width,gesture.TargetBounds.Width) && Math.Abs(same.Bounds.Top-gesture.TargetBounds.Top)<8)return new TaskButton{AppId=same.AppId,Bounds=gesture.TargetBounds};
        }return null;
    }
    public void Cancel(){if(active!=null)active.Cancelled=true;}
    public void Abort(Action<GroupDragGesture> end){var old=active;active=null;Interlocked.Increment(ref epoch);if(old!=null){old.Cancelled=true;old.Released=true;if(!old.NativeActive)end(old);}}
}
