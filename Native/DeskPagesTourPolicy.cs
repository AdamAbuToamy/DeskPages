// The tour never invokes a workspace mutation: only the real + Page button can add a page.
public sealed class DeskPagesTourPolicy {
    public int Step {get;private set;}
    private int before;
    public void Start(int count){before=count;Step=count<=1?1:2;}
    public bool PageAdded(int count){if(Step!=1 || count<=before)return false;Step=2;return true;}
    public void Finish(){Step=0;}
}
