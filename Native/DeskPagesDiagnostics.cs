using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

// Best-effort bounded telemetry never holds up input, animation or persistence.
public static class DeskPagesDiagnostics {
    sealed class Entry {public string Path,Header,Line;}
    static readonly ConcurrentQueue<Entry> queue=new ConcurrentQueue<Entry>();static int running;
    public static void Append(string path,string header,string line){
        if(string.IsNullOrEmpty(path))return;Entry removed;while(queue.Count>=128)queue.TryDequeue(out removed);
        queue.Enqueue(new Entry{Path=path,Header=header,Line=line});Start();
    }
    static void Start(){if(Interlocked.CompareExchange(ref running,1,0)==0)ThreadPool.QueueUserWorkItem(delegate{Drain();});}
    static void Drain(){
        try{Entry value;while(queue.TryDequeue(out value))try{Directory.CreateDirectory(System.IO.Path.GetDirectoryName(value.Path));if(!string.IsNullOrEmpty(value.Header) && !File.Exists(value.Path))File.WriteAllText(value.Path,value.Header);File.AppendAllText(value.Path,value.Line);}catch(IOException){}catch(UnauthorizedAccessException){}}
        finally{Interlocked.Exchange(ref running,0);if(!queue.IsEmpty)Start();}
    }
}
