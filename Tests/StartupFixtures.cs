using System;
using System.Collections.Generic;
using System.IO;

public static class DeskPagesStartupFixtures
{
    private sealed class Fake:DeskPagesStartupGate.IPlatform {
        public string Store{get;set;}public string Account{get;set;}
        public bool Value,Installed,FailShow,FailHide,CheckOwnership;
        public bool GetHidden(){return Value;}
        public bool CanArm(){return Installed;}
        public void PutHidden(bool hidden){
            if(hidden && CheckOwnership && !File.Exists(DeskPagesStartupGate.MarkerPath))throw new Exception("Setting changed before ownership was durable.");
            if((hidden && FailHide) || (!hidden && FailShow))throw new IOException("Injected visibility error");
            Value=hidden;
        }
    }
    private static readonly List<string> passed=new List<string>();
    private static void Check(bool value,string label){if(!value)throw new Exception("FAILED: "+label);passed.Add("PASS: "+label);}
    public static string[] Run(){
        passed.Clear();var original=DeskPagesStartupGate.Platform;
        string root=Path.Combine(Path.GetTempPath(),"DeskPages-startup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var fake=new Fake{Store=root,Account=Guid.NewGuid().ToString("N"),CheckOwnership=true};DeskPagesStartupGate.Platform=fake;
        try{
            Check(System.Runtime.InteropServices.Marshal.SizeOf(typeof(DeskPagesStartupGate).GetNestedType("ShellState",System.Reflection.BindingFlags.NonPublic))==32,"SHELLSTATE layout matches eight Windows DWORD fields");
            Check(DeskPagesStartupGate.ArmForNextLogon()==null && !File.Exists(DeskPagesStartupGate.MarkerPath),"no persistent icon gate without installed startup");
            fake.Installed=true;
            string first=DeskPagesStartupGate.ArmForNextLogon();
            Check(fake.Value && DeskPagesStartupGate.Read().Phase=="next-logon","shutdown arms persisted setting after ownership record");
            var restoring=DeskPagesStartupGate.BeginRestore();
            Check(restoring.Token!=first && !restoring.OriginalHidden && restoring.Phase=="restoring","logon inherits original visibility and creates a fresh lease");
            Check(DeskPagesStartupGate.IsOwnerAlive(restoring),"guardian identity includes matching process creation time");
            var reused=new DeskPagesStartupGate.Lease{Owner=restoring.Owner,Started=restoring.Started+1};
            Check(!DeskPagesStartupGate.IsOwnerAlive(reused),"reused PID or wrong creation time is rejected");
            DeskPagesStartupGate.SetForLease(restoring.Token,false);
            Check(!fake.Value && File.Exists(DeskPagesStartupGate.MarkerPath),"live icons can restore behind cover while lease remains owned");
            DeskPagesStartupGate.SignalReady(Guid.NewGuid().ToString("N"));
            Check(!File.Exists(DeskPagesStartupGate.ReadyPath),"wrong worker token cannot release startup cover");
            DeskPagesStartupGate.SignalReady(restoring.Token);
            Check(DeskPagesStartupGate.Ready(restoring.Token),"matching restored worker publishes readiness");
            string newer=DeskPagesStartupGate.ArmForNextLogon();
            Check(!DeskPagesStartupGate.Release(restoring.Token) && fake.Value && DeskPagesStartupGate.Read().Token==newer,"old watchdog cannot release or delete a newer shutdown gate");
            DeskPagesStartupGate.Release(newer);
            Check(!fake.Value && !File.Exists(DeskPagesStartupGate.MarkerPath),"owned recovery restores visibility and removes lease");
            fake.Value=true;
            Check(DeskPagesStartupGate.BeginRestore()==null && DeskPagesStartupGate.ArmForNextLogon()==null,"pre-existing user hide-icons preference is not claimed");fake.Value=false;
            string fail=DeskPagesStartupGate.ArmForNextLogon();fake.FailShow=true;
            bool failed=false;try{DeskPagesStartupGate.Release(fail);}catch(IOException){failed=true;}
            Check(failed && File.Exists(DeskPagesStartupGate.MarkerPath),"failed recovery preserves ownership for retry");fake.FailShow=false;
            DeskPagesStartupGate.Release(fail);
            fake.FailHide=true;failed=false;try{DeskPagesStartupGate.ArmForNextLogon();}catch(IOException){failed=true;}
            Check(failed && !File.Exists(DeskPagesStartupGate.MarkerPath) && !fake.Value,"failed arm rolls back its owned marker");fake.FailHide=false;
            DeskPagesStartupGate.AtomicText(DeskPagesStartupGate.MarkerPath,"invalid");
            failed=false;try{DeskPagesStartupGate.Read();}catch(InvalidDataException){failed=true;}
            Check(failed,"corrupt ownership record cannot become an arbitrary lease");File.Delete(DeskPagesStartupGate.MarkerPath);
            var p=new DeskPagesCheckpointPolicy();p.Changed(100);
            Check(!p.Due(219) && p.Due(220),"drop checkpoint becomes due after 120ms instead of five seconds");
            long captured=p.Generation;p.Changed(230);p.Completed(captured,true,240);
            Check(p.Due(350),"a second drop during capture remains dirty");
            p.Completed(p.Generation,false,350);Check(!p.Due(849) && p.Due(850),"failed checkpoint retries without discarding dirty generation");
            p.Completed(p.Generation,true,850);Check(!p.Due(10000),"successful checkpoint clears only its captured generation");
            var events=new List<string>();var session=new DeskPagesSessionPolicy();
            Action save=delegate{events.Add("save");};Func<string> arm=delegate{events.Add("arm");return "owned";};Action<string> release=delegate(string token){events.Add("release:"+token);};Action<string> log=delegate(string message){events.Add("log");};
            Check(session.Query() && events.Count==0,"query-end-session answers immediately without cleanup or blocking");
            session.End(false,0,false,save,arm,release,log);Check(events.Count==0 && !session.Ending,"cancelled shutdown before confirmation changes nothing");
            session.End(true,0,false,save,arm,release,log);Check(string.Join(",",events)=="save,arm" && session.Ending,"confirmed session end checkpoints before arming next logon");
            session.End(true,0,false,save,arm,release,log);Check(events.Count==2,"duplicate end-session cannot save or arm twice");
            session.End(false,0,false,save,arm,release,log);Check(!session.Ending && events[2]=="release:owned","cancelled confirmed shutdown releases only its own gate");
            events.Clear();session=new DeskPagesSessionPolicy();session.End(true,0,true,save,arm,release,log);
            Check(!events.Contains("save") && events.Contains("arm"),"shutdown reentered during transition cannot snapshot mixed icons");
            events.Clear();session=new DeskPagesSessionPolicy();session.End(true,1,false,save,arm,release,log);
            Check(string.Join(",",events)=="save","Restart Manager close saves without hiding desktop for a new logon");
            events.Clear();session=new DeskPagesSessionPolicy();session.End(true,0,false,delegate{throw new IOException("Explorer unavailable");},arm,release,log);
            Check(string.Join(",",events)=="log,arm","unavailable Explorer preserves old checkpoint and does not veto shutdown");
            return passed.ToArray();
        }finally{DeskPagesStartupGate.Platform=original;Directory.Delete(root,true);}
    }
}
