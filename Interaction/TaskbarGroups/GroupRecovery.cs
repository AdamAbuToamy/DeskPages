using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;

internal sealed class GroupLease {
    public GroupWindow Window;public string[] Original,Assigned,Previous;
}
internal sealed class GroupRecovery {
    private static readonly uint[] Keys={2,3,4,5};
    private readonly string folder,marker;
    private readonly Dictionary<string,GroupLease> leases=new Dictionary<string,GroupLease>();
    public GroupRecovery(string session){Guid id;if(!Guid.TryParseExact(session,"N",out id))throw new ArgumentException("Invalid group session");folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPages","TaskbarGroups",session);marker="DeskPages.TaskbarGroups."+session;Directory.CreateDirectory(folder);}
    public string Folder{get{return folder;}}
    private string Journal{get{return Path.Combine(folder,"windows.dat");}}
    public void Assign(GroupWindow window,string[] values){
        if(values.Length!=4 || !WindowShell.Same(window))throw new InvalidOperationException("The dragged window has closed or changed");
        var h=new IntPtr(window.Handle);GroupLease lease;bool added=!leases.TryGetValue(window.Key,out lease);
        if(added){if(leases.Count>=512)throw new InvalidOperationException("Maximum 512 grouped windows");lease=new GroupLease{Window=window,Original=Keys.Select(k=>WindowShell.Get(h,k)).ToArray(),Previous=new string[4]};leases.Add(window.Key,lease);}
        else lease.Previous=lease.Assigned;
        lease.Assigned=values;Save(); // Durable intent before the first property change.
        try{
            if(!WindowShell.SetPropW(h,marker,new IntPtr(1)))throw new InvalidOperationException("This window does not permit grouping");
            for(int i=0;i<Keys.Length;i++)WindowShell.Set(h,Keys[i],values[i]);
            for(int i=0;i<Keys.Length;i++)if(WindowShell.Get(h,Keys[i])!=values[i])throw new InvalidOperationException("The application rejected taskbar grouping");
        }catch{Restore(window);throw;}
    }
    public void Restore(GroupWindow window){GroupLease lease;if(!leases.TryGetValue(window.Key,out lease))return;if(RestoreLease(lease,marker)){leases.Remove(window.Key);Save();}}
    public void RestoreAll(){foreach(var lease in leases.Values.ToArray())if(RestoreLease(lease,marker))leases.Remove(lease.Window.Key);Save();}
    public bool Owns(GroupWindow w){return leases.ContainsKey(w.Key);}
    public bool Empty{get{return leases.Count==0;}}
    private static bool RestoreLease(GroupLease lease,string marker){
        if(!WindowShell.Same(lease.Window))return true;var h=new IntPtr(lease.Window.Handle);
        if(WindowShell.GetPropW(h,marker)==IntPtr.Zero)return true;
        try{
            for(int i=0;i<Keys.Length;i++){
                string current=WindowShell.Get(h,Keys[i]);
                // Preserve changes made independently by the application itself.
                if(current!=null && (current==lease.Assigned[i] || (lease.Previous!=null && current==lease.Previous[i])))WindowShell.Set(h,Keys[i],lease.Original[i]);
            }
            WindowShell.RemovePropW(h,marker);return true;
        }catch(COMException){return false;}catch(System.ComponentModel.Win32Exception){return false;}
    }
    private void Save(){
        using(var stream=new MemoryStream()){
            using(var writer=new BinaryWriter(stream,Encoding.UTF8,true)){
                writer.Write("DeskPagesGroups1");writer.Write(leases.Count);
                foreach(var lease in leases.Values){writer.Write(lease.Window.Handle);writer.Write(lease.Window.ProcessId);writer.Write(lease.Window.Started);foreach(var values in new[]{lease.Original,lease.Assigned,lease.Previous??new string[4]})foreach(string value in values){writer.Write(value!=null);if(value!=null)writer.Write(value);}}
            }
            if(stream.Length>190000)throw new InvalidOperationException("Taskbar recovery journal is full");
            DeskPagesDragProtocol.AtomicText(Journal,Convert.ToBase64String(stream.ToArray()));
        }
    }
    private GroupLease[] Read(){
        string text;if(!DeskPagesDragProtocol.TryReadText(Journal,out text)){if(File.Exists(Journal))throw new IOException("Recovery journal is temporarily unavailable");return new GroupLease[0];}
        using(var stream=new MemoryStream(Convert.FromBase64String(text)))using(var reader=new BinaryReader(stream,Encoding.UTF8)){
            if(reader.ReadString()!="DeskPagesGroups1")throw new InvalidDataException("Invalid recovery journal");int count=reader.ReadInt32();if(count<0 || count>512)throw new InvalidDataException("Too many windows");var result=new List<GroupLease>();
            for(int n=0;n<count;n++){
                var lease=new GroupLease{Window=new GroupWindow{Handle=reader.ReadInt64(),ProcessId=reader.ReadInt32(),Started=reader.ReadInt64()}};var arrays=new string[3][];
                for(int a=0;a<3;a++){arrays[a]=new string[4];for(int i=0;i<4;i++){arrays[a][i]=reader.ReadBoolean()?reader.ReadString():null;if(arrays[a][i]!=null && arrays[a][i].Length>32768)throw new InvalidDataException("Oversized property");}}
                lease.Original=arrays[0];lease.Assigned=arrays[1];lease.Previous=arrays[2];result.Add(lease);
            }
            if(stream.Position!=stream.Length)throw new InvalidDataException("Trailing recovery data");return result.ToArray();
        }
    }
    public static int Watch(string session,int pid,long started){
        var recovery=new GroupRecovery(session);File.WriteAllText(Path.Combine(recovery.folder,"watch-ready"),"ready");
        while(OwnerAlive(pid,started))Thread.Sleep(300);
        try{foreach(var lease in recovery.Read())recovery.leases[lease.Window.Key]=lease;for(int attempt=0;attempt<5 && !recovery.Empty;attempt++){recovery.RestoreAll();if(!recovery.Empty)Thread.Sleep(200);}return recovery.Empty?0:1;}catch(Exception){return 1;}
    }
    private static bool OwnerAlive(int pid,long started){try{using(var p=Process.GetProcessById(pid))return !p.HasExited && p.StartTime.ToUniversalTime().Ticks==started;}catch(ArgumentException){return false;}catch(System.ComponentModel.Win32Exception){return true;}}
    public static void RecoverOrphans(){
        string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPages","TaskbarGroups");if(!Directory.Exists(root))return;
        foreach(string directory in Directory.GetDirectories(root).Take(512)){
            Guid token;if(!Guid.TryParseExact(Path.GetFileName(directory),"N",out token))continue;
            try{
                string[] owner=File.ReadAllLines(Path.Combine(directory,"owner.txt"));int pid;long started;
                if(owner.Length!=2 || !int.TryParse(owner[0],out pid) || !long.TryParse(owner[1],out started) || OwnerAlive(pid,started))continue;
                var recovery=new GroupRecovery(token.ToString("N"));foreach(var lease in recovery.Read())recovery.leases[lease.Window.Key]=lease;recovery.RestoreAll();
                if(!recovery.Empty)throw new InvalidOperationException("Some previous taskbar groups could not be restored");
                Directory.Delete(directory,true);
            }catch(FileNotFoundException){ }catch(IOException){ }
        }
    }
}
