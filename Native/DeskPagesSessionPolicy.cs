using System;

// The same policy is exercised without Windows callbacks in regression tests.
public sealed class DeskPagesSessionPolicy
{
    public bool Ending{get;private set;}
    private string armedToken;
    public bool Query(){return true;}
    public void End(bool ending,long flags,bool busy,Action checkpoint,Func<string> arm,Action<string> release,Action<string> log){
        if(!ending){
            Ending=false;
            if(armedToken!=null){try{release(armedToken);armedToken=null;}catch(Exception ex){log("cancelled shutdown gate restore failed: "+ex.Message);}}
            return;
        }
        if(Ending)return;
        Ending=true;
        try{if(!busy)checkpoint();else log("shutdown during transaction; preserving last committed layout");}
        catch(Exception ex){log("shutdown checkpoint preserved previous copy: "+ex.Message);}
        if((flags&1)==0){try{armedToken=arm();}catch(Exception ex){log("could not arm next logon: "+ex.Message);}}
    }
}
