using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;

// Session-only membership: a process restart or reused HWND is never a member.
internal sealed class GroupWindow {
    public long Handle,Started;public int ProcessId;public string Title,AppId,ImagePath;
    public string Key {get{return Handle+":"+ProcessId+":"+Started;}}
}
internal sealed class LiveGroup {
    public int Number;public string AppId;public bool Used;
    public readonly Dictionary<string,GroupWindow> Windows=new Dictionary<string,GroupWindow>();
}
internal sealed class GroupModel {
    private readonly List<LiveGroup> groups=new List<LiveGroup>();
    private readonly string prefix;
    public GroupModel(string session){Guid id;if(!Guid.TryParseExact(session,"N",out id))throw new ArgumentException("Invalid session");prefix="DeskPages.LiveGroups."+session+".";}
    public LiveGroup[] Groups {get{return groups.ToArray();}}
    public LiveGroup Add(){int n=Enumerable.Range(1,99).FirstOrDefault(i=>!groups.Any(g=>g.Number==i));if(n==0)throw new InvalidOperationException("Maximum 99 groups");var created=new LiveGroup{Number=n,AppId=prefix+n};groups.Add(created);return created;}
    public LiveGroup Find(string id){return groups.FirstOrDefault(g=>g.AppId==id);}
    public LiveGroup Owner(GroupWindow window){return groups.FirstOrDefault(g=>g.Windows.ContainsKey(window.Key));}
    public void Put(LiveGroup group,GroupWindow window){if(!groups.Contains(group))throw new ArgumentException("Unknown group");foreach(var g in groups)g.Windows.Remove(window.Key);group.Windows[window.Key]=window;group.Used=true;}
    public void Remove(GroupWindow window){foreach(var g in groups)g.Windows.Remove(window.Key);}
    public LiveGroup[] Prune(Func<GroupWindow,bool> alive){foreach(var g in groups)foreach(var w in g.Windows.Values.ToArray())if(!alive(w))g.Windows.Remove(w.Key);var empty=groups.Where(g=>g.Used && g.Windows.Count==0).ToArray();foreach(var g in empty)groups.Remove(g);return empty;}
    public void DeleteEmpty(LiveGroup group){if(group.Windows.Count!=0)throw new InvalidOperationException("Group is not empty");groups.Remove(group);}
}
internal sealed class GroupDropDwell {
    public const int Hold=1200;
    private string target;private long since;
    public bool Observe(string next,long now){if(next!=target){target=next;since=now;}return target!=null && now-since>=Hold;}
    public void Reset(){target=null;since=0;}
}
internal static class GroupLayoutPolicy {
    public static bool ShellSurface(string windowClass){return windowClass=="Progman" || windowClass=="WorkerW" || windowClass=="Shell_TrayWnd" || windowClass=="Shell_SecondaryTrayWnd";}
    public static Rectangle AddBounds(Rectangle taskbar,Rectangle weather,Rectangle area,Rectangle[] apps,bool accessible){
        if(taskbar.IsEmpty || taskbar.Height<10 || taskbar.Top>=area.Bottom-2 || taskbar.Bottom<=area.Top+2)return Rectangle.Empty;
        int size=Math.Max(28,Math.Min(36,taskbar.Height-8)),x=weather.IsEmpty?area.Left+160:weather.Right+8;
        var result=new Rectangle(x,taskbar.Top+(taskbar.Height-size)/2,size,size);
        if(!accessible || apps.Any(b=>b.IntersectsWith(result)))result.Y=taskbar.Top-size-6;
        return result;
    }
    public static bool Fullscreen(Rectangle window,Rectangle display,string windowClass){
        if(ShellSurface(windowClass) || windowClass=="SHELLDLL_DefView" || windowClass=="SysListView32")return false;
        return !window.IsEmpty && window.Width>=display.Width && window.Height>=display.Height;
    }
}
