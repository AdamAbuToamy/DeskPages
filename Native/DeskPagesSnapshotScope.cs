using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Runtime.InteropServices;
public static class DeskPagesSnapshotScope {
 [StructLayout(LayoutKind.Sequential)]struct Point{public int X,Y;}
 [DllImport("user32.dll")]static extern IntPtr MonitorFromPoint(Point point,uint flags);
 [DllImport("shcore.dll")]static extern int GetDpiForMonitor(IntPtr monitor,int kind,out uint x,out uint y);
 public static string DisplayContext(bool primaryOnly,string primary,string[] displays){
  return primaryOnly?"primary|"+primary:"all|"+string.Join("|",displays);
 }
 public static string Hash(string context){
  var b=new StringBuilder("DeskPagesSnapshot4|").Append(System.Globalization.CultureInfo.CurrentUICulture.Name).Append(context);
  var displays=new System.Collections.Generic.List<string>();string primary="";
  foreach(var screen in Screen.AllScreens){var d=new StringBuilder().Append(screen.WorkingArea).Append(screen.Bounds);uint x,y;if(GetDpiForMonitor(MonitorFromPoint(new Point{X=screen.Bounds.Left,Y=screen.Bounds.Top},2),0,out x,out y)==0)d.Append('|').Append(x).Append(':').Append(y);if(screen.Primary)primary=d.ToString();displays.Add(screen.DeviceName+"|"+d);}
  b.Append(DisplayContext(DeskPagesWorkspaceApi.PrimaryOnly,primary,displays.ToArray()));
  foreach(var folder in new[]{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)}){
   b.Append('|').Append(DeskPagesSnapshotFiles.Stamp(folder));
  }
  foreach(var path in new[]{@"Control Panel\Desktop",@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",@"Software\Microsoft\Windows\DWM"})using(var key=Registry.CurrentUser.OpenSubKey(path)){
   if(key==null)continue;foreach(var name in new[]{"Wallpaper","WallPaperStyle","TileWallpaper","AppsUseLightTheme","SystemUsesLightTheme","ColorizationColor","AccentColor"}){var value=key.GetValue(name);b.Append('|').Append(name).Append('=').Append(value);if(name=="Wallpaper" && value is string && File.Exists((string)value))b.Append(File.GetLastWriteTimeUtc((string)value).Ticks);}
  }
  using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(b.ToString()))).Replace("-","");
 }
}
