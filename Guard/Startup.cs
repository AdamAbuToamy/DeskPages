using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

[assembly:System.Reflection.AssemblyVersion("0.8.17.0")]
[assembly:System.Reflection.AssemblyFileVersion("0.8.17.0")]
[assembly:System.Reflection.AssemblyProduct("DeskPages")]
[assembly:System.Reflection.AssemblyTitle("DeskPages startup guardian")]

internal static class Startup
{
    private const int DeadlineMs=20000;
    [STAThread] private static int Main(string[] args){
        try{
            if(args.Length==1 && args[0]=="--recover-gate"){Fallback();return 0;}
            if(args.Length==2 && args[0]=="--watch"){Watch(args[1]);return 0;}
            if(args.Length!=1 || args[0]!="--logon")return 2;
            string sid=WindowsIdentity.GetCurrent().User.Value;
            using(var mutex=new Mutex(false,"Local\\DeskPages-Startup-"+sid)){
                bool owned=false;try{owned=mutex.WaitOne(0);}catch(AbandonedMutexException){owned=true;}
                if(!owned)return 0;
                try{Run();}finally{mutex.ReleaseMutex();}
            }
            return 0;
        }catch(Exception ex){DeskPagesStartupGate.Log("guardian error: "+ex.Message);return 1;}
    }
    private static string Self {get{return System.Reflection.Assembly.GetExecutingAssembly().Location;}}
    private static void Run(){
        DeskPagesStartupGate.Lease lease=null;
        Cover cover=null;Process agent=null;
        try{
            lease=DeskPagesStartupGate.BeginRestore();
            if(lease!=null){
                // Independent process survives an unresponsive guardian UI.
                using(var watcher=Process.Start(new ProcessStartInfo(Self,"--watch "+lease.Token){UseShellExecute=false,CreateNoWindow=true})){
                    if(watcher==null)throw new InvalidOperationException("Could not start startup watchdog.");
                }
                DeskPagesStartupGate.SetForLease(lease.Token,true);
                cover=new Cover();cover.Show();Application.DoEvents();
                cover.Present();
                DeskPagesBootPlan.Apply();
                // The persistent gate protects Explorer's first paint. The
                // wallpaper cover now protects live COM/layout restoration.
                DeskPagesStartupGate.SetForLease(lease.Token,false);
            }
            string root=AppDomain.CurrentDomain.BaseDirectory;
            string shell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
            string options="-NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File \""+Path.Combine(root,"Agent.ps1")+"\" -Logon";
            if(lease!=null)options+=" -StartupToken "+lease.Token;
            agent=Process.Start(new ProcessStartInfo(shell,options){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root});
            if(agent==null)throw new InvalidOperationException("Could not start DeskPages agent.");
            if(lease==null){
                DeskPagesStartupGate.Log("original Windows icon visibility was already hidden; no owned startup gate");
                agent.WaitForExit();if(agent.ExitCode!=0)throw new InvalidOperationException("Agent failed with exit code "+agent.ExitCode);return;
            }
            using(var timer=new System.Windows.Forms.Timer{Interval=30}){
                var clock=Stopwatch.StartNew();
                timer.Tick+=delegate{
                    try{
                        var current=DeskPagesStartupGate.Read();
                        if(current==null || current.Token!=lease.Token){cover.Close();return;}
                        if(DeskPagesStartupGate.Ready(lease.Token)){
                            DeskPagesStartupGate.Log("page and host ready; revealing original Explorer");cover.Close();
                        }else if(agent.HasExited || clock.ElapsedMilliseconds>=DeadlineMs){
                            DeskPagesStartupGate.Log("startup failed/timed out; restoring native visibility and removing cover");cover.Close();
                        }
                    }catch(Exception ex){DeskPagesStartupGate.Log("startup monitoring error: "+ex.Message);cover.Close();}
                };
                timer.Start();Application.Run(cover);timer.Stop();
            }
            DeskPagesStartupGate.Release(lease.Token);lease=null;
            cover.Dispose();cover=null;
            // Keep the scheduled task's parent alive without a window. Report
            // an agent crash to Task Scheduler so its restart policy applies.
            agent.WaitForExit();
            if(agent.ExitCode!=0)throw new InvalidOperationException("Agent failed with exit code "+agent.ExitCode);
        }catch(Exception ex){DeskPagesStartupGate.Log("startup preparation failed: "+ex.Message);throw;}
        finally{
            // A real end-session handler leaves a new next-logon lease intact.
            if(lease!=null){try{DeskPagesStartupGate.Release(lease.Token);}catch(Exception ex){DeskPagesStartupGate.Log("release failed: "+ex.Message);}}
            if(cover!=null)cover.Dispose();if(agent!=null)agent.Dispose();
        }
    }
    private static void Watch(string token){
        Guid parsed;if(!Guid.TryParseExact(token,"N",out parsed))return;
        var clock=Stopwatch.StartNew();DeskPagesStartupGate.Lease lease=null;
        while(clock.ElapsedMilliseconds<DeadlineMs){
            lease=DeskPagesStartupGate.Read();if(lease==null || lease.Token!=token)return;
            if(!DeskPagesStartupGate.IsOwnerAlive(lease))break;
            Thread.Sleep(100);
        }
        lease=DeskPagesStartupGate.Read();if(lease==null || lease.Token!=token || lease.Phase!="restoring")return;
        DeskPagesStartupGate.Log("independent startup watchdog released expired gate");
        DeskPagesStartupGate.Release(token);
        // Kill only this executable's exact original instance, never Explorer
        // and never the agent. Creation time also prevents PID reuse mistakes.
        if(DeskPagesStartupGate.IsOwnerAlive(lease)){
            using(var p=Process.GetProcessById(lease.Owner)){
                if(string.Equals(p.MainModule.FileName,Self,StringComparison.OrdinalIgnoreCase))p.Kill();
            }
        }
    }
    private static void Fallback(){
        // A second, unprivileged Run entry handles a missing/disabled main
        // task. It never launches an elevated worker or creates a new gate.
        var clock=Stopwatch.StartNew();string previous=null;
        while(clock.ElapsedMilliseconds<DeadlineMs){
            var lease=DeskPagesStartupGate.Read();if(lease==null)return;
            if(previous!=null && lease.Token!=previous && lease.Phase=="next-logon")return;
            previous=lease.Token;Thread.Sleep(100);
        }
        var current=DeskPagesStartupGate.Read();if(current!=null && current.Token==previous){
            DeskPagesStartupGate.Log("fallback sign-in recovery restored owned icon visibility");DeskPagesStartupGate.Release(current.Token);
        }
    }
    private sealed class Cover:Form{
        private Image wallpaper;
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool SystemParametersInfoW(uint action,uint parameter,System.Text.StringBuilder value,uint flags);
        [DllImport("user32.dll")] private static extern uint GetSysColor(int index);
        [DllImport("dwmapi.dll")] private static extern int DwmFlush();
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x80;return p;}}
        public Cover(){
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;
            StartPosition=FormStartPosition.Manual;Bounds=Screen.PrimaryScreen.WorkingArea;
            AutoScaleMode=AutoScaleMode.None;DoubleBuffered=true;
            uint color=GetSysColor(1);BackColor=Color.FromArgb((int)(color&255),(int)((color>>8)&255),(int)((color>>16)&255));
            // Only local wallpaper files are read; no network access at logon.
            var path=new System.Text.StringBuilder(32768);
            if(SystemParametersInfoW(0x73,(uint)path.Capacity,path,0)){
                string file=path.ToString();
                if(!file.StartsWith("\\\\",StringComparison.Ordinal) && File.Exists(file)){
                    try{using(var image=Image.FromFile(file))wallpaper=new Bitmap(image);}catch{}
                }
            }
        }
        protected override void OnPaint(PaintEventArgs e){
            base.OnPaint(e);if(wallpaper==null)return;
            double scale=Math.Max((double)ClientSize.Width/wallpaper.Width,(double)ClientSize.Height/wallpaper.Height);
            int w=(int)(wallpaper.Width*scale),h=(int)(wallpaper.Height*scale);
            e.Graphics.DrawImage(wallpaper,(ClientSize.Width-w)/2,(ClientSize.Height-h)/2,w,h);
        }
        public void Present(){Refresh();DwmFlush();}
        protected override void Dispose(bool disposing){if(disposing && wallpaper!=null){wallpaper.Dispose();wallpaper=null;}base.Dispose(disposing);}
    }
}
