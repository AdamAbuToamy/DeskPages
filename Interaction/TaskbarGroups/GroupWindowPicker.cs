using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

internal sealed class GroupWindowPicker:Form {
    private readonly Action<GroupWindow[]> assign;private readonly Func<GroupWindow[]> read;
    internal readonly ListView Apps=new ListView{Dock=DockStyle.Fill,View=View.Details,CheckBoxes=true,FullRowSelect=true,HideSelection=false,BorderStyle=BorderStyle.None};
    internal readonly Button Add=new Button{Text="Add selected",AutoSize=true,Enabled=false};
    private readonly Label status=new Label{Dock=DockStyle.Bottom,Height=32,Padding=new Padding(12,4,12,0)};
    private readonly ImageList icons=new ImageList{ImageSize=new Size(24,24),ColorDepth=ColorDepth.Depth32Bit};
    private int loading;
    public GroupWindowPicker(int number,Action<GroupWindow[]> commit,Func<GroupWindow[]> loader=null){
        assign=commit;read=loader??WindowShell.Windows;Text="Add open apps · "+number.ToString("00");Font=new Font("Segoe UI",10);BackColor=Color.White;Size=new Size(780,480);MinimumSize=new Size(620,340);StartPosition=FormStartPosition.CenterScreen;
        var heading=new Label{Text="Select open windows to add to group "+number.ToString("00"),Dock=DockStyle.Top,Height=54,Padding=new Padding(16),Font=new Font("Segoe UI Semibold",11)};
        var actions=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=54,Padding=new Padding(12),FlowDirection=FlowDirection.RightToLeft};
        var cancel=new Button{Text="Cancel",AutoSize=true};cancel.Click+=delegate{Close();};var refresh=new Button{Text="Refresh",AutoSize=true};refresh.Click+=delegate{Reload();};
        AcceptButton=Add;CancelButton=cancel;actions.Controls.Add(cancel);actions.Controls.Add(Add);actions.Controls.Add(refresh);Controls.Add(Apps);Controls.Add(status);Controls.Add(actions);Controls.Add(heading);
        Apps.Columns.Add("App",180);Apps.Columns.Add("Open window",520);Apps.SmallImageList=icons;var imageHandle=icons.Handle;
        Apps.ItemCheck+=delegate{if(IsHandleCreated && !IsDisposed)BeginInvoke((Action)delegate{if(!IsDisposed)Add.Enabled=Apps.CheckedItems.Count>0;});};
        Add.Click+=delegate{Commit();};Shown+=delegate{Reload();};
    }
    private void Reload(){
        if(Interlocked.CompareExchange(ref loading,1,0)!=0)return;Add.Enabled=false;status.Text="Reading open windows…";
        ThreadPool.QueueUserWorkItem(delegate{
            GroupWindow[] windows=null;Exception error=null;try{windows=read();}catch(Exception ex){error=ex;}
            if(IsDisposed || !IsHandleCreated){Interlocked.Exchange(ref loading,0);return;}
            try{BeginInvoke((Action)delegate{
                Interlocked.Exchange(ref loading,0);if(IsDisposed)return;Apps.BeginUpdate();
                try{
                    Apps.Items.Clear();icons.Images.Clear();
                    if(windows!=null)foreach(var window in windows.OrderBy(AppName).ThenBy(w=>w.Title)){
                        Icon icon=null;try{if(!string.IsNullOrEmpty(window.ImagePath))icon=Icon.ExtractAssociatedIcon(window.ImagePath);}catch(Exception){}
                        if(icon==null)icon=(Icon)SystemIcons.Application.Clone();using(icon)icons.Images.Add(icon);
                        var row=new ListViewItem(AppName(window),icons.Images.Count-1){Tag=window};row.SubItems.Add(window.Title);Apps.Items.Add(row);
                    }
                }finally{Apps.EndUpdate();}
                status.Text=error==null?Apps.Items.Count+" open windows. Select one or more, then Add selected.":"Could not read open windows: "+error.Message;
            });}catch(InvalidOperationException){Interlocked.Exchange(ref loading,0);}
        });
    }
    private static string AppName(GroupWindow window){try{return string.IsNullOrEmpty(window.ImagePath)?window.Title:Path.GetFileNameWithoutExtension(window.ImagePath);}catch(ArgumentException){return window.Title;}}
    private void Commit(){
        var selected=Apps.CheckedItems.Cast<ListViewItem>().Select(i=>(GroupWindow)i.Tag).ToArray();if(selected.Length==0)return;
        if(selected.Any(w=>!WindowShell.Same(w))){status.Text="A selected window closed. Refresh and select its current window.";return;}
        try{assign(selected);Close();}catch(Exception ex){status.Text="Could not add the selected windows: "+ex.Message;}
    }
    protected override void Dispose(bool disposing){if(disposing)icons.Dispose();base.Dispose(disposing);}
}
