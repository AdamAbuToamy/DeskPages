using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public static class DeskPagesBootPlan
{
    public sealed class Entry {public string Path;public int Page;}
    private static string Decode(string value){return Encoding.UTF8.GetString(Convert.FromBase64String(value));}
    private static string Normal(string path){return System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar,System.IO.Path.AltDirectorySeparatorChar);}
    public static string Hash(string path){using(var s=File.OpenRead(path))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-","");}
    public static Entry[] Parse(string[] lines,string manifestHash,string desktop,string common){
        if(lines.Length<4 || lines.Length>100004 || lines[0]!="DeskPagesMain1" || !string.Equals(lines[1],manifestHash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Missing/stale main-page plan.");
        if(!string.Equals(Normal(Decode(lines[2])),Normal(desktop),StringComparison.OrdinalIgnoreCase) || !string.Equals(Normal(Decode(lines[3])),Normal(common),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Boot plan belongs to another account.");
        var result=new List<Entry>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for(int i=4;i<lines.Length;i++){
            if(string.IsNullOrWhiteSpace(lines[i]))continue;
            string[] fields=lines[i].Split('\t');int page;
            if(fields.Length!=2 || !int.TryParse(fields[0],out page) || page<1 || page>99)throw new InvalidDataException("Invalid boot plan membership.");
            string decoded=Decode(fields[1]);if(!Path.IsPathRooted(decoded))throw new InvalidDataException("Relative boot path.");
            string path=Normal(decoded),parent=Normal(Path.GetDirectoryName(path));
            if((!string.Equals(parent,Normal(desktop),StringComparison.OrdinalIgnoreCase) && !string.Equals(parent,Normal(common),StringComparison.OrdinalIgnoreCase)) || !seen.Add(path))throw new InvalidDataException("Invalid/duplicate boot plan path.");
            result.Add(new Entry{Path=path,Page=page});
        }
        return result.ToArray();
    }
    public static int Apply(){
        string store=DeskPagesStartupGate.Store,plan=Path.Combine(store,"startup-main.plan"),manifest=Path.Combine(store,"pages.xml");
        if(!File.Exists(plan) || !File.Exists(manifest) || File.Exists(Path.Combine(store,"suspended.xml")))return 0;
        var applied=new List<Entry>();
        try{
            if(new FileInfo(plan).Length>4*1024*1024)throw new InvalidDataException("Oversize startup plan.");
            var entries=Parse(File.ReadAllLines(plan),Hash(manifest),Environment.GetFolderPath(Environment.SpecialFolder.Desktop),Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
            foreach(var entry in entries){
                if(!File.Exists(entry.Path) && !Directory.Exists(entry.Path))continue;
                var attrs=File.GetAttributes(entry.Path);if((attrs&FileAttributes.ReparsePoint)!=0)continue;
                bool hidden=(attrs&FileAttributes.Hidden)!=0,wanted=entry.Page!=1;
                if(hidden==wanted)continue;
                PutHidden(entry.Path,wanted);applied.Add(new Entry{Path=entry.Path,Page=hidden?2:1});
            }
            DeskPagesStartupGate.Log("native main-page file preparation changed="+applied.Count);return applied.Count;
        }catch(Exception ex){
            foreach(var old in applied){try{PutHidden(old.Path,old.Page!=1);}catch{}}
            DeskPagesStartupGate.Log("native main-page preparation skipped; agent will restore: "+ex.Message);return 0;
        }
    }
    private static void PutHidden(string path,bool hidden){
        var attrs=File.GetAttributes(path);if((attrs&FileAttributes.ReparsePoint)!=0)return;
        attrs=hidden?(attrs&~FileAttributes.Normal)|FileAttributes.Hidden:attrs&~FileAttributes.Hidden;
        if(attrs==0)attrs=FileAttributes.Normal;File.SetAttributes(path,attrs);
    }
}
