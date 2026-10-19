using System;
using System.IO;
using System.Diagnostics;

public sealed class DeskPagesDragMailbox:IDisposable {
    private readonly string folder;
    private string lastId,lastInvalidText,response,navigationId,lastVisual;
    private long lastPublish;
    public DeskPagesDragRequest Pending{get;private set;}
    public DeskPagesDragMailbox(string token){folder=DeskPagesDragProtocol.SessionPath(token);Directory.CreateDirectory(folder);using(var process=Process.GetCurrentProcess())DeskPagesDragProtocol.AtomicText(Path.Combine(folder,"owner.txt"),process.Id+"\n"+process.StartTime.ToUniversalTime().Ticks);Publish(1,false,0);}
    public void Publish(int page,bool enabled,int count){
        if(DeskPagesDragProtocol.TryAtomicText(Path.Combine(folder,"state.txt"),"DeskPagesDragState1\n"+page+"\n"+enabled+"\n"+count+"\n"+DateTime.UtcNow.Ticks))lastPublish=DateTime.UtcNow.Ticks;
    }
    public void PublishVisualState(bool transitionVisible){
        if(navigationId==null)return;
        string text="DeskPagesDragVisual1\n"+navigationId+"\n"+(transitionVisible?"PLAYING":"SETTLED");
        if(text==lastVisual)return;
        if(DeskPagesDragProtocol.TryAtomicText(Path.Combine(folder,"visual-state.txt"),text))lastVisual=text;
    }
    private bool FlushResponse(){
        if(response==null)return true;
        if(!DeskPagesDragProtocol.TryAtomicText(Path.Combine(folder,"response.txt"),response))return false;
        response=null;Pending=null;return true;
    }
    public bool Poll(int page,bool enabled,int count){
        if(DateTime.UtcNow.Ticks-lastPublish>TimeSpan.TicksPerSecond)Publish(page,enabled,count);
        // The command has already run. Retry only its acknowledgement, never dispatch again.
        if(response!=null){FlushResponse();return false;}
        if(Pending!=null)return false;
        string text;if(!DeskPagesDragProtocol.TryReadText(Path.Combine(folder,"request.txt"),out text))return false;
        if(text==lastInvalidText)return false;
        DeskPagesDragRequest request;
        try{request=DeskPagesDragProtocol.Decode(text);}
        catch(ArgumentException){lastInvalidText=text;throw;}
        catch(FormatException){lastInvalidText=text;throw;}
        if(request.Id==lastId)return false;Pending=request;return true;
    }
    public void Complete(bool success,int page){
        if(Pending==null)return;
        // Mark consumed before attempting IO; sharing failures retain this exact reply.
        if(response==null){if(success && Pending.Kind=="Navigate")navigationId=Pending.Id;lastId=Pending.Id;response=Pending.Id+"\n"+(success?"OK":"ERROR")+"\n"+page;}
        FlushResponse();
    }
    public void Dispose(){try{foreach(string file in new[]{"state.txt","request.txt","response.txt","owner.txt","visual-state.txt"})File.Delete(Path.Combine(folder,file));Directory.Delete(folder);}catch(IOException){}catch(UnauthorizedAccessException){}}
}
