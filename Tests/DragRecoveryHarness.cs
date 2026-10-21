using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

public static class DeskPagesDragRecoveryHarness {
    private static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static object Field(object broker,string name){return broker.GetType().GetField(name,Private).GetValue(broker);}
    private static void Set(object broker,string name,object value){broker.GetType().GetField(name,Private).SetValue(broker,value);}
    private static object Call(object broker,string name,params object[] args){return broker.GetType().GetMethod(name,Private).Invoke(broker,args);}
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    [STAThread]public static int Main(){
        object broker=null;DeskPagesDragMailbox mailbox=null;Thread worker=null;
        string token=Guid.NewGuid().ToString("N");
        try{
            mailbox=new DeskPagesDragMailbox(token);mailbox.Publish(1,true,2);
            broker=Activator.CreateInstance(typeof(DragBroker),Private,null,new object[]{token},null);
            worker=(Thread)Field(broker,"inputThread");
            Require(worker.IsAlive && worker.ManagedThreadId!=Thread.CurrentThread.ManagedThreadId,"Input hooks share the OLE/UI thread");
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
}
