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
    private readonly GroupCommandWindow commands;private readonly GroupCreateSignal createRequest;private readonly GroupDropCue cue=new GroupDropCue();
    private readonly GroupDragGhost ghost=new GroupDragGhost();private readonly GroupDragInput input;private readonly GroupPopup groupPopup;
    private GroupDragGesture captured;private int replayBusy,windowsBusy;private GroupDragSource[] resolvedSources=new GroupDragSource[0];private GroupWindowPicker picker;
    private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=40};
    private readonly GroupDropDwell dwell=new GroupDropDwell();
    private readonly EventWaitHandle stop=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\DeskPagesTaskbarGroupsStop-"+System.Security.Principal.WindowsIdentity.GetCurrent().User.Value);
    private TaskbarSnapshot snapshot=new TaskbarSnapshot();private GroupWindow[] drag=new GroupWindow[0];
    private Point press;private bool held,moved,ready,disposed;private int readBusy,generation;private long lastRead,lastWindowRead;
    private readonly GroupWindowEvents windowEvents=new GroupWindowEvents();
    private long refreshRevision=1,seenRefreshRevision,seenWindowRevision,resolvedWindowRevision,lastGeometryRead;private bool pointerOnBar,wasOnBar;
    private string resolvedButtons="";
    private LiveGroup target;private Process guardian;private int resolveBusy;
    private TaskButton[] pressGroups=new TaskButton[0];private string sourceAppId;
    private static long Now{get{return (long)(Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency);}}
    public GroupController(GroupCreateSignal request){
        createRequest=request;
        model=new GroupModel(session);recovery=new GroupRecovery(session);commands=new GroupCommandWindow(AddGroup);var uiHandle=commands.Handle;
        groupPopup=new GroupPopup(commands,g=>model.Find(g.AppId)==g,DeleteGroup,WindowShell.Activate,BeginMemberDrag,ChooseOpenApps);
        Directory.CreateDirectory(Path.GetDirectoryName(EnabledPath));stop.Reset();
        StartGuardian();input=new GroupDragInput(g=>Post(delegate{BeginCaptured(g);}),g=>Post(delegate{EndCaptured(g);}),delegate{Post(RefreshSoon);},(id,point)=>Post(delegate{var group=model.Find(id);if(group!=null)groupPopup.Show(group,point);}));snapshot=TaskbarAccess.ReadBounds();timer.Tick+=delegate{Tick();};timer.Start();if(commands.RegistrationCount==0)Post(delegate{Notify("The group shortcut is in use. Add groups from the DeskPages tray menu.");});
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
        if(disposed)return;
        LiveGroup group=null;GroupAnchor anchor=null;
        try{
            group=model.Add();var added=group;anchor=new GroupAnchor(group,delegate{OpenGroup(added);});
            // Set the window identity before Explorer sees its first visible state.
            WindowShell.Set(anchor.Handle,5,group.AppId);anchors.Add(group.Number,anchor);
            anchor.Show();taskbar.Add(anchor.Handle);
            RefreshSoon();
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
            if(createRequest.Take())AddGroup();
            var pointer=Cursor.Position;pointerOnBar=snapshot.Bounds.Contains(pointer);
            if(pointerOnBar && !wasOnBar)RefreshSoon();wasOnBar=pointerOnBar;
            bool interacting=pointerOnBar || captured!=null || drag.Length>0;
            bool dragging=captured!=null || drag.Length>0 || (held && pointerOnBar);
            if(Now-lastGeometryRead>=500){lastGeometryRead=Now;var bounds=TaskbarAccess.ReadBounds();if(bounds.Handle!=snapshot.Handle || bounds.Bounds!=snapshot.Bounds)RefreshSoon();}
            bool changed=seenRefreshRevision!=Interlocked.Read(ref refreshRevision) || seenWindowRevision!=windowEvents.Revision;
            if(GroupRefreshPolicy.Due(Now,lastRead,changed,interacting,dragging) && Interlocked.CompareExchange(ref readBusy,1,0)==0){
                lastRead=Now;seenRefreshRevision=Interlocked.Read(ref refreshRevision);seenWindowRevision=windowEvents.Revision;ReadSnapshot(interacting);
            }
            if(captured!=null){if(Now-captured.Started>30000){captured.Cancelled=true;input.Repair();EndCaptured(captured);}else UpdateCaptured(Cursor.Position);return;}
            bool down=(WindowShell.GetAsyncKeyState(1)&0x8000)!=0;Point point=Cursor.Position;
            if((WindowShell.GetAsyncKeyState(0x1B)&0x8000)!=0){CancelDrag();held=down;return;}
            if(down && !held){
                var root=WindowShell.GetAncestor(WindowShell.WindowFromPoint(point),2);bool thumbnail=WindowShell.Class(root)=="TaskListThumbnailWnd";
                if(pointerOnBar || thumbnail){press=point;moved=false;drag=new GroupWindow[0];sourceAppId=null;pressGroups=snapshot.Buttons.Where(b=>model.Find(b.AppId)!=null).ToArray();RefreshSoon();int request=++generation;if(thumbnail)ResolvePress(point,request);}
            }
            if(down && drag.Length>0){
                moved=moved || Math.Abs(point.X-press.X)+Math.Abs(point.Y-press.Y)>SystemInformation.DragSize.Width;
                var button=TaskbarAccess.DropTarget(snapshot,pressGroups,point,sourceAppId,id=>model.Find(id)!=null);target=moved && button!=null?model.Find(button.AppId):null;
                ready=dwell.Observe(target==null?null:target.AppId,Now);
                if(target!=null)cue.Present(button.Bounds,ready);else cue.Hide();
            }
            if(!down && held && (drag.Length>0 || sourceAppId!=null)){
                if(moved && drag.Length>0){
                    if(ready && target!=null && TaskbarAccess.DropTarget(snapshot,pressGroups,point,sourceAppId,id=>model.Find(id)!=null)!=null)VerifyDrop(target,drag,point);
                    else if(target==null && (Math.Abs(point.X-press.X)+Math.Abs(point.Y-press.Y)>48))foreach(var w in drag)if(model.Owner(w)!=null){recovery.Restore(w);if(!recovery.Owns(w))model.Remove(w);}
                }
                CancelDrag();Prune();RefreshSoon();
            }
            held=down;
        }catch(Exception ex){CancelDrag();Notify(ex.Message);}
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
                if(drag.Length==0 && button!=null)Notify("No unambiguous running window was found for this button. Closed pinned apps are not grouped.");
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
                try{Assign(group,items);}catch(Exception ex){Notify(ex.Message);}
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
        // Input stays native until the pointer enters a numbered target.
        target=model.Find(captured.TargetAppId);ready=target!=null;
        if(target!=null)cue.Present(captured.TargetBounds,true);else cue.Hide();
    }
    private void EndCaptured(GroupDragGesture gesture){
        if(captured!=gesture)return;
        var destination=gesture.TargetAppId==null?null:model.Find(gesture.TargetAppId);
        captured=null;held=!gesture.Released;ghost.Hide();CancelDrag();int request=generation;
        // Leaving the target resumes Explorer immediately; there is no delayed
        // click/reorder replay when the user releases elsewhere on the taskbar.
        if(!gesture.Released){RefreshSoon();return;}
        if(gesture.Cancelled || destination==null)return;
        if(Interlocked.CompareExchange(ref replayBusy,1,0)!=0)return;
        ThreadPool.QueueUserWorkItem(delegate{
            TaskbarSnapshot fresh=null;GroupWindow[] windows=null;
            try{fresh=TaskbarAccess.Read();windows=TaskbarAccess.Resolve(gesture.Source.Button,WindowShell.Windows(),fresh.Buttons.Select(b=>b.AppId).ToArray());}
            catch(Exception){}finally{Interlocked.Exchange(ref replayBusy,0);}
            Post(delegate{
                if(disposed || request!=generation || !input.IsCurrent(gesture) || gesture.Cancelled || fresh==null || fresh.Handle!=gesture.Taskbar || model.Find(destination.AppId)!=destination)return;
                var approved=new[]{new TaskButton{AppId=destination.AppId,Bounds=gesture.TargetBounds}};
                var hit=TaskbarAccess.DropTarget(fresh,approved,gesture.Release,gesture.Source.Button.AppId,id=>id==destination.AppId);
                if(hit==null || hit.AppId!=destination.AppId || !WindowShell.OnTaskbar(gesture.Release,fresh.Handle))return;
                if(windows==null || windows.Length==0){Notify("This taskbar button could not be matched to a running window. No application was changed.");return;}
                if(gesture.Source.Windows.Length>0 && !windows.Select(w=>w.Key).OrderBy(k=>k).SequenceEqual(gesture.Source.Windows.Select(w=>w.Key).OrderBy(k=>k)))return;
                Assign(destination,windows);RefreshSoon();
            });
        });
    }
    private void ReadSnapshot(bool interacting){
        var ids=model.Groups.Select(g=>g.AppId).ToArray();long eventRevision=windowEvents.Revision;
        ThreadPool.QueueUserWorkItem(delegate{
            long observedAt=Now;TaskbarSnapshot next=null;
            try{next=TaskbarAccess.Read();}catch(Exception){}finally{Interlocked.Exchange(ref readBusy,0);}
            if(next==null)return;
            var cache=Interlocked.CompareExchange(ref resolvedSources,null,null);
            var regions=new GroupInputMap{Created=observedAt,DragDistance=SystemInformation.DragSize.Width,Taskbar=next.Handle,Bounds=next.Bounds,Groups=next.Buttons.Where(b=>ids.Contains(b.AppId)).ToArray(),Sources=next.Buttons.Where(b=>!ids.Contains(b.AppId)).Select(b=>new GroupDragSource{Button=b,Windows=(cache.FirstOrDefault(c=>c.Button.AppId==b.AppId)??new GroupDragSource{Windows=new GroupWindow[0]}).Windows}).ToArray()};
            input.Publish(regions);
            Post(delegate{bool recreated=snapshot.Handle!=next.Handle;snapshot=next;Prune();if(recreated){taskbar.Reset();foreach(var g in model.Groups)if(!g.Used)taskbar.Add(anchors[g.Number].Handle);}});
            // Window/shortcut resolution cannot stall publication of hit regions.
            var liveIds=next.Buttons.Select(b=>b.AppId).ToArray();string buttonSignature=string.Join("\n",liveIds.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase));
            bool resolveChanged=resolvedWindowRevision!=eventRevision || resolvedButtons!=buttonSignature;
            if(ids.Length>0 && GroupRefreshPolicy.ResolveWindows(Now,lastWindowRead,interacting,resolveChanged) && Interlocked.CompareExchange(ref windowsBusy,1,0)==0){lastWindowRead=Now;ThreadPool.QueueUserWorkItem(delegate{
                try{var windows=WindowShell.Windows();var sources=next.Buttons.Select(b=>new GroupDragSource{Button=b,Windows=TaskbarAccess.Resolve(b,windows,liveIds)}).ToArray();Interlocked.Exchange(ref resolvedSources,sources);resolvedWindowRevision=eventRevision;resolvedButtons=buttonSignature;}
                catch(Exception){}finally{Interlocked.Exchange(ref windowsBusy,0);}
            });}
        });
    }
    private void ChooseOpenApps(LiveGroup group){
        if(model.Find(group.AppId)!=group)return;
        if(picker!=null && !picker.IsDisposed){picker.Activate();return;}
        picker=new GroupWindowPicker(group.Number,delegate(GroupWindow[] chosen){if(model.Find(group.AppId)!=group)throw new InvalidOperationException("The group was removed");Assign(group,chosen);});
        picker.FormClosed+=delegate{picker=null;};picker.Show();
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
    private void DeleteGroup(LiveGroup group){
        // Restore durable ownership before removing the numbered anchor. A failed
        // restore leaves that member and the group available for a safe retry.
        foreach(var window in group.Windows.Values.ToArray()){recovery.Restore(window);if(!recovery.Owns(window))model.Remove(window);}
        if(group.Windows.Count!=0)throw new InvalidOperationException("Some applications could not be restored. Retry deleting this group.");
        model.DeleteEmpty(group);RemoveAnchor(group);RefreshSoon();
    }
    private void BeginMemberDrag(GroupWindow window){CancelDrag();drag=new[]{window};press=Cursor.Position;held=true;}
    private void RefreshSoon(){Interlocked.Increment(ref refreshRevision);}
    private void CancelDrag(){generation++;drag=new GroupWindow[0];pressGroups=new TaskButton[0];sourceAppId=null;target=null;ready=false;moved=false;dwell.Reset();cue.Hide();}
    private void Notify(string message){if(!disposed)MessageBox.Show(commands,message,"DeskPages groups",MessageBoxButtons.OK,MessageBoxIcon.Information);}
    private void Post(Action action){if(disposed)return;try{commands.BeginInvoke((Action)delegate{if(!disposed)try{action();}catch(Exception ex){Notify(ex.Message);}});}catch(InvalidOperationException){ }}
    protected override void ExitThreadCore(){
        if(disposed)return;disposed=true;if(captured!=null)captured.Cancelled=true;input.Dispose();windowEvents.Dispose();timer.Stop();timer.Dispose();
        try{recovery.RestoreAll();}catch(Exception){/* The independent guardian retries the durable journal after exit. */}
        finally{foreach(var group in model.Groups)if(anchors.ContainsKey(group.Number))RemoveAnchor(group);if(picker!=null && !picker.IsDisposed)picker.Close();groupPopup.Dispose();ghost.Dispose();cue.Dispose();commands.Dispose();taskbar.Dispose();stop.Dispose();if(guardian!=null)guardian.Dispose();}
        base.ExitThreadCore();
    }
}
