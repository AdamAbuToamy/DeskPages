using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

// File metadata is read only by the worker. Navigation reads cached digests.
public sealed class DeskPagesSnapshotCatalog:IDisposable {
    readonly object gate=new object();readonly Func<string,string> stamp;readonly Action<int[]> invalidated;readonly Action ready;
    string[] rows=new string[0];int[] pages=new int[0];Dictionary<int,string> digests=new Dictionary<int,string>();long revision;bool running,disposed;
    public DeskPagesSnapshotCatalog(Action<int[]> changed,Action completed):this(changed,completed,FileStamp){}
    public DeskPagesSnapshotCatalog(Action<int[]> changed,Action completed,Func<string,string> read){invalidated=changed;ready=completed;stamp=read;}
    static string FileStamp(string path){try{var f=new FileInfo(path);return f.Exists?f.Length+":"+f.LastWriteTimeUtc.Ticks:(Directory.Exists(path)?"folder":"missing");}catch(IOException){return "unavailable";}catch(UnauthorizedAccessException){return "unavailable";}}
    public void SetPages(int[] value){lock(gate){if(pages.SequenceEqual(value))return;pages=(int[])value.Clone();RefreshLocked();}}
    public void SetRows(string[] value){
        int[] affected=new int[0];lock(gate){if(rows.SequenceEqual(value))return;
            if(rows.Length>0){var delta=rows.Except(value,StringComparer.OrdinalIgnoreCase).Concat(value.Except(rows,StringComparer.OrdinalIgnoreCase));affected=Affected(delta);foreach(int p in affected)digests.Remove(p);}
            rows=(string[])value.Clone();RefreshLocked();
        }if(affected.Length>0)invalidated(affected);
    }
    int[] Affected(IEnumerable<string> values){var result=new HashSet<int>();foreach(string row in values){var parts=row.Split('|');int page;if(parts.Length<3 || !int.TryParse(parts[1],out page))continue;if(string.Equals(parts[2],"True",StringComparison.OrdinalIgnoreCase)){foreach(int p in pages)result.Add(p);}else result.Add(page);}return result.ToArray();}
    public void FileChanged(string path,int fallbackPage){
        bool unknown;lock(gate){unknown=!rows.Any(r=>r.StartsWith(path+"|",StringComparison.OrdinalIgnoreCase));RefreshLocked();}
        // Keep the last validated digest while rechecking duplicate events.
        // A genuinely new, not-yet-indexed item still refreshes the live page.
        if(unknown)invalidated(new[]{fallbackPage});
    }
    public string Digest(int page){lock(gate){string value;return digests.TryGetValue(page,out value)?value:"";}}
    void RefreshLocked(){revision++;if(running||disposed)return;running=true;ThreadPool.QueueUserWorkItem(delegate{Run();});}
    void Run(){
        for(;;){string[] copy;int[] ids;long version;lock(gate){if(disposed){running=false;return;}copy=(string[])rows.Clone();ids=(int[])pages.Clone();version=revision;}
            var tokens=new Dictionary<int,List<string>>();foreach(int id in ids)tokens[id]=new List<string>();
            foreach(string row in copy){var parts=row.Split('|');int page;if(parts.Length<3||!int.TryParse(parts[1],out page))continue;string token=row+"|"+stamp(parts[0]);
                if(string.Equals(parts[2],"True",StringComparison.OrdinalIgnoreCase)){foreach(var list in tokens.Values)list.Add(token);}else if(tokens.ContainsKey(page))tokens[page].Add(token);
            }
            var result=new Dictionary<int,string>();using(var hash=SHA256.Create())foreach(var pair in tokens){pair.Value.Sort(StringComparer.OrdinalIgnoreCase);result[pair.Key]=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n",pair.Value)))).Replace("-","");}
            int[] affected;lock(gate){if(disposed){running=false;return;}if(version!=revision)continue;affected=result.Where(p=>digests.ContainsKey(p.Key) && digests[p.Key]!=p.Value).Select(p=>p.Key).ToArray();digests=result;running=false;}
            if(affected.Length>0)invalidated(affected);
            ready();return;
        }
    }
    public void Dispose(){lock(gate){disposed=true;}}
}
