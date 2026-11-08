using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Runtime.InteropServices;

public static class DeskPagesDragRecoveryHarness {
    private static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static object Field(object broker,string name){return broker.GetType().GetField(name,Private).GetValue(broker);}
    private static void Set(object broker,string name,object value){broker.GetType().GetField(name,Private).SetValue(broker,value);}
    private static object Call(object broker,string name,params object[] args){return broker.GetType().GetMethod(name,Private).Invoke(broker,args);}
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static long Now(){return (long)(Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency);}
    private static void Wait(Func<bool> done,string message){var clock=Stopwatch.StartNew();while(!done()){if(clock.ElapsedMilliseconds>5000)throw new Exception(message);Thread.Sleep(10);}}
    private static void Motion(){Input forward=new Input{Type=0,Mouse=new Mouse{X=1,Flags=1}},back=new Input{Type=0,Mouse=new Mouse{X=-1,Flags=1}};Require(SendInput(2,new[]{forward,back},Marshal.SizeOf(typeof(Input)))==2,"Mouse motion fixture unavailable");}
    private static void RecoverHooks(object broker){
        long events=(long)Field(broker,"inputEvents");Motion();Wait(()=> (long)Field(broker,"inputEvents")>events,"Original input hook did not receive input");
        Require(UnhookWindowsHookEx((IntPtr)Field(broker,"mouseHandle")),"Cannot simulate Windows removing the hook");
        events=(long)Field(broker,"inputEvents");Motion();Thread.Sleep(80);
        Require((long)Field(broker,"inputEvents")==events,"Removed hook still received input");
        long generation=(long)Field(broker,"hookGeneration");Set(broker,"lastHookRenewal",Now()-16000);Call(broker,"Tick");
        Wait(()=> (long)Field(broker,"hookGeneration")>generation,"Silently removed hook was not renewed");
        Motion();Wait(()=> (long)Field(broker,"inputEvents")>events,"Renewed hook did not receive native input");
        for(int i=0;i<30;i++){
            generation=(long)Field(broker,"hookGeneration");Set(broker,"lastForeground",new IntPtr(-1));Call(broker,"Tick");
            Wait(()=> (long)Field(broker,"hookGeneration")>generation,"Application change did not renew hooks");
        }
        generation=(long)Field(broker,"hookGeneration");Set(broker,"lastHookRenewal",Now());Set(broker,"lastForeground",new IntPtr(-1));Set(broker,"candidate",true);Call(broker,"Tick");
        Require((IntPtr)Field(broker,"lastForeground")==new IntPtr(-1),"Busy drag discarded the pending application-change renewal");
        Set(broker,"candidate",false);Set(broker,"releaseAt",-1L);Call(broker,"Tick");
        Wait(()=> (long)Field(broker,"hookGeneration")>generation,"Deferred application change did not renew when drag finished");
        var worker=(Thread)Field(broker,"inputThread");PostThreadMessageW((uint)Field(broker,"inputThreadId"),0x12,UIntPtr.Zero,IntPtr.Zero);Require(worker.Join(3000),"Input fixture thread did not stop");
        Set(broker,"lastHookRenewal",Now()-16000);Call(broker,"Tick");
        Require(((Thread)Field(broker,"inputThread")).IsAlive && !Object.ReferenceEquals(worker,Field(broker,"inputThread")),"Stopped input thread did not recover");
        Console.WriteLine("PASS: actual hook removal reproduced; native input returns after renewal; 30 application-change renewals; dead input thread restarted without restarting broker.");
    }
    private static void Supervision(){
        string token=Guid.NewGuid().ToString("N"),folder=DeskPagesDragProtocol.SessionPath(token);Directory.CreateDirectory(folder);
        Process peer=null;
        try{
            peer=Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--peer "+token){UseShellExecute=false,CreateNoWindow=true});
            string identity=Path.Combine(folder,"broker-owner.txt");Wait(()=>File.Exists(identity),"Peer did not register");
            Require(DeskPagesDragLifetime.ReadPeer(identity)==true,"Live peer identity rejected");
            var supervisor=new DeskPagesDragSupervisor(token,0);
            Require(!supervisor.ShouldRestart(0,false,false),"Live peer restarted on first observation");
            string savedIdentity=File.ReadAllText(identity);File.Delete(identity);
            for(int i=0;i<360;i++)Require(!supervisor.ShouldRestart(i*10000,false,false),"Live idle helper restarted");
            File.WriteAllText(identity,savedIdentity);
            peer.Kill();peer.WaitForExit();Require(DeskPagesDragLifetime.ReadPeer(identity)==false,"Dead helper reported alive");
            supervisor=new DeskPagesDragSupervisor(token,0);
            Require(!supervisor.ShouldRestart(20000,false,false),"Restart skipped final request drain grace");
            Require(!supervisor.ShouldRestart(23000,true,false),"Restart replayed an in-flight operation");
            Require(!supervisor.ShouldRestart(24000,false,true),"Restart interrupted a held drag");
            Require(supervisor.ShouldRestart(25000,false,false),"Dead helper never restarted");
            Require(!supervisor.ShouldRestart(26000,false,false),"Failed startup caused a restart storm");
            var policy=new DeskPagesDragSupervisor(token,0);
            for(int i=0;i<360;i++)Require(!policy.Observe(i*10000,null,false,false),"Unknown liveness killed/replaced a helper");
            using(var current=Process.GetCurrentProcess())Require(DeskPagesDragLifetime.Alive(current.Id,current.StartTime.ToUniversalTime().Ticks+1)==false,"Reused PID identity accepted");
            Console.WriteLine("PASS: live idle process retained for simulated hour; actual helper process death detected; restart waits for pending operation and button release; unknown identity and PID reuse fenced; retries bounded.");
        }finally{if(peer!=null){if(!peer.HasExited){peer.Kill();peer.WaitForExit();}peer.Dispose();}File.Delete(Path.Combine(folder,"broker-owner.txt"));Directory.Delete(folder);}
    }
    [STAThread]public static int Main(string[] args){
        if(args.Length==2 && args[0]=="--peer"){
            string path=DeskPagesDragProtocol.SessionPath(args[1]);Directory.CreateDirectory(path);DeskPagesDragProtocol.AtomicText(Path.Combine(path,"broker-owner.txt"),DeskPagesDragLifetime.Identity());Thread.Sleep(30000);return 0;
        }
        object broker=null;DeskPagesDragMailbox mailbox=null;Thread worker=null;
        string token=Guid.NewGuid().ToString("N");
        try{
            mailbox=new DeskPagesDragMailbox(token);mailbox.Publish(1,true,2);
            broker=Activator.CreateInstance(typeof(DragBroker),Private,null,new object[]{token},null);
            worker=(Thread)Field(broker,"inputThread");
            Require(worker.IsAlive && worker.ManagedThreadId!=Thread.CurrentThread.ManagedThreadId,"Input hooks share the OLE/UI thread");
            Call(broker,"Tick");
            // A transient absent/malformed state snapshot after hours of uptime
            // must not permanently stop an otherwise live helper.
            Set(broker,"started",Now()-3600000);
            string state=Path.Combine(DeskPagesDragProtocol.SessionPath(token),"state.txt");File.Delete(state);Call(broker,"Tick");
            Require(!(bool)Field(broker,"closed"),"Temporary missing state permanently stopped the helper");
            File.WriteAllText(state,"incomplete snapshot");Call(broker,"Tick");
            Require(!(bool)Field(broker,"closed"),"Temporary invalid state permanently stopped the helper");
            mailbox.Publish(1,true,2);Call(broker,"Tick");
            RecoverHooks(broker);worker=(Thread)Field(broker,"inputThread");
            Supervision();
            Set(broker,"approved",true);Set(broker,"files",new string[]{"C:\\fixture.txt"});
            Call(broker,"Send","Navigate",1,0,null);
            var request=(DeskPagesDragRequest)Field(broker,"pending");string id=request.Id;
            long now=(long)(Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency);
            Set(broker,"pendingAt",now-9000);
            Call(broker,"Tick");
            Require(!(bool)Field(broker,"closed"),"Delayed acknowledgement stopped the helper");
            Require(((DeskPagesDragRequest)Field(broker,"pending")).Id==id,"Delayed command was replayed or forgotten");
            Require(mailbox.Poll(1,true,2) && mailbox.Pending.Id==id,"Original command changed during timeout");
            mailbox.Complete(true,2);mailbox.Publish(2,true,2);
            Call(broker,"Tick");
            var cancel=(DeskPagesDragRequest)Field(broker,"pending");
            Require(cancel!=null && cancel.Kind=="Cancel" && cancel.Id!=id,"Late navigation did not cancel its obsolete drag");
            Require(mailbox.Poll(2,true,2) && mailbox.Pending.Kind=="Cancel","Late navigation was dispatched again");
            mailbox.Complete(true,2);Call(broker,"Tick");
            Require(Field(broker,"pending")==null && !(bool)Field(broker,"approved") && !(bool)Field(broker,"closed"),"Helper did not recover after late reply");
            Require(!mailbox.Poll(2,true,2),"Acknowledged cancellation replayed");
            Call(broker,"Send","Arm",10,20,new string[]{"C:\\fixture.txt"});
            Require(mailbox.Poll(2,true,2) && mailbox.Pending.Kind=="Arm","Next drag could not arm after delayed reply");
            Console.WriteLine("PASS: separate input thread; delayed navigation acknowledged once; safe cancellation; helper stays alive and accepts the next drag.");
            return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{
            if(broker!=null)Call(broker,"ExitThreadCore");
            if(worker!=null)worker.Join(3000);
            if(mailbox!=null)mailbox.Dispose();
        }
    }
    [StructLayout(LayoutKind.Sequential)]private struct Mouse{public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Sequential)]private struct Input{public uint Type;public Mouse Mouse;}
    [DllImport("user32.dll",SetLastError=true)]private static extern uint SendInput(uint count,Input[] inputs,int size);
    [DllImport("user32.dll")]private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]private static extern bool PostThreadMessageW(uint id,uint message,UIntPtr wparam,IntPtr lparam);
}
