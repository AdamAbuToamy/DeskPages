using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Immutable, pre-resolved hit regions. The input thread never calls UI Automation,
// reads files or waits for the UI thread inside a low-level hook.
internal sealed class GroupDragSource {
    public TaskButton Button;public GroupWindow[] Windows;
}
internal sealed class GroupInputMap {
    public long Created;public IntPtr Taskbar;public Rectangle Bounds;public GroupDragSource[] Sources=new GroupDragSource[0];
    public TaskButton[] Groups=new TaskButton[0];
    public GroupDragSource SourceAt(Point point,long now){
        if(now-Created<0 || now-Created>3000 || Groups.Length==0)return null;
        if(Groups.Any(g=>g.Bounds.Contains(point)))return null; // Native group reordering.
        return Sources.FirstOrDefault(s=>s.Button.Bounds.Contains(point));
    }
}
internal sealed class GroupDragGesture {
    public GroupDragSource Source;public TaskButton[] Groups;public Point Press,Release;public IntPtr Taskbar;
    public long Epoch,Started;public volatile bool Cancelled,Released;
}
// Deterministic ownership state: only an unmodified press on a cached live-app
// button is consumed. Mouse motion and presses on groups remain native.
internal sealed class GroupDragGate {
    private GroupDragGesture active;private long epoch;private bool nativePress;
    public long Epoch{get{return Interlocked.Read(ref epoch);}}
    public bool Process(int kind,Point point,bool injected,bool modified,IntPtr root,GroupInputMap map,long now,Action<GroupDragGesture> begin,Action<GroupDragGesture> end,Action nativeEnd=null){
        if(injected)return false;
        if(kind==0x201){
            long current=Interlocked.Increment(ref epoch);
            if(active!=null){active.Cancelled=true;return true;}
            nativePress=root==map.Taskbar;var source=map.SourceAt(point,now);
            if(source!=null && !modified && root==map.Taskbar){active=new GroupDragGesture{Source=source,Groups=map.Groups,Press=point,Taskbar=map.Taskbar,Epoch=current,Started=now};nativePress=false;begin(active);return true;}
        }
        if(kind==0x202 && active!=null){var completed=active;active=null;completed.Release=point;completed.Released=true;end(completed);return true;}
        if(kind==0x202 && nativePress){nativePress=false;if(nativeEnd!=null)nativeEnd();}
        if((kind==0x204 || kind==0x207 || kind==0x20B) && active!=null)Cancel();
        return false;
    }
    public void Cancel(){if(active!=null)active.Cancelled=true;}
    public void Abort(Action<GroupDragGesture> end){var abandoned=active;active=null;nativePress=false;Interlocked.Increment(ref epoch);if(abandoned!=null){abandoned.Cancelled=true;abandoned.Released=true;end(abandoned);}}
}
internal sealed class GroupDragInput:IDisposable {
    private readonly Action<GroupDragGesture> started,finished;private readonly Action refresh;
    private readonly Thread thread;private readonly ManualResetEvent initialized=new ManualResetEvent(false);
    private GroupInputMap map=new GroupInputMap();private readonly GroupDragGate gate=new GroupDragGate();
    private Hook mouse,key;private IntPtr mouseHandle,keyHandle;private uint threadId;
    private Exception error;private int disposed;
    internal const uint ReplayTag=0x44504752;private volatile string lastPress="No taskbar press observed";
    public string Diagnostics{get{return lastPress;}}
    public GroupDragInput(Action<GroupDragGesture> begin,Action<GroupDragGesture> end,Action changed=null){
        started=begin;finished=end;refresh=changed??delegate{};thread=new Thread(Run){IsBackground=true,Name="DeskPages taskbar input"};thread.SetApartmentState(ApartmentState.STA);thread.Start();
        if(!initialized.WaitOne(3000)){Dispose();throw new InvalidOperationException("Taskbar input could not start");}
        if(error!=null){Dispose();throw new InvalidOperationException("Taskbar input could not start",error);}
    }
    public void Publish(GroupInputMap next){Interlocked.Exchange(ref map,next);}
    public void Repair(){PostThreadMessageW(threadId,0x8032,IntPtr.Zero,IntPtr.Zero);}
    public bool IsCurrent(GroupDragGesture gesture){return gate.Epoch==gesture.Epoch;}
    private static long Now{get{return (long)(System.Diagnostics.Stopwatch.GetTimestamp()*1000.0/System.Diagnostics.Stopwatch.Frequency);}}
    private void Run(){
        try{
            threadId=GetCurrentThreadId();NativeMessage message;PeekMessageW(out message,IntPtr.Zero,0,0,0);
            if(Interlocked.CompareExchange(ref disposed,0,0)!=0){initialized.Set();return;}
            mouse=Mouse;key=Key;mouseHandle=SetWindowsHookExW(14,mouse,GetModuleHandleW(null),0);keyHandle=SetWindowsHookExW(13,key,GetModuleHandleW(null),0);
            if(mouseHandle==IntPtr.Zero || keyHandle==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
            initialized.Set();while(GetMessageW(out message,IntPtr.Zero,0,0)>0){
                if(message.Message==0x8032){
                    gate.Abort(finished);Publish(new GroupInputMap());
                    if(mouseHandle!=IntPtr.Zero)UnhookWindowsHookEx(mouseHandle);if(keyHandle!=IntPtr.Zero)UnhookWindowsHookEx(keyHandle);
                    mouseHandle=SetWindowsHookExW(14,mouse,GetModuleHandleW(null),0);keyHandle=SetWindowsHookExW(13,key,GetModuleHandleW(null),0);refresh();
                }else{TranslateMessage(ref message);DispatchMessageW(ref message);}
            }
        }catch(Exception ex){error=ex;initialized.Set();}
        finally{if(mouseHandle!=IntPtr.Zero)UnhookWindowsHookEx(mouseHandle);if(keyHandle!=IntPtr.Zero)UnhookWindowsHookEx(keyHandle);}
    }
    private IntPtr Mouse(int code,IntPtr message,IntPtr data){
        if(code<0)return CallNextHookEx(IntPtr.Zero,code,message,data);
        var input=(MouseData)Marshal.PtrToStructure(data,typeof(MouseData));
        if(input.Extra.ToUInt64()==ReplayTag)return CallNextHookEx(IntPtr.Zero,code,message,data); // Ignore only our own native replay, not device-generated mouse input.
        int kind=message.ToInt32();
        try{
            var current=map;IntPtr actual=kind==0x201?WindowShell.GetAncestor(WindowShell.WindowFromPoint(input.Point),2):IntPtr.Zero;
            IntPtr root=kind==0x201 && WindowShell.OnTaskbar(input.Point,current.Taskbar)?current.Taskbar:actual;
            bool modified=kind==0x201 && ModifierDown();
            bool owned=gate.Process(kind,input.Point,false,modified,root,current,Now,started,finished,delegate{Publish(new GroupInputMap());refresh();});
            if(kind==0x201 && current.Bounds.Contains(input.Point))lastPress="Owned="+owned+"; flags="+input.Flags+"; root="+WindowShell.Class(actual)+"; taskbarMatch="+(root==current.Taskbar)+"; source="+(current.Sources.FirstOrDefault(v=>v.Button.Bounds.Contains(input.Point))??new GroupDragSource{Button=new TaskButton{AppId="none"}}).Button.AppId+"; groups="+current.Groups.Length+"; sources="+current.Sources.Length+"; ageMs="+(Now-current.Created)+"; modified="+modified;
            if(owned)return new IntPtr(1);
        }catch(Exception){gate.Cancel();}
        return CallNextHookEx(IntPtr.Zero,code,message,data);
    }
    private IntPtr Key(int code,IntPtr message,IntPtr data){
        if(code>=0 && Marshal.ReadInt32(data)==0x1B)gate.Cancel();
        return CallNextHookEx(IntPtr.Zero,code,message,data);
    }
    private static bool ModifierDown(){return (WindowShell.GetAsyncKeyState(0x10)&0x8000)!=0 || (WindowShell.GetAsyncKeyState(0x11)&0x8000)!=0 || (WindowShell.GetAsyncKeyState(0x12)&0x8000)!=0 || (WindowShell.GetAsyncKeyState(0x5B)&0x8000)!=0 || (WindowShell.GetAsyncKeyState(0x5C)&0x8000)!=0;}
    // A cancelled/short group drop is consumed. Ordinary clicks and drops between
    // other app buttons replay the user's gesture as one bounded native batch.
    internal static bool Replay(GroupDragGesture gesture,TaskbarSnapshot fresh){
        if(gesture.Cancelled || fresh.Handle!=gesture.Taskbar || !WindowShell.OnTaskbar(gesture.Release,fresh.Handle))return false;
        var source=fresh.At(gesture.Press);if(source==null || source.AppId!=gesture.Source.Button.AppId)return false;
        if(Math.Abs(Cursor.Position.X-gesture.Release.X)+Math.Abs(Cursor.Position.Y-gesture.Release.Y)>4)return false;
        if((WindowShell.GetAsyncKeyState(1)&0x8000)!=0 || ModifierDown())return false;
        var end=fresh.At(gesture.Release);if(end==null)return false;
        var area=SystemInformation.VirtualScreen;
        bool moved=Math.Abs(gesture.Release.X-gesture.Press.X)+Math.Abs(gesture.Release.Y-gesture.Press.Y)>SystemInformation.DragSize.Width;
        Point origin=moved?gesture.Press:gesture.Release;
        var batch=moved?new[]{Move(origin,area),Button(2),Move(new Point(origin.X+(gesture.Release.X-origin.X)/2,origin.Y+(gesture.Release.Y-origin.Y)/2),area),Move(gesture.Release,area),Button(4)}:new[]{Move(origin,area),Button(2),Button(4)};
        uint sent=SendInput((uint)batch.Length,batch,Marshal.SizeOf(typeof(Input)));
        if(sent!=(uint)batch.Length){var release=new[]{Button(4)};SendInput(1,release,Marshal.SizeOf(typeof(Input)));return false;}return true;
    }
    private static Input Move(Point point,Rectangle area){return new Input{Type=0,Mouse=new MouseInput{X=(int)Math.Round((point.X-area.Left)*65535.0/Math.Max(1,area.Width-1)),Y=(int)Math.Round((point.Y-area.Top)*65535.0/Math.Max(1,area.Height-1)),Flags=0xE001,Extra=new UIntPtr(ReplayTag)}};}
    private static Input Button(uint flag){return new Input{Type=0,Mouse=new MouseInput{Flags=flag,Extra=new UIntPtr(ReplayTag)}};}
    public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;gate.Cancel();if(threadId!=0)PostThreadMessageW(threadId,0x12,IntPtr.Zero,IntPtr.Zero);if(thread!=Thread.CurrentThread && thread.Join(1000))initialized.Dispose();}
    private delegate IntPtr Hook(int code,IntPtr message,IntPtr data);
    [StructLayout(LayoutKind.Sequential)]private struct MouseData{public Point Point;public uint Data,Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Sequential)]private struct NativeMessage{public IntPtr Window;public uint Message;public UIntPtr W;public IntPtr L;public uint Time;public Point Point;public uint Private;}
    [StructLayout(LayoutKind.Sequential)]private struct MouseInput{public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Explicit,Size=40)]private struct Input{[FieldOffset(0)]public uint Type;[FieldOffset(8)]public MouseInput Mouse;}
    [DllImport("user32.dll",SetLastError=true)]private static extern IntPtr SetWindowsHookExW(int kind,Hook callback,IntPtr module,uint thread);
    [DllImport("user32.dll")]private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr GetModuleHandleW(string name);
    [DllImport("kernel32.dll")]private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]private static extern bool PeekMessageW(out NativeMessage message,IntPtr window,uint min,uint max,uint remove);
    [DllImport("user32.dll")]private static extern int GetMessageW(out NativeMessage message,IntPtr window,uint min,uint max);
    [DllImport("user32.dll")]private static extern bool TranslateMessage(ref NativeMessage message);
    [DllImport("user32.dll")]private static extern IntPtr DispatchMessageW(ref NativeMessage message);
    [DllImport("user32.dll")]private static extern bool PostThreadMessageW(uint thread,uint message,IntPtr w,IntPtr l);
    [DllImport("user32.dll",SetLastError=true)]private static extern uint SendInput(uint count,Input[] input,int size);
}
