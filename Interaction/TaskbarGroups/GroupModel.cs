using System;
using System.Collections.Generic;
using System.Linq;

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
