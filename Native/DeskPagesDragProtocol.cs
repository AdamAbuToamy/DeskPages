using System;
using System.IO;
using System.Text;

// Small data-only IPC. No script/XML deserialization, arbitrary command or file destination.
public sealed class DeskPagesDragRequest {
    public string Id,Kind;
    public int X,Y;
    public string[] Paths;
}
public static class DeskPagesDragProtocol {
    public static string SessionPath(string token){
        Guid value;if(!Guid.TryParseExact(token,"N",out value))throw new ArgumentException("Invalid drag session token");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPages","DragSessions",token);
    }
    public static void AtomicText(string path,string text){
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,text,Encoding.UTF8);if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    // Atomic replacement must coexist with readers on Windows. Never hold a
    // read handle which denies DELETE sharing, and never delete another peer's request.
    public static bool TryReadText(string path,out string text){
        text=null;
        try{
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
                if(stream.Length>262144)throw new InvalidDataException("Drag packet too large");
                using(var reader=new StreamReader(stream,Encoding.UTF8,true))text=reader.ReadToEnd();
                if(text.Length>262144)throw new InvalidDataException("Drag packet too large");
            }
            return true;
        }catch(InvalidDataException){throw;}catch(IOException){return false;}
    }
    public static bool TryReadLines(string path,out string[] lines){
        string text;lines=null;if(!TryReadText(path,out text))return false;
        lines=text.Replace("\r","").TrimEnd('\n').Split('\n');return true;
    }
    public static bool TryAtomicText(string path,string text){
        try{AtomicText(path,text);return true;}catch(IOException){return false;}
    }
    public static bool NavigationSettled(string[] lines,string id){
        return lines!=null && lines.Length==3 && lines[0]=="DeskPagesDragVisual1" && lines[1]==id && lines[2]=="SETTLED";
    }
    public static string Encode(DeskPagesDragRequest request){
        var text=new StringBuilder("DeskPagesDrag1\n");text.Append(request.Id).Append('\n').Append(request.Kind).Append('\n').Append(request.X).Append('\n').Append(request.Y).Append('\n');
        string[] paths=request.Paths??new string[0];text.Append(paths.Length).Append('\n');foreach(string path in paths)text.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(path))).Append('\n');return text.ToString();
    }
    public static DeskPagesDragRequest Decode(string text){
        if(text==null || text.Length>262144)throw new ArgumentException("Invalid drag packet size");
        string[] lines=text.Replace("\r","").TrimEnd('\n').Split('\n');Guid id;int x,y,count;
        if(lines.Length<6 || lines[0]!="DeskPagesDrag1" || !Guid.TryParseExact(lines[1],"N",out id) || !int.TryParse(lines[3],out x) || !int.TryParse(lines[4],out y) || !int.TryParse(lines[5],out count) || count<0 || count>256 || lines.Length!=6+count)throw new ArgumentException("Invalid drag packet");
        string kind=lines[2];if(kind!="Arm" && kind!="Navigate" && kind!="Drop" && kind!="Cancel")throw new ArgumentException("Unknown drag operation");
        if(kind=="Arm" ? count<1 : count!=0)throw new ArgumentException("Invalid drag path list");
        if(kind=="Navigate" && (x!=-1 && x!=1))throw new ArgumentException("Invalid drag direction");
        if(Math.Abs((long)x)>1000000 || Math.Abs((long)y)>1000000)throw new ArgumentException("Invalid drop coordinates");
        string[] paths=new string[count];for(int i=0;i<count;i++){paths[i]=Encoding.UTF8.GetString(Convert.FromBase64String(lines[6+i]));if(paths[i].Length<1 || paths[i].Length>32760 || paths[i].IndexOf('\0')>=0)throw new ArgumentException("Invalid drag path");}
        return new DeskPagesDragRequest{Id=id.ToString("N"),Kind=kind,X=x,Y=y,Paths=paths};
    }
}
public sealed class DeskPagesEdgeDragPolicy {
    public const int EdgeWidth=48,RearmDistance=80,DwellMilliseconds=320,CooldownMilliseconds=650;
    public int ZoneWidth=EdgeWidth,HoldMilliseconds=DwellMilliseconds;
    private int edge;private long since,lastSwitch=-10000;private bool latched;
    public bool IsLatched{get{return latched;}}
    public int Direction(int x,int left,int right){return x<=left+ZoneWidth?-1:(x>=right-ZoneWidth?1:0);}
    public int Observe(long now,int x,int left,int right,bool dragging){return Observe(now,x,left,right,dragging,true);}
    public int Observe(long now,int x,int left,int right,bool dragging,bool canSwitch){
        if(!dragging){Reset();return 0;}
        if(x>left+Math.Max(RearmDistance,ZoneWidth+20) && x<right-Math.Max(RearmDistance,ZoneWidth+20)){edge=0;latched=false;return 0;}
        int next=Direction(x,left,right);
        if(next==0){edge=0;return 0;}
        if(next!=edge){edge=next;since=now;}
        if(!canSwitch || latched || now-since<HoldMilliseconds || now-lastSwitch<CooldownMilliseconds)return 0;
        latched=true;lastSwitch=now;return next;
    }
    public void Reset(){edge=0;latched=false;since=0;}
}
