// Website evidence: actual production forms, sample data, no Explorer or user files.
using System;using System.Drawing;using System.Drawing.Imaging;using System.IO;using System.Threading;using System.Windows.Forms;
public static class DeskPagesHost {public static string[] WorkspaceCommand(Func<string[]> action){return action();}public static void ShowError(string text){throw new Exception(text);}}
public static class DeskPagesStartupGate {public static void Log(string text){Console.WriteLine(text);}}
public static class WebsiteUiCapture {
 private static string[] Request(string command,string[] args){
  if(command=="pages")return new[]{"1\tMain\t6\tFalse\tFalse\tTrue","2\tWork\t3\tFalse\tFalse\tFalse","3\tStudy\t3\tFalse\tFalse\tFalse"};
  if(command=="display-settings")return new[]{"False"};
  if(command=="settings")return new[]{"0",DeskPagesWorkspaceApi.Language,"False","40","350","48","320","True","False","True","True","True","True","False","False","False"};
  return new string[0];
 }
 private static void Pump(){for(int i=0;i<12;i++){Application.DoEvents();Thread.Sleep(25);}}
 private static void Capture(Form form,string path){form.Refresh();Application.DoEvents();using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save(path,ImageFormat.Png);}}
 [STAThread]public static void Main(string[] args){
  if(args.Length!=1)throw new ArgumentException("Output directory required");Directory.CreateDirectory(args[0]);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  foreach(string language in new[]{"en","ar"}){
   DeskPagesWorkspaceApi.Language=language;DeskPagesWorkspaceApi.Request=Request;DeskPagesWorkspaceApi.Open();Pump();
   Form pages=null;foreach(Form form in Application.OpenForms)if(form is DeskPagesWorkspace)pages=form;
   if(pages==null)throw new Exception("Production page window missing");Capture(pages,Path.Combine(args[0],"pages-"+language+".png"));
   DeskPagesWorkspaceApi.OpenSettings();Pump();Form settings=null;foreach(Form form in Application.OpenForms)if(form is DeskPagesSimpleSettings)settings=form;
   if(settings==null)throw new Exception("Production settings window missing");Capture(settings,Path.Combine(args[0],"settings-"+language+".png"));DeskPagesWorkspaceApi.Close();Pump();
  }
  Console.WriteLine("Captured actual production WinForms on Windows with sample data; no Explorer simulation or file operations executed.");
 }
}
