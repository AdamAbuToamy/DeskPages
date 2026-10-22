using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal sealed class GroupDragInput:IDisposable {
    private readonly Action<GroupDragGesture> started,finished;private readonly Action refresh;
    private readonly Thread thread,sender;private readonly BlockingCollection<Action> native=new BlockingCollection<Action>();
    private sealed class NativeBatch {public int Remaining;public bool Resume;public GroupDragGesture Gesture;}
    private readonly ConcurrentDictionary<ulong,NativeBatch> pending=new ConcurrentDictionary<ulong,NativeBatch>();private int sequence;private readonly ManualResetEvent initialized=new ManualResetEvent(false);
    private GroupInputMap map=new GroupInputMap();private readonly GroupDragGate gate=new GroupDragGate();
    private Hook mouse,key;private IntPtr mouseHandle,keyHandle;private uint threadId;
    private Exception error;private int disposed,handoffs;private GroupDragGesture handingOff;
    internal const uint ReplayTag=0x44504752;private volatile string lastPress="No taskbar press observed";
    private volatile string lastMotion="none",lastTransfer="none",lastNative="none";
    public string Diagnostics{get{return lastPress+"; motion="+lastMotion+"; transfer="+lastTransfer+"; native="+lastNative;}}
    public GroupDragInput(Action<GroupDragGesture> begin,Action<GroupDragGesture> end,Action changed=null){
        started=begin;finished=end;refresh=changed??delegate{};sender=new Thread(SendNative){IsBackground=true,Name="DeskPages native drag continuation"};sender.Start();thread=new Thread(Run){IsBackground=true,Name="DeskPages taskbar input"};thread.SetApartmentState(ApartmentState.STA);thread.Start();
        if(!initialized.WaitOne(3000)){Dispose();throw new InvalidOperationException("Taskbar input could not start");}
        if(error!=null){Dispose();throw new InvalidOperationException("Taskbar input could not start",error);}
    }
    public void Publish(GroupInputMap next){Interlocked.Exchange(ref map,next);if(threadId!=0)PostThreadMessageW(threadId,0x8033,IntPtr.Zero,IntPtr.Zero);}
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
                    gate.Abort(FinishOrdered);Publish(new GroupInputMap());
                    if(mouseHandle!=IntPtr.Zero)UnhookWindowsHookEx(mouseHandle);if(keyHandle!=IntPtr.Zero)UnhookWindowsHookEx(keyHandle);
                    mouseHandle=SetWindowsHookExW(14,mouse,GetModuleHandleW(null),0);keyHandle=SetWindowsHookExW(13,key,GetModuleHandleW(null),0);refresh();
                }else if(message.Message==0x8033){if(gate.Owned && gate.Current!=null)ProcessGesture(0x200,gate.Current.LastPhysical,IntPtr.Zero,map,false);}else{TranslateMessage(ref message);DispatchMessageW(ref message);}
            }
        }catch(Exception ex){error=ex;initialized.Set();}
        finally{if(mouseHandle!=IntPtr.Zero)UnhookWindowsHookEx(mouseHandle);if(keyHandle!=IntPtr.Zero)UnhookWindowsHookEx(keyHandle);}
    }
    private IntPtr Mouse(int code,IntPtr message,IntPtr data){
        if(code<0)return CallNextHookEx(IntPtr.Zero,code,message,data);
        var input=(MouseData)Marshal.PtrToStructure(data,typeof(MouseData));
        ulong token=input.Extra.ToUInt64();
        if((token&0xFFFFFFFFUL)==ReplayTag){
            NativeBatch batch;pending.TryGetValue(token,out batch);
            // A physical release can race a queued continuation. Never deliver
            // its synthetic down after release; that would leave a button held.
            bool skip=batch!=null && batch.Resume && (batch.Gesture.Released || batch.Gesture.Cancelled);
            IntPtr result=skip?new IntPtr(1):CallNextHookEx(IntPtr.Zero,code,message,data);
            if(batch!=null)Interlocked.Decrement(ref batch.Remaining);return result;
        }
        int kind=message.ToInt32();
        try{
            var current=map;IntPtr actual=kind==0x201?WindowShell.GetAncestor(WindowShell.WindowFromPoint(input.Point),2):IntPtr.Zero;
            IntPtr root=kind==0x201 && WindowShell.OnTaskbar(input.Point,current.Taskbar)?current.Taskbar:actual;
            bool modified=kind==0x201 && ModifierDown();
            bool pendingMove=kind==0x200 && Volatile.Read(ref handoffs)>0;
            bool owned=ProcessGesture(kind,input.Point,root,current,modified);
            if(pendingMove && !owned){QueueNative(new[]{Move(input.Point,SystemInformation.VirtualScreen)},handingOff,false);owned=true;}
            if(kind==0x200 && current.Groups.Any(g=>g.Bounds.Contains(input.Point)))lastMotion="point="+input.Point+", owned="+owned+", "+gate.Describe();
            if(kind==0x201 && current.Bounds.Contains(input.Point))lastPress="Owned="+owned+"; flags="+input.Flags+"; root="+WindowShell.Class(actual)+"; taskbarMatch="+(root==current.Taskbar)+"; source="+(current.Sources.FirstOrDefault(v=>v.Button.Bounds.Contains(input.Point))??new GroupDragSource{Button=new TaskButton{AppId="none"}}).Button.AppId+"; groups="+current.Groups.Length+"; sources="+current.Sources.Length+"; ageMs="+(Now-current.Created)+"; modified="+modified+"; gate="+gate.Describe();
            if(owned)return new IntPtr(1);
        }catch(Exception){gate.Cancel();}
        return CallNextHookEx(IntPtr.Zero,code,message,data);
    }
    private bool ProcessGesture(int kind,Point point,IntPtr root,GroupInputMap current,bool modified){
        return gate.Process(kind,point,false,modified,root,current,Now,BeginOrdered,FinishOrdered,refresh,
            delegate(GroupDragGesture gesture,Point target){return TransferToGroup(gesture,target);},
            delegate(GroupDragGesture gesture,Point target){return ResumeNative(gesture,target,current);},
            delegate(Point target){var gesture=gate.Current;QueueNative(new[]{Move(target,SystemInformation.VirtualScreen)},gesture,false);});
    }
    private IntPtr Key(int code,IntPtr message,IntPtr data){
        if(code>=0 && Marshal.ReadInt32(data)==0x1B){bool owned=gate.Owned;gate.Cancel();if(owned)return new IntPtr(1);}
        return CallNextHookEx(IntPtr.Zero,code,message,data);
    }
    private static bool ModifierDown(){return (WindowShell.GetAsyncKeyState(0x10)&0x8000)!=0 || (WindowShell.GetAsyncKeyState(0x11)&0x8000)!=0 || (WindowShell.GetAsyncKeyState(0x12)&0x8000)!=0 || (WindowShell.GetAsyncKeyState(0x5B)&0x8000)!=0 || (WindowShell.GetAsyncKeyState(0x5C)&0x8000)!=0;}
    // Complete Explorer's drag at its last native point before forwarding a move
    // over the numbered target. From here only DeskPages receives the physical up.
    private bool TransferToGroup(GroupDragGesture gesture,Point point){
        var safe=GroupDragGeometry.NativeEnd(gesture,SystemInformation.DragSize.Width);lastTransfer="dragSize="+SystemInformation.DragSize.Width+", box="+gesture.Source.Button.Bounds+", safe="+(safe.HasValue?safe.Value.ToString():"none");if(!safe.HasValue)return false;
        var area=SystemInformation.VirtualScreen;
        handingOff=gesture;Interlocked.Increment(ref handoffs);
        bool queued=QueueNative(new[]{Move(safe.Value,area),Button(4),Move(point,area)},gesture,true);
        if(!queued)Interlocked.Decrement(ref handoffs);return queued;
    }
    private bool ResumeNative(GroupDragGesture gesture,Point point,GroupInputMap current){
        if(ModifierDown() || !current.Fresh(Now) || Now-current.Created>250 || current.Created<gesture.TransferredAt+80 || current.Taskbar!=gesture.Taskbar)return false;
        var source=current.Sources.FirstOrDefault(s=>s.Button.AppId==gesture.Source.Button.AppId);
        if(source==null)return false;
        var box=source.Button.Bounds;var origin=new Point(box.Left+box.Width/2,box.Top+box.Height/2);
        if(!WindowShell.OnTaskbar(origin,current.Taskbar))return false;
        var area=SystemInformation.VirtualScreen;
        return QueueNative(new[]{Move(origin,area),Button(2),Move(point,area)},gesture,false,current.Created);
    }
    // SendInput must never run inside WH_MOUSE_LL: Windows can wait for this
    // hook while delivering the injected batch. Serialize sends on another thread.
    private void SendNative(){foreach(var action in native.GetConsumingEnumerable())try{action();}catch(Exception){Repair();}}
    private bool QueueNative(Input[] batch,GroupDragGesture gesture,bool finishNative,long observedAt=0){
        if(Interlocked.CompareExchange(ref disposed,0,0)!=0)return false;
        try{native.Add(delegate{
            try{
                if(Interlocked.CompareExchange(ref disposed,0,0)!=0)return;
                if(gesture!=null && gesture.Cancelled && !finishNative)return;
                if(observedAt!=0 && gesture.Released)return;
                if(observedAt!=0 && Now-observedAt>250){if(gesture!=null)gesture.Cancelled=true;Repair();return;}
                var send=finishNative && gesture.Cancelled?batch.Take(2).ToArray():batch;
                if(!Send(send,gesture,observedAt!=0)){if(gesture!=null)gesture.Cancelled=true;Repair();}
            }finally{if(finishNative)Interlocked.Decrement(ref handoffs);}
        });return true;}catch(InvalidOperationException){return false;}
    }
    private void BeginOrdered(GroupDragGesture gesture){
        if(Interlocked.CompareExchange(ref disposed,0,0)!=0)return;
        try{native.Add(delegate{started(gesture);});}catch(InvalidOperationException){}
    }
    private void FinishOrdered(GroupDragGesture gesture){
        if(Interlocked.CompareExchange(ref disposed,0,0)!=0)return;
        try{native.Add(delegate{finished(gesture);});}catch(InvalidOperationException){}
    }
    private bool Send(Input[] batch,GroupDragGesture gesture,bool resume){
        ulong token=((ulong)unchecked((uint)Interlocked.Increment(ref sequence))<<32)|ReplayTag;
        var record=new NativeBatch{Remaining=batch.Length,Resume=resume,Gesture=gesture};pending[token]=record;
        for(int i=0;i<batch.Length;i++){var mouse=batch[i].Mouse;mouse.Extra=new UIntPtr(token);batch[i].Mouse=mouse;}
        try{
            lastNative="sending token="+token+", resume="+resume;uint sent=SendInput((uint)batch.Length,batch,Marshal.SizeOf(typeof(Input)));lastNative="sent="+sent+", remaining="+Volatile.Read(ref record.Remaining);
            if(sent==(uint)batch.Length && SpinWait.SpinUntil(delegate{return Volatile.Read(ref record.Remaining)==0;},1000))return true;
            SendInput(1,new[]{Button(4)},Marshal.SizeOf(typeof(Input)));return false;
        }finally{NativeBatch removed;pending.TryRemove(token,out removed);}
    }
    private static Input Move(Point point,Rectangle area){return new Input{Type=0,Mouse=new MouseInput{X=(int)Math.Round((point.X-area.Left)*65535.0/Math.Max(1,area.Width-1)),Y=(int)Math.Round((point.Y-area.Top)*65535.0/Math.Max(1,area.Height-1)),Flags=0xE001,Extra=new UIntPtr(ReplayTag)}};}
    private static Input Button(uint flag){return new Input{Type=0,Mouse=new MouseInput{Flags=flag,Extra=new UIntPtr(ReplayTag)}};}
    public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;gate.Cancel();if(threadId!=0)PostThreadMessageW(threadId,0x12,IntPtr.Zero,IntPtr.Zero);native.CompleteAdding();if(thread!=Thread.CurrentThread && thread.Join(1000))initialized.Dispose();if(sender!=Thread.CurrentThread)sender.Join(500);}
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
