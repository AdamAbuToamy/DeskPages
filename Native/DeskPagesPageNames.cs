using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Separate cosmetic metadata: never changes membership, paths or Explorer layouts.
public sealed class DeskPagesPageNames {
    private readonly Dictionary<int,string> names=new Dictionary<int,string>();
    private readonly string path;
    public DeskPagesPageNames(string file){path=file;Load();}
    public static string Validate(string value){
        value=(value??"").Trim();
        if(value.Length<1 || value.Length>40)throw new ArgumentException("Use a page name between 1 and 40 characters.");
        foreach(char c in value)if(char.IsControl(c))throw new ArgumentException("Page names cannot contain control characters.");
        return value;
    }
    public string Get(int page){string name;return names.TryGetValue(page,out name)?name:(page==1?"Main page":"Page "+page);}
    public void Rename(int page,string value){
        if(page<1 || page>99)throw new ArgumentOutOfRangeException("page");
        value=Validate(value);var next=new Dictionary<int,string>(names);next[page]=value;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        var lines=new List<string>();lines.Add("DeskPagesNames1");
        foreach(var pair in next)lines.Add(pair.Key+":"+Convert.ToBase64String(Encoding.UTF8.GetBytes(pair.Value)));
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllLines(temp,lines,Encoding.UTF8);if(File.Exists(path))File.Replace(temp,path,path+".previous");else File.Move(temp,path);}
        finally{if(File.Exists(temp))File.Delete(temp);}
        names[page]=value;
    }
    private void Load(){
        try{if(!File.Exists(path))return;string[] lines=File.ReadAllLines(path);if(lines.Length==0 || lines[0]!="DeskPagesNames1")return;
            for(int i=1;i<lines.Length;i++){int split=lines[i].IndexOf(':');int id;if(split<1 || !int.TryParse(lines[i].Substring(0,split),out id) || id<1 || id>99)continue;
                try{names[id]=Validate(Encoding.UTF8.GetString(Convert.FromBase64String(lines[i].Substring(split+1))));}catch(ArgumentException){}catch(FormatException){}
            }
        }catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
