using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
[assembly:AssemblyTitle("DeskPages Rescue")]
[assembly:AssemblyCompany("Adam Abu Toamy")]
[assembly:AssemblyVersion("0.8.18.0")]
[assembly:AssemblyFileVersion("0.8.18.0")]
internal static class RescueLauncher {
    [STAThread]private static int Main(){try{
        string root=Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File \""+Path.Combine(root,"Rescue.ps1")+"\""){UseShellExecute=false,CreateNoWindow=true};
        using(var process=Process.Start(start)){process.WaitForExit();return process.ExitCode;}
    }catch(Exception ex){MessageBox.Show(ex.Message,"DeskPages Rescue");return 1;}}
}
