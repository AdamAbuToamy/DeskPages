// Isolated forms fixture; its stubs must never be compiled into the runtime DLL.
using System;using System.Drawing;using System.Windows.Forms;using System.Diagnostics;using System.Reflection;
public static class DeskPagesHost {public static string[] WorkspaceCommand(Func<string[]> action){return action();}public static void ShowError(string text){throw new Exception(text);}}
public static class DeskPagesStartupGate {public static void Log(string text){Console.WriteLine(text);}}
public static class SimpleUiHarness {
 private static string[] settings;private static int reads,writes,displayWrites;private static bool scoped,failScope;private static bool forbidden;
 private static string[] Request(string command,string[] args){
  if(command=="capture-settings")return new[]{"False"};
  if(command=="capture-mode")return new string[0];
  if(command=="display-settings")return new[]{scoped.ToString()};
  if(command=="display-scope"){if(failScope)throw new Exception("Injected display write failure.");displayWrites++;scoped=bool.Parse(args[0]);return new string[0];}
  if(command=="pages")return new[]{"1\tMain\t71\tFalse\tFalse\tTrue","2\tProject\t2\tFalse\tTrue\tFalse","3\tArchive\t0\tTrue\tFalse\tFalse"};
  if(command=="settings"){reads++;return settings;}
  if(command=="preferences-save"){if(args.Length!=9)throw new Exception("Wrong preference payload.");writes++;settings[1]=args[0];int[] slots={7,12,10,11,8,9,2};for(int i=0;i<7;i++)settings[slots[i]]=args[i+1];settings[0]=args[8];return new string[0];}
  if(command=="search" || command=="rules" || command=="history" || command=="preview")forbidden=true;
  return new string[0];
 }
 [STAThread]public static void Main(string[] args){Application.EnableVisualStyles();Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
  foreach(string lang in new[]{"en","ar","he"}){
   DeskPagesWorkspaceApi.Language=lang;reads=0;writes=0;displayWrites=0;scoped=false;failScope=false;forbidden=false;settings=new[]{"0",lang,"False","40","350","48","320","True","False","True","True","True","True","False","False","False"};DeskPagesWorkspaceApi.Request=Request;
   var watch=Stopwatch.StartNew();DeskPagesWorkspaceApi.Open();if(Application.OpenForms.Count!=1 || !Application.OpenForms[0].Visible)throw new Exception("Pages were not shown.");var pages=Application.OpenForms[0];int ticks=0;var timer=new Timer{Interval=25};DeskPagesSimpleSettings preferences=null;
   timer.Tick+=delegate{ticks++;
    if(ticks==5){if(reads!=0 || forbidden)throw new Exception("Pages loaded settings or removed tools.");Capture(pages,args,lang+"-pages");DeskPagesWorkspaceApi.OpenSettings();foreach(Form form in Application.OpenForms)if(form is DeskPagesSimpleSettings)preferences=(DeskPagesSimpleSettings)form;if(preferences==null || !preferences.Visible || pages.Visible)throw new Exception("Settings must be independent and hide the page switcher.");}
    if(ticks==14){if(reads!=1 || writes!=0)throw new Exception("Loading settings wrote preferences or loaded more than once.");var field=typeof(DeskPagesSimpleSettings).GetField("basic",BindingFlags.Instance|BindingFlags.NonPublic);var toggles=(DeskPagesModernToggle[])field.GetValue(preferences);if(toggles.Length!=4)throw new Exception("Basic settings must have exactly four toggles.");toggles[2].Checked=!toggles[2].Checked;}
    if(ticks==28){if(writes!=1)throw new Exception("Toggle did not auto-save exactly once.");Capture(preferences,args,lang+"-settings");preferences.Surface.SelectSection(2);preferences.PerformLayout();Capture(preferences,args,lang+"-appearance");preferences.Surface.SelectSection(3);Capture(preferences,args,lang+"-pages-settings");}
    if(ticks==35){var toggle=(DeskPagesModernToggle)typeof(DeskPagesSimpleSettings).GetField("primaryOnly",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(preferences);toggle.Checked=true;if(!scoped || displayWrites!=1 || !DeskPagesWorkspaceApi.PrimaryOnly)throw new Exception("Display scope toggle was not committed.");failScope=true;toggle.Checked=false;if(!toggle.Checked || !scoped || displayWrites!=1)throw new Exception("Failed scope update did not roll back UI.");failScope=false;}
    if(ticks>=30 && ticks<55 && ticks%5==0){preferences.Width=700+(ticks%3)*20;preferences.Height=560+(ticks%2)*20;}
    if(ticks==60){if(forbidden)throw new Exception("Removed workspace tools were requested.");timer.Stop();DeskPagesWorkspaceApi.Close();Application.ExitThread();}
   };timer.Start();Application.Run();timer.Dispose();Console.WriteLine("PASS: "+lang+" compact pages, separate settings, exactly 4 basic toggles, no writes while loading, one debounced auto-save, four settings sections, responsive resizes and no search/rules/history/preview requests; elapsed="+watch.ElapsedMilliseconds+" ms (fixture only).");
  }
 }
 private static void Capture(Form form,string[] args,string suffix){form.Refresh();Application.DoEvents();if(args.Length==0)return;using(var image=new Bitmap(form.Width,form.Height)){using(var graphics=Graphics.FromImage(image))graphics.CopyFromScreen(form.Location,Point.Empty,image.Size);image.Save(args[0]+"-"+suffix+".png");}}
}
