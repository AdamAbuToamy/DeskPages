using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// The popup lives as long as its stable owner. Closing a ToolStrip is not a
// disposal boundary: WinForms still uses it while unwinding mouse/menu messages.
internal sealed class GroupPopup:IDisposable {
    private readonly Control owner;private readonly Func<LiveGroup,bool> exists;
    private readonly Action<LiveGroup> remove,add;private readonly Action<GroupWindow> activate,drag;
    internal readonly ContextMenuStrip Menu=new ContextMenuStrip();
    private LiveGroup pending;private Point location;private bool scheduled,disposed;
    public GroupPopup(Control stableOwner,Func<LiveGroup,bool> present,Action<LiveGroup> delete,Action<GroupWindow> focus,Action<GroupWindow> beginDrag,Action<LiveGroup> choose=null){add=choose;owner=stableOwner;exists=present;remove=delete;activate=focus;drag=beginDrag;}
    public void Show(LiveGroup group,Point at){pending=group;location=at;Schedule();}
    private void Schedule(){if(disposed || scheduled || owner.IsDisposed)return;scheduled=true;owner.BeginInvoke((Action)Present);}
    private void Present(){
        scheduled=false;if(disposed || owner.IsDisposed || pending==null || !exists(pending))return;
        if(Menu.Visible){Menu.Close();Schedule();return;}
        foreach(ToolStripItem item in Menu.Items.Cast<ToolStripItem>().ToArray()){Menu.Items.Remove(item);item.Dispose();}
        var group=pending;
        if(add!=null){Menu.Items.Add("Add open apps…",null,delegate{Defer(delegate{if(exists(group))add(group);});});Menu.Items.Add(new ToolStripSeparator());}
        foreach(var window in group.Windows.Values){
            var member=window;var item=Menu.Items.Add(member.Title);
            item.Click+=delegate{Defer(delegate{activate(member);});};
            item.MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left)drag(member);};
        }
        if(group.Windows.Count==0){
            Menu.Items.Add("Drag an open taskbar app here").Enabled=false;
            Menu.Items.Add("Delete empty group",null,delegate{Defer(delegate{if(exists(group) && group.Windows.Count==0)remove(group);});});
        }
        Menu.Show(owner,owner.PointToClient(location));
    }
    private void Defer(Action action){if(!disposed && !owner.IsDisposed)owner.BeginInvoke((Action)delegate{if(!disposed)action();});}
    public void Dispose(){if(disposed)return;disposed=true;pending=null;Menu.Dispose();}
}
