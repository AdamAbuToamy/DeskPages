using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
[assembly:AssemblyVersion("0.8.28.0")]
[assembly:AssemblyProduct("DeskPages")]
[assembly:AssemblyCompany("Adam Abu Toamy")]
[assembly:AssemblyCopyright("Copyright © 2026 Adam Abu Toamy")]
[assembly:AssemblyTitle("DeskPages Setup")]
internal static class Setup {
    [STAThread] static int Main(string[] args){
        Application.EnableVisualStyles();
        try {
            string sid=WindowsIdentity.GetCurrent().User.Value;
            bool admin=new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            if(!admin){
                var start=new ProcessStartInfo(Application.ExecutablePath,"--account "+sid){UseShellExecute=true,Verb="runas"};
                using(var p=Process.Start(start)){p.WaitForExit();return p.ExitCode;}
            }
            if(args.Length==2 && (args[0]!="--account" || args[1]!=sid))throw new InvalidOperationException("Use the same Windows account for installation and elevation.");
            using(var form=new SetupWindow(sid,Install)){Application.Run(form);return form.InstallFailed?1:0;}
        }catch(Exception ex){MessageBox.Show(ex.Message,"DeskPages Setup",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
    }
    static void Install(string sid,string target,Action<string> status,CancellationToken cancellation){
        cancellation.ThrowIfCancellationRequested();
        using(var version=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")){
            int build;if(version==null || !int.TryParse(Convert.ToString(version.GetValue("CurrentBuildNumber")),out build) || build<22000 || !Environment.Is64BitProcess)throw new PlatformNotSupportedException("DeskPages requires Windows 11 x64.");
        }
        status("Checking the installation package…");
        byte[] payload;
        using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("DeskPages.Payload"))using(var m=new MemoryStream()){s.CopyTo(m);payload=m.ToArray();}
        using(var hash=SHA256.Create())if(BitConverter.ToString(hash.ComputeHash(payload)).Replace("-","")!=PayloadIdentity.Hash)throw new InvalidDataException("Installer payload hash mismatch.");
        string stage=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"DeskPages-Setup-"+Guid.NewGuid().ToString("N"));
        var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);
        var inherit=InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit;
        foreach(string id in new[]{"S-1-5-18","S-1-5-32-544"})acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(id),FileSystemRights.FullControl,inherit,PropagationFlags.None,AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid),FileSystemRights.ReadAndExecute,inherit,PropagationFlags.None,AccessControlType.Allow));
        Directory.CreateDirectory(stage,acl);
        try {
            using(var m=new MemoryStream(payload))using(var zip=new ZipArchive(m,ZipArchiveMode.Read)){
                long total=0;var names=new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var entry in zip.Entries){
                    cancellation.ThrowIfCancellationRequested();
                    string name=entry.FullName.Replace('/',Path.DirectorySeparatorChar);
                    string path=Path.GetFullPath(Path.Combine(stage,name));
                    if(Path.IsPathRooted(name)||name.Contains(":")||!path.StartsWith(stage+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!names.Add(path))throw new InvalidDataException("Unsafe installer entry.");
                    total+=entry.Length;if(total>80*1024*1024)throw new InvalidDataException("Oversized payload.");
                    if(string.IsNullOrEmpty(entry.Name)){Directory.CreateDirectory(path);continue;}
                    Directory.CreateDirectory(Path.GetDirectoryName(path));using(var input=entry.Open())using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write)){input.CopyTo(output);}
                }
            }
            cancellation.ThrowIfCancellationRequested();
            DeskPagesScriptEncoding.ValidateDirectory(stage);
            status("Installing DeskPages and starting your desktop pages…");
            string cancelName="Local\\DeskPagesSetupCancel-"+Guid.NewGuid().ToString("N");
            using(var cancelEvent=new EventWaitHandle(false,EventResetMode.ManualReset,cancelName))
            using(cancellation.Register(delegate{cancelEvent.Set();})){
            string script=Path.Combine(stage,"Install-Startup.ps1");
            var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile -STA -ExecutionPolicy Bypass -File \""+script+"\" -InstallDir \""+target+"\" -CancelEventName "+cancelName){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=stage};
            string log=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPages","setup.log");Directory.CreateDirectory(Path.GetDirectoryName(log));
            using(var writer=new StreamWriter(log,false))using(var process=new Process{StartInfo=info}){
                process.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null){lock(writer)writer.WriteLine(e.Data);if(e.Data=="DESKPAGES_COMMIT_STARTED")status("@commit");}};
                process.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null)lock(writer)writer.WriteLine(e.Data);};
                process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();process.WaitForExit();if(process.ExitCode==1223)throw new OperationCanceledException(cancellation);if(process.ExitCode!=0)throw new InvalidOperationException("See "+log+". If upgrading an older version, close its tray menu → Exit (keep active page), then retry.");
            }

            using(var key=Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DeskPages-"+sid)){
                key.SetValue("DisplayName","DeskPages");key.SetValue("Publisher","Adam Abu Toamy");key.SetValue("DisplayVersion","0.8.28");key.SetValue("DisplayIcon",Path.Combine(target,"Assets","DeskPages.ico"));key.SetValue("InstallLocation",target);
                key.SetValue("UninstallString","\""+info.FileName+"\" -NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File \""+Path.Combine(target,"Uninstall-Startup.ps1")+"\"");key.SetValue("NoModify",1);key.SetValue("NoRepair",1);
            }
            status("DeskPages is running. Finishing installation…");
            }
        }finally{try{Directory.Delete(stage,true);}catch{}}
    }
}
