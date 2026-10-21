using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
// Ordinary Hidden/System paging and descendant project writes are not scene changes.
public static class DeskPagesSnapshotFiles {
 public static string Stamp(string folder){
  var b=new StringBuilder(folder);if(Directory.Exists(folder)){
   var paths=Directory.GetFileSystemEntries(folder);Array.Sort(paths,StringComparer.OrdinalIgnoreCase);
   foreach(var path in paths){b.Append('|').Append(path);if(File.Exists(path)){var f=new FileInfo(path);b.Append(':').Append(f.Length).Append(':').Append(f.LastWriteTimeUtc.Ticks);}}
  }
  using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(b.ToString()))).Replace("-","");
 }
}
