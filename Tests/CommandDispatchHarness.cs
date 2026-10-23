using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Forms;

public static class CommandDispatchHarness {
    [STAThread]public static int Main(string[] args){
        try{Assembly.LoadFrom(args[0]);Run();return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Field(object instance,string name,object value){instance.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(instance,value);}
    [MethodImpl(MethodImplOptions.NoInlining)]static void Run(){
        var queue=new DeskPagesCommandQueue();Check(queue.Enqueue(9701,false,10) && queue.Enqueue(1,false,11) && queue.Enqueue(1002,true,12) && !queue.Enqueue(-1,false,13),"Queue limit changed");
        DeskPagesInputCommand c;queue.TryDequeue(out c);Check(c.Code==9701 && !c.Navigation,"Display-change fence must remain ahead of following navigation");queue.TryDequeue(out c);Check(c.Navigation && c.ReceivedAt==11,"Input timestamp lost");queue.TryDequeue(out c);Check(c.Code==1002 && c.ExplicitUi && queue.IsEmpty,"FIFO/UI intent changed");
        var priority=ThreadPriority.Normal;int writes=0;
        try{using(var scope=new DeskPagesNavigationPriority(true,()=>priority,p=>{priority=p;writes++;})){Check(priority==ThreadPriority.AboveNormal,"Missing bounded boost");throw new InvalidOperationException("fixture");}}catch(InvalidOperationException){}
        Check(priority==ThreadPriority.Normal && writes==2,"Failed navigation did not restore priority");
        using(var scope=new DeskPagesNavigationPriority(false,()=>priority,p=>writes++)){}Check(writes==2,"Background command boosted");priority=ThreadPriority.Highest;using(var scope=new DeskPagesNavigationPriority(true,()=>priority,p=>writes++)){}Check(writes==2,"Higher inherited priority was lowered");
        using(var scope=new DeskPagesNavigationPriority(true,()=>{throw new Exception("denied");},p=>{})){}
        // Run the production host queue against a real hidden message window,
        // without constructing the desktop agent or changing Explorer state.
        var host=(DeskPagesHost)FormatterServices.GetUninitializedObject(typeof(DeskPagesHost));
        Field(host,"session",new DeskPagesSessionPolicy());Field(host,"commands",new DeskPagesCommandQueue());
        int dispatched=0;var before=Thread.CurrentThread.Priority;
        Field(host,"dispatch",(Action<int>)delegate(int code){Check(code==1,"Wrong posted command");dispatched++;});
        var nested=typeof(DeskPagesHost).GetNestedType("SessionWindow",BindingFlags.NonPublic);
        var window=(NativeWindow)Activator.CreateInstance(nested,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{host},null);
        Field(host,"commandWindow",window.Handle);
        try{
            var enqueue=typeof(DeskPagesHost).GetMethod("QueueCommand",BindingFlags.NonPublic|BindingFlags.Instance);
            Field(host,"busy",true);enqueue.Invoke(host,new object[]{1,false});Application.DoEvents();Check(dispatched==0,"Posted command reentered an active transaction");
            Field(host,"busy",false);typeof(DeskPagesHost).GetMethod("TryDispatchCommand",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(host,null);Check(dispatched==1,"Pending command lost after busy wake");
            enqueue.Invoke(host,new object[]{1,false});Application.DoEvents();Check(dispatched==2,"Input wake still requires a timer tick");
            Check(Thread.CurrentThread.Priority==before,"Production dispatch leaked priority boost");
        }finally{((IDisposable)window).Dispose();}
        Console.WriteLine("PASS: real posted-message wake without timer, busy/reentry fence, FIFO display-change order, timestamps, bounded priority and exception restoration. No Explorer mutation or physical-input latency claim.");
    }
}
