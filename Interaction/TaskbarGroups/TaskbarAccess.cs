using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Automation;

internal sealed class TaskButton {
    public string AppId,Name;public string[] Names=new string[0];public Rectangle Bounds;
}
internal sealed class TaskbarSnapshot {
    public IntPtr Handle;public Rectangle Bounds,Weather;public TaskButton[] Buttons=new TaskButton[0];
    public TaskButton At(Point point){return Buttons.FirstOrDefault(b=>b.Bounds.Contains(point));}
}
internal static class TaskbarAccess {
    public static TaskbarSnapshot Read(){
        IntPtr h=WindowShell.FindWindowW("Shell_TrayWnd",null);var result=new TaskbarSnapshot{Handle=h,Bounds=WindowShell.Bounds(h)};if(h==IntPtr.Zero)return result;
        var root=AutomationElement.FromHandle(h);var list=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));var buttons=new List<TaskButton>();
        for(int i=0;i<list.Count;i++){
            var info=list[i].Current;if(info.IsOffscreen)continue;var box=Box(info.BoundingRectangle);if(box.IsEmpty)continue;
            string id=info.AutomationId??"";
            if(id=="WidgetsButton" || id=="AugmentedEntryPointButton")result.Weather=box;
            if(id.StartsWith("Appid:",StringComparison.OrdinalIgnoreCase))buttons.Add(new TaskButton{AppId=id.Substring(6).Trim(),Name=info.Name,Bounds=box});
        }
        result.Buttons=buttons.ToArray();return result;
    }
    public static TaskButton Hit(Point point){
        var element=AutomationElement.FromPoint(new System.Windows.Point(point.X,point.Y));
        var names=new List<string>();var root=WindowShell.GetAncestor(WindowShell.WindowFromPoint(point),2);bool preview=WindowShell.Class(root)=="TaskListThumbnailWnd";Rectangle previewBounds=WindowShell.Bounds(root);
        for(int depth=0;depth<8 && element!=null;depth++,element=TreeWalker.ControlViewWalker.GetParent(element)){
            var info=element.Current;string id=info.AutomationId??"";
            if(!string.IsNullOrWhiteSpace(info.Name))names.Add(info.Name);
            if(!preview && id.StartsWith("Appid:",StringComparison.OrdinalIgnoreCase))return new TaskButton{AppId=id.Substring(6).Trim(),Name=info.Name,Bounds=Box(info.BoundingRectangle)};
            // A thumbnail's accessible title identifies one window. Never guess
            // a process from a translated title or a closed pinned shortcut.
        }
        return preview?new TaskButton{Names=names.ToArray(),Bounds=previewBounds}:null;
    }
    public static GroupWindow[] Resolve(TaskButton button,GroupWindow[] windows){
        if(button==null)return new GroupWindow[0];
        if(!string.IsNullOrEmpty(button.AppId)){
            var direct=windows.Where(w=>!string.IsNullOrEmpty(w.AppId) && string.Equals(w.AppId,button.AppId,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(direct.Length>0 || windows.Length==0)return direct;
            return ResolveShortcut(button,windows,ShortcutIdentity.Map);
        }
        var titled=windows.Where(w=>w.Title==button.Name || button.Names.Contains(w.Title)).ToArray();return titled.Length==1?titled:new GroupWindow[0];
    }
    internal static GroupWindow[] ResolveShortcut(TaskButton button,GroupWindow[] windows,IDictionary<string,HashSet<string>> aliases){
        HashSet<string> paths;if(!aliases.TryGetValue(button.AppId,out paths))return new GroupWindow[0];
        return windows.Where(w=>string.IsNullOrEmpty(w.AppId) && w.ImagePath!=null && paths.Contains(w.ImagePath)
            && !aliases.Any(other=>!string.Equals(other.Key,button.AppId,StringComparison.OrdinalIgnoreCase) && other.Value.Contains(w.ImagePath))).ToArray();
    }
    private static Rectangle Box(System.Windows.Rect r){return r.IsEmpty || double.IsInfinity(r.X)?Rectangle.Empty:Rectangle.FromLTRB((int)r.Left,(int)r.Top,(int)r.Right,(int)r.Bottom);}
}
