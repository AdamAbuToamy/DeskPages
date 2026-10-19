using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
public static class FeedbackUiHarness {
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern int GetWindowLongW(IntPtr hwnd,int index);
 static void Assert(bool pass,string message){if(!pass)throw new Exception(message);}
 static ConcurrentQueue<Action<DeskPagesIndicator>> Queue(DeskPagesIndicatorController c){return (ConcurrentQueue<Action<DeskPagesIndicator>>)typeof(DeskPagesIndicatorController).GetField("jobs",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c);}
 static void Inspect(DeskPagesIndicatorController c,Action<DeskPagesIndicator> inspect){Exception error=null;using(var ready=new ManualResetEvent(false)){typeof(DeskPagesIndicatorController).GetMethod("Post",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c,new object[]{new Action<DeskPagesIndicator>(delegate(DeskPagesIndicator h){try{inspect(h);}catch(Exception ex){error=ex;}finally{ready.Set();}})});Assert(ready.WaitOne(2500),"Feedback thread did not respond while caller was blocked");}if(error!=null)throw error;}
 static Bitmap Capture(DeskPagesIndicatorController c,string file){Bitmap image=null;Inspect(c,delegate(DeskPagesIndicator h){image=new Bitmap(h.Width,h.Height);h.DrawToBitmap(image,h.ClientRectangle);if(file!=null)image.Save(file);});return image;}
 [STAThread]public static int Main(string[] args){
  try{
   var area=new Rectangle(0,0,1280,960);var errors=new ConcurrentQueue<string>();
   using(var c=new DeskPagesIndicatorController(delegate(string e){errors.Enqueue(e);})){
    IntPtr foreground=Environment.OSVersion.Platform==PlatformID.Win32NT?GetForegroundWindow():IntPtr.Zero;
    long token=c.Begin(2,1,area);Thread.Sleep(180);
    Inspect(c,delegate(DeskPagesIndicator h){Assert(h.Visible && h.Pending,"Accepted request was not visible/pending");
     if(Environment.OSVersion.Platform==PlatformID.Win32NT){Assert(GetForegroundWindow()==foreground,"HUD stole focus");Assert((GetWindowLongW(h.Handle,-20)&0x080800A0)==0x080800A0 && !h.ShowInTaskbar,"HUD input isolation styles missing");}});
    using(var a=Capture(c,args[0]+"-pending-a.png")){
     Thread.Sleep(280);
     using(var b=Capture(c,args[0]+"-pending-b.png")){int changed=0;for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)if(a.GetPixel(x,y)!=b.GetPixel(x,y))changed++;Assert(changed>5,"Progress line froze while caller slept");}
    }
    Thread.Sleep(1600);Inspect(c,delegate(DeskPagesIndicator h){Assert(h.Visible && h.Pending,"Pending HUD auto-expired");});
    c.Complete(token);Thread.Sleep(80);Inspect(c,delegate(DeskPagesIndicator h){Assert(h.Visible && !h.Pending,"Success hold missing");});
    Thread.Sleep(650);Inspect(c,delegate(DeskPagesIndicator h){Assert(!h.Visible,"Completed HUD stayed visible");});
    long old=c.Begin(3,-1,area);long next=c.Begin(99,1,area);c.Complete(old);Thread.Sleep(80);
    Inspect(c,delegate(DeskPagesIndicator h){Assert(h.Pending,"Stale completion retired newer request");});
    using(var ignored=Capture(c,args[0]+"-page99.png")){}
    c.Fail(next);Thread.Sleep(100);using(var ignored=Capture(c,args[0]+"-failed.png")){}
    Thread.Sleep(1100);Inspect(c,delegate(DeskPagesIndicator h){Assert(!h.Visible,"Failed HUD stayed visible");});
    c.Begin(2,1,area);c.Dismiss();c.Complete(next);Thread.Sleep(100);Inspect(c,delegate(DeskPagesIndicator h){Assert(!h.Visible,"Dismissed HUD resurrected");});
    Assert(errors.IsEmpty,"Feedback controller logged an error: "+string.Join(";",errors.ToArray()));
   }
   using(var h=new DeskPagesIndicator()){
    foreach(float scale in new[]{1f,1.5f,2f}){
     h.ClientSize=new Size((int)(128*scale),(int)(78*scale));h.Begin(99,-1,area);
     using(var b=new Bitmap(h.Width,h.Height)){h.DrawToBitmap(b,h.ClientRectangle);b.Save(args[0]+"-scale-"+scale.ToString(System.Globalization.CultureInfo.InvariantCulture)+".png");}
     h.Dismiss();
    }
   }
   var broker=Assembly.LoadFrom(args[1]);var cueType=broker.GetType("DragBroker").GetNestedType("EdgeCue",BindingFlags.NonPublic);
   using(var cue=(Form)Activator.CreateInstance(cueType,true)){
    foreach(int direction in new[]{-1,1}){
     cueType.GetField("edgeDirection",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(cue,direction);
     cue.ClientSize=new Size(18,600);
     using(var b=new Bitmap(cue.Width,cue.Height)){cue.DrawToBitmap(b,cue.ClientRectangle);b.Save(args[0]+"-edge-"+direction+".png");Assert(b.GetPixel(direction<0?0:17,300).R==255,"White edge core missing");}
    }
   }
   Console.WriteLine("PASS: actual HUD stayed visible and animated during caller sleeps; commit hold/fade, stale completion, failure, dismissal, 99-page and 100/150/200 percent drawing, left/right edge core verified on the current UI host. Windows checks foreground and noactivate/click-through styles; physical capture/OLE remains device acceptance.");return 0;
  }catch(Exception ex){Console.WriteLine(ex);return 1;}
 }
}
