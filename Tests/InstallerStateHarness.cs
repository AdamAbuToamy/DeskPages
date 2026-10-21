using System;using System.Reflection;using System.Security.Principal;using System.Threading;using System.Windows.Forms;
internal static class InstallerStateHarness {
    static T Field<T>(object o,string name){return (T)o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
    static void Assert(bool value,string text){if(!value)throw new Exception(text);}
    static void Until(Func<bool> done){var end=DateTime.UtcNow.AddSeconds(8);while(!done()){if(DateTime.UtcNow>end)throw new Exception("UI fixture timed out");Application.DoEvents();Thread.Sleep(10);}}
    [STAThread]static int Main(){Application.EnableVisualStyles();string sid=WindowsIdentity.GetCurrent().User.Value;
        using(var form=new SetupWindow(sid,delegate(string account,string path,Action<string> status,CancellationToken token){token.WaitHandle.WaitOne(6000);token.ThrowIfCancellationRequested();})){
            form.Show();Application.DoEvents();Field<Button>(form,"primary").PerformClick();Assert(!Field<Button>(form,"primary").Visible,"Install button stayed visible");Assert(Field<Button>(form,"secondary").Enabled,"Cancel disabled during preparation");Field<Button>(form,"secondary").PerformClick();Until(delegate{return form.IsDisposed;});Assert(!form.InstallFailed,"Cancellation reported failure");
        }
        using(var finish=new ManualResetEvent(false))using(var form=new SetupWindow(sid,delegate(string account,string path,Action<string> status,CancellationToken token){status("@commit");finish.WaitOne(6000);Assert(!token.IsCancellationRequested,"Critical installation was interrupted");})){
            form.Show();Application.DoEvents();Field<Button>(form,"primary").PerformClick();Until(delegate{return Field<Button>(form,"secondary").Text=="Close when finished";});Field<Button>(form,"secondary").PerformClick();Assert(!form.IsDisposed,"Closed while file replacement running");finish.Set();Until(delegate{return form.IsDisposed;});
        }
        int tries=0;using(var form=new SetupWindow(sid,delegate(string account,string path,Action<string> status,CancellationToken token){if(tries++==0)throw new Exception("Injected failure");})){
            form.Show();Application.DoEvents();Field<Button>(form,"primary").PerformClick();Until(delegate{return form.InstallFailed;});Assert(Field<Button>(form,"primary").Visible && Field<Button>(form,"primary").Text=="Try again","Retry not restored");Field<Button>(form,"primary").PerformClick();Until(delegate{return Field<Button>(form,"primary").Text=="Finish";});form.Close();
        }
        Console.WriteLine("PASS: real installer controls hide Install, cancel preparation, safely defer close during commit, and restore Retry/Finish. No app installation or desktop changes.");return 0;
    }
}
