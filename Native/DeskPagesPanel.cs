using System;
using System.Drawing;
using System.Windows.Forms;

// A normal small utility window; never owns, replaces or captures the desktop.
public sealed class DeskPagesPanel:Form {
    private readonly ListBox pages;
    private readonly Label current;
    private readonly TextBox name;
    private readonly Action<int> command;
    private readonly Func<int,string> pageName;
    private int[] ids=new int[0];
    private int activePage;
    public DeskPagesPanel(Action<int> dispatch,Func<int,string> names,Action<int,string> rename,bool[] settings,Action<int,bool> change,Action<string> document){
        command=dispatch;pageName=names;Text="DeskPages";ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
        FormBorderStyle=FormBorderStyle.FixedToolWindow;MaximizeBox=false;MinimizeBox=false;
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        ClientSize=new Size(400,540);Font=new Font("Segoe UI",10);BackColor=DeskPagesTheme.Canvas;Padding=new Padding(12);
        KeyPreview=true;KeyDown+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.Escape){Hide();e.Handled=true;}};
        FormClosing+=delegate(object s,FormClosingEventArgs e){if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};
        var tabs=new TabControl{Dock=DockStyle.Fill};Controls.Add(tabs);
        var home=new TabPage("Pages"){BackColor=BackColor,Padding=new Padding(10)};
        var options=new TabPage("Settings"){BackColor=BackColor,Padding=new Padding(10)};
        var about=new TabPage("About"){BackColor=BackColor,Padding=new Padding(10)};tabs.TabPages.AddRange(new[]{home,options,about});DeskPagesTheme.Tabs(tabs);DeskPagesTheme.Header(this,"Your desktop, a little more organized.");
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<6;i++)layout.RowStyles.Add(new RowStyle(i==1?SizeType.Percent:SizeType.AutoSize,i==1?100:0));home.Controls.Add(layout);
        current=new Label{AutoSize=true,Margin=new Padding(0,0,0,12),Font=new Font(Font,FontStyle.Bold)};layout.Controls.Add(current,0,0);
        pages=new ListBox{Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,IntegralHeight=false,ItemHeight=32,DrawMode=DrawMode.OwnerDrawFixed,AccessibleName="Desktop pages"};
        pages.DrawItem+=delegate(object s,DrawItemEventArgs e){e.DrawBackground();if(e.Index<0)return;TextRenderer.DrawText(e.Graphics,pages.Items[e.Index].ToString(),Font,e.Bounds,e.ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);e.DrawFocusRectangle();};
        pages.DoubleClick+=delegate{Go();};pages.KeyDown+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.Enter){Go();e.Handled=true;}};layout.Controls.Add(pages,0,1);
        var row=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,10,0,8)};
        row.Controls.Add(Button("Open",delegate{Go();}));row.Controls.Add(Button("+ Add page",delegate{command(100);}));row.Controls.Add(Button("Delete active",delegate{command(101);}));layout.Controls.Add(row,0,2);
        name=new TextBox{Dock=DockStyle.Fill,MaxLength=40,AccessibleName="Page name"};pages.SelectedIndexChanged+=delegate{if(pages.SelectedIndex>=0)name.Text=names(ids[pages.SelectedIndex]);};layout.Controls.Add(name,0,3);
        var naming=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,8,0,8)};
        naming.Controls.Add(Button("Rename",delegate{if(pages.SelectedIndex<0)return;try{int id=ids[pages.SelectedIndex];rename(id,name.Text);SetPages(ids,activePage);}catch(Exception ex){MessageBox.Show(this,ex.Message,"DeskPages");}}));
        naming.Controls.Add(Button("Move selected → next",delegate{command(200);}));layout.Controls.Add(naming,0,4);
        layout.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(310,0),ForeColor=Color.DimGray,Text="Win + ← / → on the desktop\r\nFiles keep their original paths.",Margin=new Padding(0,8,2,0)},0,5);
        var flow=Flow(options);
        string[] labels={"Two-finger swipe","Reverse swipe direction","Win + arrows on desktop","Slide animation","Show page number","Drag files across page edges"};
        for(int i=0;i<labels.Length;i++){int setting=i;var check=new DeskPagesSwitch{Text=labels[i],Checked=settings[i],AutoSize=true,Margin=new Padding(0,0,0,12)};check.CheckedChanged+=delegate{change(setting,check.Checked);};flow.Controls.Add(check);}
        flow.Controls.Add(Button("Workspace tools…",delegate{command(9700);}));
        flow.Controls.Add(Button("Export pages and layouts…",delegate{command(9400);}));
        flow.Controls.Add(Button("Open desktop folder",delegate{command(9401);}));
        var advanced=new ContextMenuStrip();advanced.Items.Add("Input diagnostics",null,delegate{command(9001);});advanced.Items.Add("Open local log",null,delegate{command(9003);});advanced.Items.Add("Prepare restart check",null,delegate{command(9200);});advanced.Items.Add("Open restart report",null,delegate{command(9201);});
        Button more=Button("Advanced…",null);more.Click+=delegate{advanced.Show(more,new Point(0,more.Height));};flow.Controls.Add(more);Disposed+=delegate{advanced.Dispose();};
        flow.Controls.Add(Button("Exit and show all files",delegate{command(9002);}));
        var info=Flow(about);info.Controls.Add(new Label{Text="DeskPages",Font=new Font("Segoe UI",22,FontStyle.Bold),AutoSize=true});
        info.Controls.Add(new Label{Text="Version 0.8.21\r\nCreated by Adam Abu Toamy\r\n© 2026 Adam Abu Toamy",AutoSize=true,Margin=new Padding(0,8,0,20)});
        info.Controls.Add(Button("Privacy",delegate{document("PRIVACY.txt");}));info.Controls.Add(Button("License",delegate{document("LICENSE.txt");}));
        info.Controls.Add(new Label{Text="No account. No telemetry.\r\nYour pages are stored on this PC.",AutoSize=true,ForeColor=Color.DimGray,Margin=new Padding(0,20,0,0)});
        DeskPagesTheme.Apply(this);
    }
    private static FlowLayoutPanel Flow(Control parent){var flow=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};parent.Controls.Add(flow);return flow;}
    private static Button Button(string text,Action action){var b=new DeskPagesActionButton{Text=text,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,MinimumSize=new Size(76,32),Padding=new Padding(5,2,5,2),Margin=new Padding(0,0,6,8),FlatStyle=FlatStyle.Flat,BackColor=Color.White};b.FlatAppearance.BorderColor=Color.FromArgb(214,222,233);if(action!=null)b.Click+=delegate{action();};return b;}
    private void Go(){if(pages.SelectedIndex>=0){command(1000+ids[pages.SelectedIndex]);Hide();}}
    public void SetPages(int[] pageIds,int currentPage){
        int selected=currentPage;
        if(activePage==currentPage && pages.SelectedIndex>=0 && pages.SelectedIndex<ids.Length)selected=ids[pages.SelectedIndex];
        ids=(int[])pageIds.Clone();activePage=currentPage;
        if(Array.IndexOf(ids,selected)<0)selected=currentPage;
        current.Text=pageName(activePage);pages.BeginUpdate();pages.Items.Clear();
        for(int i=0;i<ids.Length;i++){pages.Items.Add((ids[i]==activePage?"●  ":"    ")+pageName(ids[i]));if(ids[i]==selected)pages.SelectedIndex=i;}
        pages.EndUpdate();
    }
    public void OpenNearPointer(){var area=Screen.FromPoint(Cursor.Position).WorkingArea;int x=Cursor.Position.X-Width+20,y=Cursor.Position.Y-Height-12;Location=new Point(Math.Max(area.Left,Math.Min(x,area.Right-Width)),Math.Max(area.Top,Math.Min(y,area.Bottom-Height)));Show();Activate();}
}
