using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// RAM hot images plus user-bound encrypted local snapshots; no timed expiry.
public sealed class DeskPagesSnapshotSession:IDisposable {
    public static readonly string Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPages","HiddenPages");
    static string ModePath {get{return Path.Combine(Folder,"capture-settings.txt");}}
    public bool Enabled {get;private set;}
    readonly List<FileSystemWatcher> watchers=new List<FileSystemWatcher>();
    long dirtyAt,nextReport,nextHook,retryAt;int pending=1,observerFailed,observerUnhealthy,scopeDirty=1;bool disposed;
    string sceneContext="",scope="";bool storeReady;
    public void SceneContext(string value){value=value??"";if(sceneContext!=value){sceneContext=value;Invalidate();}}
    public bool EnsureScene(){
        if(!Enabled)return false;if(Volatile.Read(ref observerUnhealthy)!=0)Interlocked.Exchange(ref scopeDirty,1);if(Interlocked.Exchange(ref scopeDirty,0)==0 && storeReady)return true;
        string next=DeskPagesSnapshotScope.Hash(sceneContext);
        if(!storeReady){
            string folder=Path.Combine(Folder,"Snapshots");Directory.CreateDirectory(folder);
            var acl=new System.Security.AccessControl.DirectorySecurity();acl.SetAccessRuleProtection(true,false);
            var inherit=System.Security.AccessControl.InheritanceFlags.ContainerInherit|System.Security.AccessControl.InheritanceFlags.ObjectInherit;
            foreach(var sid in new[]{System.Security.Principal.WindowsIdentity.GetCurrent().User,new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid,null)})acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(sid,System.Security.AccessControl.FileSystemRights.FullControl,inherit,System.Security.AccessControl.PropagationFlags.None,System.Security.AccessControl.AccessControlType.Allow));
            Directory.SetAccessControl(folder,acl);DeskPagesAnimation.ConfigureSnapshotStore(folder,next);storeReady=true;
        }
        if(scope!=next){DeskPagesAnimation.InvalidateSnapshots();DeskPagesAnimation.ConfigureSnapshotStore(Path.Combine(Folder,"Snapshots"),next);scope=next;DeskPagesStartupGate.Log("snapshot scene changed; encrypted records validated by scope");}
        return true;
    }
    public string KnownKey(int page){return EnsureScene()?DeskPagesAnimation.SavedSnapshotKey(page):"";}
    public bool NeedsCapture(string key){int page;if(!EnsureScene() || !int.TryParse(key.Split(':')[0],out page))return true;return KnownKey(page)!=key;}
    public void Captured(string key,bool ok){if(!ok){retryAt=Now+250;RequestCheck();}}
    [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
    int currentPage;
    public void SetPage(int page){Volatile.Write(ref currentPage,page);}
    Stopwatch navigation;long firstSlide=-1;ulong captureAtStart;
    public void MarkSlideStarted(){if(navigation!=null)firstSlide=navigation.ElapsedMilliseconds;}
    delegate void WinEvent(IntPtr hook,uint evt,IntPtr hwnd,int obj,int child,uint thread,uint time);
    readonly WinEvent callback;IntPtr hook;uint explorerPid;
    [DllImport("user32.dll")]static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr dll,WinEvent cb,uint pid,uint tid,uint flags);
    [DllImport("user32.dll")]static extern bool UnhookWinEvent(IntPtr value);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindowW(string cls,string title);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassNameW(IntPtr hwnd,StringBuilder name,int size);
    static string Class(IntPtr hwnd){var b=new StringBuilder(80);GetClassNameW(hwnd,b,b.Capacity);return b.ToString();}
    static long Now {get{return (long)(Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency);}}
    public DeskPagesSnapshotSession(){
        Directory.CreateDirectory(Folder);bool saved;try{Enabled=File.Exists(ModePath)&&bool.TryParse(File.ReadAllText(ModePath).Trim(),out saved)&&saved;}catch(IOException){Enabled=false;}catch(UnauthorizedAccessException){Enabled=false;}
        callback=Changed;if(Enabled)RefreshHook();
        foreach(var path in new[]{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)}){
            if(!Directory.Exists(path))continue;
            try{var w=new FileSystemWatcher(path){IncludeSubdirectories=false,NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName|NotifyFilters.LastWrite|NotifyFilters.Size|NotifyFilters.CreationTime};
                w.Changed+=delegate(object sender,FileSystemEventArgs args){if(Directory.Exists(args.FullPath))RequestCheck();else Invalidate();};w.Created+=delegate{Invalidate();};w.Deleted+=delegate{Invalidate();};w.Renamed+=delegate{Invalidate();};w.Error+=delegate{Interlocked.Exchange(ref observerUnhealthy,1);Interlocked.Exchange(ref observerFailed,1);Invalidate();};w.EnableRaisingEvents=Enabled;watchers.Add(w);
            }catch(Exception ex){DeskPagesStartupGate.Log("snapshot watcher: "+ex.Message);Enabled=false;}
        }
        if(watchers.Count==0 || hook==IntPtr.Zero)Enabled=false;
        DeskPagesAnimation.SnapshotCacheEnabled=Enabled;
        try{using(var p=Process.GetCurrentProcess())File.WriteAllLines(Path.Combine(Folder,"performance-agent.pid"),new[]{p.Id.ToString(),p.StartTime.ToUniversalTime().Ticks.ToString()});}catch(IOException){}catch(UnauthorizedAccessException){}
    }
    void RefreshHook(){uint pid;GetWindowThreadProcessId(FindWindowW("Progman",null),out pid);if(pid==explorerPid && hook!=IntPtr.Zero)return;
        if(hook!=IntPtr.Zero)UnhookWinEvent(hook);explorerPid=pid;
        hook=pid==0?IntPtr.Zero:SetWinEventHook(0x8000,0x8018,IntPtr.Zero,callback,pid,0,2);
        Invalidate();
    }
    void Changed(IntPtr h,uint evt,IntPtr hwnd,int obj,int child,uint tid,uint at){
        if(disposed || !Enabled || DeskPagesAnimation.IsVisible || hwnd==IntPtr.Zero || !DeskPagesSnapshotInvalidation.ChangesImage(evt))return;
        if((evt==0x800b || evt==0x800c) && child<=0)return;
        string root=Class(GetAncestor(hwnd,2));if((root=="Progman" || root=="WorkerW") && (Class(hwnd)=="SysListView32" || Class(hwnd)=="SHELLDLL_DefView" || obj==0))RequestCheck();
    }
    public void RequestLayoutRefresh(){retryAt=0;RequestCheck();Interlocked.Exchange(ref dirtyAt,Now-40);}
    void RequestCheck(){if(disposed)return;Interlocked.Exchange(ref dirtyAt,Now);Interlocked.Exchange(ref pending,1);}
    public void Invalidate(){Interlocked.Exchange(ref scopeDirty,1);RequestCheck();}
    public void SetEnabled(bool value){
        // Never enable a stale cache when file or shell event observation failed.
        if(value)RefreshHook();
        if(value && (watchers.Count==0 || hook==IntPtr.Zero))throw new InvalidOperationException("Desktop change tracking is unavailable. Use direct capture.");
        DeskPagesStartupGate.AtomicText(ModePath,value.ToString());Enabled=value;DeskPagesAnimation.SnapshotCacheEnabled=value;foreach(var w in watchers)w.EnableRaisingEvents=value;
        if(!value && hook!=IntPtr.Zero){UnhookWinEvent(hook);hook=IntPtr.Zero;}Invalidate();
        if(!value)DeskPagesAnimation.ClearSnapshots();
    }
    public bool ShouldWarm(){
        if(!Enabled || disposed || Now<retryAt)return false;
        if(Interlocked.CompareExchange(ref pending,0,0)==0 || Now-Interlocked.Read(ref dirtyAt)<40 || (GetAsyncKeyState(1)&0x8000)!=0)return false;
        if(!EnsureScene())return false;Interlocked.Exchange(ref pending,0);return true;
    }
    public void BeginSample(){navigation=Stopwatch.StartNew();firstSlide=-1;captureAtStart=DeskPagesAnimation.CaptureCalls;DeskPagesAnimation.SnapshotSourceKey="";DeskPagesAnimation.SnapshotTargetKey="";}
    public void Source(string key){DeskPagesAnimation.SnapshotSourceKey=key;}
    public void Target(string key){DeskPagesAnimation.SnapshotTargetKey=key;}
    public void EndSample(bool success){
        if(navigation==null)return;navigation.Stop();
        try{using(var p=Process.GetCurrentProcess()){
            string path=Path.Combine(Folder,"transition-performance-v3.csv");
            if(!File.Exists(path))File.WriteAllText(path,"Utc,Mode,Success,CommandToSlideMs,SourcePrepareMs,TargetPrepareMs,CacheBytes,SourceHits,TargetHits,CacheMisses,CaptureCalls,WorkingSetBytes,PrivateBytes,GCBytes,Handles,DesktopReadyMs,CachedPreview,CaptureCallsDuringNavigation,DiskHits,DiskWrites,DiskErrors\r\n");
            File.AppendAllText(path,DateTime.UtcNow.ToString("o")+","+(Enabled?"cached":"direct")+","+success+","+(firstSlide>=0?firstSlide:navigation.ElapsedMilliseconds)+","+DeskPagesAnimation.SnapshotValuesCsv()+","+p.WorkingSet64+","+p.PrivateMemorySize64+","+GC.GetTotalMemory(false)+","+p.HandleCount+","+navigation.ElapsedMilliseconds+","+(firstSlide>=0)+","+(DeskPagesAnimation.CaptureCalls-captureAtStart)+","+DeskPagesAnimation.DiskValuesCsv()+"\r\n");
        }}catch(Exception ex){DeskPagesStartupGate.Log("performance sample: "+ex.Message);}finally{navigation=null;DeskPagesAnimation.SnapshotSourceKey="";DeskPagesAnimation.SnapshotTargetKey="";}
    }
    public void Poll(){
        if(Interlocked.Exchange(ref observerFailed,0)!=0){
            bool healthy=true;foreach(var watcher in watchers)try{watcher.EnableRaisingEvents=false;watcher.EnableRaisingEvents=Enabled;}catch(Exception ex){healthy=false;DeskPagesStartupGate.Log("snapshot watcher retry: "+ex.Message);}
            Interlocked.Exchange(ref observerUnhealthy,healthy?0:1);if(!healthy)Interlocked.Exchange(ref observerFailed,1);Invalidate();DeskPagesStartupGate.Log("snapshot watcher rescan; caching preference retained");
        }
        long now=Now;if(Enabled && now>=nextHook){nextHook=now+2000;RefreshHook();}if(now<nextReport)return;nextReport=now+5000;
        try{File.WriteAllText(Path.Combine(Folder,"snapshot-resources.txt"),DeskPagesAnimation.SnapshotCacheBytes.ToString());
            string request=Path.Combine(Folder,"capture-mode.request");if(File.Exists(request)){bool value;if(bool.TryParse(File.ReadAllText(request).Trim(),out value))SetEnabled(value);File.Delete(request);}
        }catch(IOException){}catch(UnauthorizedAccessException){}
    }
    public void Dispose(){disposed=true;if(hook!=IntPtr.Zero)UnhookWinEvent(hook);foreach(var w in watchers)w.Dispose();watchers.Clear();DeskPagesAnimation.ClearSnapshots();try{string path=Path.Combine(Folder,"performance-agent.pid");if(File.Exists(path) && File.ReadAllLines(path)[0]==Process.GetCurrentProcess().Id.ToString())File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}}
}
