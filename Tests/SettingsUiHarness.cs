using System;using System.Drawing;using System.IO;using System.Reflection;using System.Windows.Forms;
public static class DeskPagesStartupGate {public static void Log(string text){Console.WriteLine(text);}}
public static class DeskPagesWorkspaceApi {
 public static string Language="en";public static bool PrimaryOnly,ScopeDenied,CacheEnabled,CacheDenied;public static int Writes,Tours,Layouts,Measurements,CacheWrites;public static string[] LastPreferences;
 public static string L(string en,string ar,string he){return Language=="ar"?ar:Language=="he"?he:en;}
 public static void StartTour(){Tours++;}public static void RefreshSettingsLanguage(){}
 public static string[] Execute(string command,params string[] args){
  if(command=="capture-settings")return new[]{CacheEnabled.ToString()};
  if(command=="capture-mode"){if(CacheDenied)throw new Exception("Injected capture failure");CacheEnabled=bool.Parse(args[0]);CacheWrites++;return new string[0];}
  if(command=="measure-resources"){Measurements++;return new string[0];}
  if(command=="display-settings")return new[]{PrimaryOnly.ToString()};
  if(command=="display-scope"){if(ScopeDenied)throw new Exception("Injected scope failure");PrimaryOnly=bool.Parse(args[0]);return new string[0];}
  if(command=="preferences-save"){Writes++;LastPreferences=args;return new string[0];}
  if(command=="save-layout"){Layouts++;return new string[0];}
  if(command=="pages")return new[]{"1\tMain\t0\tFalse\tFalse\tTrue"};
  if(command=="settings"){var a=new string[16];for(int i=0;i<a.Length;i++)a[i]="False";a[0]="0";a[1]=Language;foreach(int i in new[]{7,9,10,11,12})a[i]="True";return a;}
  throw new Exception("Unexpected workspace command: "+command);
 }
}
public static class SettingsUiHarness {
 static string Context="";static void Require(bool value,string text){if(!value)throw new Exception(Context+": "+text);}
 static bool HasInk(Bitmap b,Rectangle r){r.Intersect(new Rectangle(Point.Empty,b.Size));int count=0;for(int y=r.Top;y<r.Bottom;y++)for(int x=r.Left;x<r.Right;x++){var c=b.GetPixel(x,y);if(c.R<205 && c.G<205 && c.B<205)count++;}return count>8;}
 static T Field<T>(object value,string name){return (T)value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);}
 [STAThread]public static int Main(string[] args){
  Application.EnableVisualStyles();Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
  string output=args[0];Directory.CreateDirectory(output);int rows=0,screens=0;
  foreach(string language in new[]{"en","ar","he"})foreach(float scale in new[]{1F,1.25F,1.5F,2F})foreach(int width in new[]{760,660}){
   DeskPagesWorkspaceApi.Language=language;
   using(var form=new DeskPagesSimpleSettings()){
    form.SetPreviewLayout(scale,width,width==660?480:560);form.Show();form.RefreshData();Application.DoEvents();
    Require(form.AutoScaleMode==AutoScaleMode.None && !form.RightToLeftLayout,"Native automatic/mirrored scaling must not own the layout");
    int writes=DeskPagesWorkspaceApi.Writes;
    for(int section=0;section<4;section++){
     Context=language+" scale="+scale+" width="+width+" section="+section;form.Surface.SelectSection(section);form.Refresh();Application.DoEvents();
     using(var bitmap=new Bitmap(form.Surface.Width,form.Surface.Height)){
      form.Surface.DrawToBitmap(bitmap,form.Surface.ClientRectangle);
      foreach(var row in form.Surface.Pages[section]){
       Require(form.Surface.ClientRectangle.Contains(row.RowBounds),"Row extends outside canvas");Require(row.RowBounds.Contains(row.TitleBounds) && row.RowBounds.Contains(row.DescriptionBounds),"Text extends outside card");
       Require(!row.TitleBounds.IntersectsWith(row.Action.Bounds) && !row.DescriptionBounds.IntersectsWith(row.Action.Bounds),"Action overlaps text");
       Require(HasInk(bitmap,row.TitleBounds),"Missing title: "+row.Title);Require(HasInk(bitmap,row.DescriptionBounds),"Missing description: "+row.Title);
       var toggle=row.Action as DeskPagesModernToggle;if(toggle!=null)Require(toggle.ClientRectangle.Contains(toggle.TrackBounds),"Clipped switch");rows++;
      }
      foreach(Control c in form.Surface.Controls)if(c.Visible)Require(form.Surface.ClientRectangle.Contains(c.Bounds),"Child extends outside canvas: "+c.AccessibleName);
     }
     if(width==760 && scale==1.25F){using(var b=new Bitmap(form.ClientSize.Width,form.ClientSize.Height)){using(var g=Graphics.FromImage(b))g.CopyFromScreen(form.PointToScreen(Point.Empty),Point.Empty,b.Size);b.Save(Path.Combine(output,language+"-settings-section-"+section+".png"));}}
     screens++;
    }
    Require(writes==DeskPagesWorkspaceApi.Writes,"Section navigation changed preferences");
    if(language=="en" && scale==1 && width==760){
     var basic=Field<DeskPagesModernToggle[]>(form,"basic");basic[0].Checked=false;
     typeof(DeskPagesSimpleSettings).GetMethod("Persist",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
     Require(DeskPagesWorkspaceApi.LastPreferences.Length==9 && DeskPagesWorkspaceApi.LastPreferences[1]=="False" && DeskPagesWorkspaceApi.LastPreferences[2]=="True","Preference mapping changed");
     form.Surface.SelectSection(0);form.Surface.Replay.PerformClick();Require(DeskPagesWorkspaceApi.Tours==1,"Replay button was disconnected");
     form.Surface.SelectSection(3);form.Surface.SaveLayout.PerformClick();Require(DeskPagesWorkspaceApi.Layouts==1,"Save layout button was disconnected");
     form.Surface.SelectSection(0);form.Surface.Performance.PerformClick();Require(DeskPagesWorkspaceApi.Measurements==1,"Resource measurement button was disconnected");
     var capture=Field<ComboBox>(form,"captureMode");Require(capture.SelectedIndex==0,"Direct capture was not the default");capture.SelectedIndex=1;Require(DeskPagesWorkspaceApi.CacheEnabled,"Cached mode was not persisted");form.RefreshData();Require(capture.SelectedIndex==1,"Cached mode was not loaded");DeskPagesWorkspaceApi.CacheDenied=true;capture.SelectedIndex=0;Require(capture.SelectedIndex==1 && DeskPagesWorkspaceApi.CacheEnabled,"Rejected capture mode was not restored");DeskPagesWorkspaceApi.CacheDenied=false;capture.SelectedIndex=0;Require(!DeskPagesWorkspaceApi.CacheEnabled && DeskPagesWorkspaceApi.CacheWrites==2,"Direct mode was not restored");
     var primary=Field<DeskPagesModernToggle>(form,"primaryOnly");DeskPagesWorkspaceApi.ScopeDenied=true;primary.Checked=true;Require(!primary.Checked && !DeskPagesWorkspaceApi.PrimaryOnly,"Rejected display scope was not restored");DeskPagesWorkspaceApi.ScopeDenied=false;
    }
    form.Close();
   }
  }
  Console.WriteLine("PASS: "+screens+" real settings screens / "+rows+" rows; EN/AR/HE; explicit 100/125/150/200% geometry; normal/narrow windows; rendered text, full switches, bounded controls; preference mapping, replay/save actions, rejected display-scope rollback, capture-mode persistence/reload/rollback, resource measurement action. Fixture only; physical Windows DPI acceptance remains separate.");return 0;
 }
}
