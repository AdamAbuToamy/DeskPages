using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal sealed class GroupTaskbar:IDisposable {
    [ComImport,Guid("56FDF342-FD6D-11d0-958A-006097C9A090"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IList {
        void HrInit();void AddTab(IntPtr hwnd);void DeleteTab(IntPtr hwnd);void ActivateTab(IntPtr hwnd);void SetActiveAlt(IntPtr hwnd);
    }
    private IList api;
    public GroupTaskbar(){Reset();}public void Reset(){if(api!=null)Marshal.ReleaseComObject(api);api=(IList)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11d0-958A-006097C9A090")));api.HrInit();}public void Add(IntPtr h){api.AddTab(h);}public void Remove(IntPtr h){api.DeleteTab(h);}public void Dispose(){Marshal.ReleaseComObject(api);}
}
internal sealed class GroupController:ApplicationContext {
    private readonly string session=Guid.NewGuid().ToString("N"),executable=Application.ExecutablePath;
    private readonly GroupModel model;private readonly GroupRecovery recovery;
    private readonly Dictionary<int,GroupAnchor> anchors=new Dictionary<int,GroupAnchor>();
    private readonly GroupTaskbar taskbar=new GroupTaskbar();
    private readonly GroupAddButton plus;private readonly GroupDropCue cue=new GroupDropCue();
    private readonly GroupDragGhost ghost=new GroupDragGhost();private readonly GroupDragInput input;private readonly GroupPopup groupPopup;
    private GroupDragGesture captured;private int replayBusy,windowsBusy;private GroupDragSource[] resolvedSources=new GroupDragSource[0];private GroupWindowPicker picker;
    private readonly NotifyIcon tray;private readonly Icon trayIcon;private readonly ToolTip hint=new ToolTip();
    private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=40};
    private readonly GroupDropDwell dwell=new GroupDropDwell();
    private readonly EventWaitHandle stop=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\DeskPagesTaskbarGroupsStop-"+System.Security.Principal.WindowsIdentity.GetCurrent().User.Value);
    private TaskbarSnapshot snapshot=new TaskbarSnapshot();private GroupWindow[] drag=new GroupWindow[0];
    private Point press;private bool held,moved,ready,disposed;private int readBusy,generation;private long lastRead,lastRaise;
    private LiveGroup target;private Process guardian;private int resolveBusy;
    private TaskButton[] pressGroups=new TaskButton[0];private string sourceAppId;
    private static long Now{get{return (long)(Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency);}}
    public GroupController(){
        model=new GroupModel(session);recovery=new GroupRecovery(session);plus=new GroupAddButton(AddGroup);var uiHandle=plus.Handle;
        groupPopup=new GroupPopup(plus,g=>model.Find(g.AppId)==g,DeleteEmptyGroup,WindowShell.Activate,BeginMemberDrag,ChooseOpenApps);
        trayIcon=Icon.ExtractAssociatedIcon(executable)??(Icon)SystemIcons.Application.Clone();
        tray=new NotifyIcon{Icon=trayIcon,Text="DeskPages · Taskbar groups",Visible=true};
        var menu=new ContextMenuStrip();menu.Items.Add("Add group",null,delegate{AddGroup();});var chooseMenu=(ToolStripMenuItem)menu.Items.Add("Add open apps to group");chooseMenu.DropDownOpening+=delegate{
            foreach(ToolStripItem old in chooseMenu.DropDownItems.Cast<ToolStripItem>().ToArray()){chooseMenu.DropDownItems.Remove(old);old.Dispose();}
            foreach(var live in model.Groups){var destination=live;chooseMenu.DropDownItems.Add("Group "+live.Number.ToString("00"),null,delegate{plus.BeginInvoke((Action)delegate{ChooseOpenApps(destination);});});}
            if(model.Groups.Length==0)chooseMenu.DropDownItems.Add("Create a group with + first").Enabled=false;
        };menu.Items.Add("Copy taskbar diagnostics",null,delegate{Clipboard.SetText(input.Diagnostics+Environment.NewLine+"Native buttons="+snapshot.Buttons.Length+"; model groups="+model.Groups.Length+Environment.NewLine+string.Join(Environment.NewLine,snapshot.Buttons.Select(b=>b.AppId+" @ "+b.Bounds)));});menu.Items.Add("Restore apps and turn off",null,delegate{File.WriteAllText(EnabledPath,"off");plus.BeginInvoke((Action)ExitThread);});tray.ContextMenuStrip=menu;tray.DoubleClick+=delegate{AddGroup();};
        Directory.CreateDirectory(Path.GetDirectoryName(EnabledPath));File.WriteAllText(EnabledPath,"on");
        StartGuardian();input=new GroupDragInput(g=>Post(delegate{BeginCaptured(g);}),g=>Post(delegate{EndCaptured(g);}),delegate{Post(RefreshSoon);});snapshot=TaskbarAccess.ReadBounds();PlacePlus();timer.Tick+=delegate{Tick();};timer.Start();
    }
    public static string EnabledPath{get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPages","TaskbarGroups","enabled.txt");}}
    private void StartGuardian(){
        using(var self=Process.GetCurrentProcess()){
            File.WriteAllLines(Path.Combine(recovery.Folder,"owner.txt"),new[]{self.Id.ToString(),self.StartTime.ToUniversalTime().Ticks.ToString()});
            guardian=Process.Start(new ProcessStartInfo(executable,"--watch "+session+" "+self.Id+" "+self.StartTime.ToUniversalTime().Ticks){UseShellExecute=false,CreateNoWindow=true});
        }
        string receipt=Path.Combine(recovery.Folder,"watch-ready");var watch=Stopwatch.StartNew();
        while(!File.Exists(receipt) && !guardian.HasExited && watch.ElapsedMilliseconds<3000)Thread.Sleep(25);
        if(!File.Exists(receipt) || guardian.HasExited)throw new InvalidOperationException("The taskbar recovery helper could not start");
    }
    private void AddGroup(){
        LiveGroup group=null;GroupAnchor anchor=null;
        try{
            group=model.Add();var added=group;anchor=new GroupAnchor(group,delegate{OpenGroup(added);});
            // Set the window identity before Explorer sees its first visible state.
            WindowShell.Set(anchor.Handle,5,group.AppId);anchors.Add(group.Number,anchor);
            anchor.Show();taskbar.Add(anchor.Handle);
            hint.Show("Drag a running taskbar icon over the numbered group. Wait for the green border, then release.",plus,0,-70,5000);RefreshSoon();
        }catch(Exception ex){
            if(anchor!=null){try{taskbar.Remove(anchor.Handle);}catch(Exception){}anchor.Finish();}
            if(group!=null){anchors.Remove(group.Number);model.DeleteEmpty(group);}
            MessageBox.Show(ex.Message,"DeskPages groups");
        }
    }
    private void Tick(){
        if(disposed)return;
        if(stop.WaitOne(0)){ExitThread();return;}
        try{
            if(Now-lastRaise>=250){lastRaise=Now;plus.KeepAbove();}
            if(Now-lastRead>=(held?350:(model.Groups.Length>0?750:2000)) && Interlocked.CompareExchange(ref readBusy,1,0)==0){lastRead=Now;ReadSnapshot();}
            if(captured!=null){if(Now-captured.Started>30000){captured.Cancelled=true;input.Repair();EndCaptured(captured);}else UpdateCaptured(Cursor.Position);return;}
            bool down=(WindowShell.GetAsyncKeyState(1)&0x8000)!=0;Point point=Cursor.Position;
            if((WindowShell.GetAsyncKeyState(0x1B)&0x8000)!=0){CancelDrag();held=down;return;}
            if(down && !held){press=point;moved=false;drag=new GroupWindow[0];sourceAppId=null;pressGroups=snapshot.Buttons.Where(b=>model.Find(b.AppId)!=null).ToArray();RefreshSoon();int request=++generation;
                var root=WindowShell.GetAncestor(WindowShell.WindowFromPoint(point),2);
                if(WindowShell.Class(root)=="TaskListThumbnailWnd")ResolvePress(point,request);
            }
            if(down && drag.Length>0){
                moved=moved || Math.Abs(point.X-press.X)+Math.Abs(point.Y-press.Y)>SystemInformation.DragSize.Width;
                var button=TaskbarAccess.DropTarget(snapshot,pressGroups,point,sourceAppId,id=>model.Find(id)!=null);target=moved && button!=null?model.Find(button.AppId):null;
                ready=dwell.Observe(target==null?null:target.AppId,Now);
                if(target!=null)cue.Present(button.Bounds,ready);else cue.Hide();
            }
            if(!down && held){
                if(moved && drag.Length>0){
                    if(ready && target!=null && TaskbarAccess.DropTarget(snapshot,pressGroups,point,sourceAppId,id=>model.Find(id)!=null)!=null)VerifyDrop(target,drag,point);
                    else if(target==null && (Math.Abs(point.X-press.X)+Math.Abs(point.Y-press.Y)>48))foreach(var w in drag)if(model.Owner(w)!=null){recovery.Restore(w);if(!recovery.Owns(w))model.Remove(w);}
                }
                CancelDrag();Prune();RefreshSoon();
            }
            held=down;
        }catch(Exception ex){CancelDrag();hint.Show(ex.Message,plus,0,-50,4000);}
    }
    private void ResolvePress(Point point,int request){
        if(Interlocked.CompareExchange(ref resolveBusy,1,0)!=0)return;
        ThreadPool.QueueUserWorkItem(delegate{
            TaskButton button=null;GroupWindow[] resolved=new GroupWindow[0];try{button=TaskbarAccess.Hit(point);resolved=TaskbarAccess.Resolve(button,WindowShell.Windows());}catch(Exception){ }finally{Interlocked.Exchange(ref resolveBusy,0);}
            Post(delegate{
                if(request!=generation || (WindowShell.GetAsyncKeyState(1)&0x8000)==0)return;
                // Dragging a numbered group itself is left to Explorer's reorder.
                if(button!=null && model.Find(button.AppId)!=null)return;
                sourceAppId=button==null?null:button.AppId;
                drag=resolved;
                if(drag.Length==0 && button!=null)hint.Show("No unambiguous running window was found for this button. Closed pinned apps are not grouped.",plus,0,-60,4000);
            });
        });
    }
    private void VerifyDrop(LiveGroup group,GroupWindow[] items,Point point){
        int approvedGeneration=generation+1;
        string source=sourceAppId;TaskButton[] original=pressGroups;
        if(Interlocked.CompareExchange(ref resolveBusy,1,0)!=0)return;
        ThreadPool.QueueUserWorkItem(delegate{
            TaskButton actual=null;try{actual=TaskbarAccess.Hit(point);if(actual==null || actual.AppId!=group.AppId)actual=TaskbarAccess.DropTarget(TaskbarAccess.Read(),original,point,source,id=>id==group.AppId);}catch(Exception){ }finally{Interlocked.Exchange(ref resolveBusy,0);}
            if(actual!=null && actual.AppId==group.AppId)Post(delegate{
                if(model.Find(group.AppId)!=group || generation!=approvedGeneration)return;
                try{Assign(group,items);}catch(Exception ex){hint.Show(ex.Message,plus,0,-50,4000);}
            });
        });
    }
    private void BeginCaptured(GroupDragGesture gesture){
        CancelDrag();captured=gesture;held=true;press=gesture.Press;sourceAppId=gesture.Source.Button.AppId;drag=gesture.Source.Windows;pressGroups=gesture.Groups;ghost.Begin(gesture.Source);
    }
    private void UpdateCaptured(Point point){
        if(captured==null)return;
        if(captured.Cancelled){target=null;ready=false;dwell.Reset();cue.Hide();ghost.Hide();return;}
        moved=moved || Math.Abs(point.X-press.X)+Math.Abs(point.Y-press.Y)>SystemInformation.DragSize.Width;
        if(!moved)return;
        ghost.Present(point);
        // Explorer never receives the app's button-down, so it cannot reorder
        // the numbered targets underneath this gesture. Native group drags pass through.
        var button=snapshot.At(point);target=button==null?null:model.Find(button.AppId);
        ready=dwell.Observe(target==null?null:target.AppId,Now);
        if(target!=null)cue.Present(button.Bounds,ready);else cue.Hide();
    }
    private void EndCaptured(GroupDragGesture gesture){
        if(captured!=gesture)return;
        UpdateCaptured(gesture.Release);
        var destination=target;bool accepted=moved && ready && destination!=null && !gesture.Cancelled;
        bool groupDrop=moved && destination!=null;
        captured=null;held=false;ghost.Hide();CancelDrag();int request=generation;
        if(gesture.Cancelled)return;
        if(groupDrop && !accepted){hint.Show("Hold over the group until its border turns green, then release.",plus,0,-50,3000);return;}
        if(Interlocked.CompareExchange(ref replayBusy,1,0)!=0)return;
        ThreadPool.QueueUserWorkItem(delegate{
            TaskbarSnapshot fresh=null;GroupWindow[] windows=null;
            try{fresh=TaskbarAccess.Read();if(accepted)windows=TaskbarAccess.Resolve(fresh.At(gesture.Press),WindowShell.Windows());}
            catch(Exception){}finally{Interlocked.Exchange(ref replayBusy,0);}
            Post(delegate{
                if(disposed || request!=generation || !input.IsCurrent(gesture) || gesture.Cancelled || fresh==null)return;
                if(accepted){
                    var hit=fresh.At(gesture.Release);var source=fresh.At(gesture.Press);
                    if(model.Find(destination.AppId)!=destination || hit==null || hit.AppId!=destination.AppId || source==null || source.AppId!=gesture.Source.Button.AppId || !WindowShell.OnTaskbar(gesture.Release,fresh.Handle))return;
                    if(gesture.Source.Windows.Length==0 || windows==null || windows.Length==0){ChooseOpenApps(destination);return;}
                    // Re-resolve at commit, refusing changes to the selected live window set.
                    if(!windows.Select(w=>w.Key).OrderBy(k=>k).SequenceEqual(gesture.Source.Windows.Select(w=>w.Key).OrderBy(k=>k)))return;
                    Assign(destination,windows);
                }else if(!fresh.Buttons.Any(b=>model.Find(b.AppId)!=null && b.Bounds.Contains(gesture.Release))){GroupDragInput.Replay(gesture,fresh);}
                RefreshSoon();
            });
        });
    }
    private void ReadSnapshot(){
        var ids=model.Groups.Select(g=>g.AppId).ToArray();
        ThreadPool.QueueUserWorkItem(delegate{
            TaskbarSnapshot next=null;
            try{next=TaskbarAccess.Read();}catch(Exception){}finally{Interlocked.Exchange(ref readBusy,0);}
            if(next==null)return;
            var cache=Interlocked.CompareExchange(ref resolvedSources,null,null);
            var regions=new GroupInputMap{Created=Now,Taskbar=next.Handle,Bounds=next.Bounds,Groups=next.Buttons.Where(b=>ids.Contains(b.AppId)).ToArray(),Sources=next.Buttons.Where(b=>!ids.Contains(b.AppId)).Select(b=>new GroupDragSource{Button=b,Windows=(cache.FirstOrDefault(c=>c.Button.AppId==b.AppId)??new GroupDragSource{Windows=new GroupWindow[0]}).Windows}).ToArray()};
            input.Publish(regions);
            Post(delegate{bool recreated=snapshot.Handle!=next.Handle;snapshot=next;PlacePlus();Prune();if(recreated){taskbar.Reset();foreach(var g in model.Groups)if(!g.Used)taskbar.Add(anchors[g.Number].Handle);}});
            // Window/shortcut resolution cannot stall publication of hit regions.
            if(ids.Length>0 && Interlocked.CompareExchange(ref windowsBusy,1,0)==0)ThreadPool.QueueUserWorkItem(delegate{
                try{var windows=WindowShell.Windows();var sources=next.Buttons.Select(b=>new GroupDragSource{Button=b,Windows=TaskbarAccess.Resolve(b,windows)}).ToArray();Interlocked.Exchange(ref resolvedSources,sources);}
                catch(Exception){}finally{Interlocked.Exchange(ref windowsBusy,0);}
            });
        });
    }
    private void ChooseOpenApps(LiveGroup group){
        if(model.Find(group.AppId)!=group)return;
        if(picker!=null && !picker.IsDisposed){picker.Activate();return;}
        picker=new GroupWindowPicker(group.Number,delegate(GroupWindow[] chosen){if(model.Find(group.AppId)!=group)throw new InvalidOperationException("The group was removed");Assign(group,chosen);});
        picker.FormClosed+=delegate{picker=null;};picker.Show();
    }
    private void PlacePlus(){
        Rectangle area=Screen.PrimaryScreen.Bounds;
        var bar=snapshot.Bounds;
        if(!bar.IsEmpty && bar.Height>=10)area=Screen.FromRectangle(bar).Bounds;
        var bounds=GroupLayoutPolicy.AddBounds(bar,snapshot.Weather,area,snapshot.Buttons.Select(b=>b.Bounds).ToArray(),snapshot.Accessible);
        plus.Bounds=bounds;if(!plus.Visible)plus.Show();
    }
    private void Assign(LiveGroup group,GroupWindow[] items){
        if(guardian.HasExited)throw new InvalidOperationException("Recovery is unavailable; no windows were grouped");
        string icon=Path.Combine(recovery.Folder,"group-"+group.Number+".ico");using(var picture=GroupVisuals.Number(group.Number))using(var file=File.Create(icon))picture.Save(file);
        string command="\""+executable+"\" --group "+session+" "+group.Number;
        string[] properties={command,icon+",0","DeskPages · "+group.Number.ToString("00"),group.AppId};
        var completed=new List<GroupWindow>();
        try{foreach(var w in items){if(!WindowShell.Same(w))throw new InvalidOperationException("A dragged window closed");recovery.Assign(w,properties);completed.Add(w);}}
        catch{foreach(var w in completed){recovery.Restore(w);if(!recovery.Owns(w))model.Remove(w);}Prune();throw;}
        foreach(var w in completed)model.Put(group,w);
        taskbar.Remove(anchors[group.Number].Handle);Prune();RefreshSoon();
    }
    private void Prune(){
        foreach(var g in model.Groups)foreach(var w in g.Windows.Values.ToArray()){
            var current=WindowShell.Identity(new IntPtr(w.Handle));
            if(current==null || current.ProcessId!=w.ProcessId || current.Started!=w.Started || current.AppId!=g.AppId){recovery.Restore(w);if(!recovery.Owns(w))model.Remove(w);}
        }
        foreach(var empty in model.Prune(WindowShell.Same))RemoveAnchor(empty);
    }
    private void RemoveAnchor(LiveGroup group){var anchor=anchors[group.Number];try{taskbar.Remove(anchor.Handle);}catch(COMException){ }anchor.Finish();anchors.Remove(group.Number);}
    private void OpenGroup(LiveGroup group){groupPopup.Show(group,Cursor.Position);}
    private void DeleteEmptyGroup(LiveGroup group){model.DeleteEmpty(group);RemoveAnchor(group);RefreshSoon();}
    private void BeginMemberDrag(GroupWindow window){CancelDrag();drag=new[]{window};press=Cursor.Position;held=true;}
    private void RefreshSoon(){lastRead=0;}
    private void CancelDrag(){generation++;drag=new GroupWindow[0];pressGroups=new TaskButton[0];sourceAppId=null;target=null;ready=false;moved=false;dwell.Reset();cue.Hide();}
    private void Post(Action action){if(disposed)return;try{plus.BeginInvoke((Action)delegate{if(!disposed)try{action();}catch(Exception ex){hint.Show(ex.Message,plus,0,-50,4000);}});}catch(InvalidOperationException){ }}
    protected override void ExitThreadCore(){
        if(disposed)return;disposed=true;if(captured!=null)captured.Cancelled=true;input.Dispose();timer.Stop();timer.Dispose();
        try{recovery.RestoreAll();}catch(Exception){/* The independent guardian retries the durable journal after exit. */}
        finally{foreach(var group in model.Groups)if(anchors.ContainsKey(group.Number))RemoveAnchor(group);if(picker!=null && !picker.IsDisposed)picker.Close();groupPopup.Dispose();ghost.Dispose();cue.Dispose();plus.Dispose();hint.Dispose();tray.Visible=false;tray.ContextMenuStrip.Dispose();tray.Dispose();trayIcon.Dispose();taskbar.Dispose();stop.Dispose();if(guardian!=null)guardian.Dispose();}
        base.ExitThreadCore();
    }
}
