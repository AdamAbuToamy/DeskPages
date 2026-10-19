using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Windows.Forms;

// Managed layout and ordinary accessible buttons; no native tab header sizing.
public sealed class DeskPagesSectionPage:Panel { public string ToolTipText; }
public sealed class DeskPagesSections:UserControl {
    private readonly Panel body=new Panel{Dock=DockStyle.Fill};
    private readonly TableLayoutPanel navigation=new TableLayoutPanel{Dock=DockStyle.Top,Height=48,RowCount=1,Padding=new Padding(4),BackColor=Color.FromArgb(232,237,247)};
    private readonly List<DeskPagesActionButton> buttons=new List<DeskPagesActionButton>();
    private int selected=-1;
    public sealed class Pages:Collection<DeskPagesSectionPage>{
        private readonly DeskPagesSections owner;public Pages(DeskPagesSections host){owner=host;}
        protected override void InsertItem(int index,DeskPagesSectionPage page){base.InsertItem(index,page);owner.AddPage(page,index);}
    }
    public readonly Pages TabPages;
    public int TabCount {get{return TabPages.Count;}}
    public int SelectedIndex {get{return selected;}set{if(value<0 || value>=TabCount)return;selected=value;for(int i=0;i<TabCount;i++){TabPages[i].Visible=i==value;buttons[i].Primary=i==value;buttons[i].Invalidate();}TabPages[value].BringToFront();}}
    public DeskPagesSectionPage SelectedTab {get{return selected<0?null:TabPages[selected];}set{SelectedIndex=TabPages.IndexOf(value);}}
    public DeskPagesSections(){DoubleBuffered=true;TabPages=new Pages(this);Controls.Add(body);Controls.Add(navigation);body.Padding=new Padding(0,12,0,0);TabStop=false;}
    private void AddPage(DeskPagesSectionPage page,int index){
        page.Dock=DockStyle.Fill;page.Visible=false;body.Controls.Add(page);
        var button=new DeskPagesActionButton{Text=page.Text,AccessibleName=page.Text,Dock=DockStyle.Fill,AutoSize=false,MinimumSize=Size.Empty,Margin=new Padding(2),Padding=new Padding(2),Font=new Font("Segoe UI",9F)};
        int id=index;button.Click+=delegate{SelectedIndex=id;};button.AllowDrop=true;button.DragEnter+=delegate(object sender,DragEventArgs e){e.Effect=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Move:DragDropEffects.None;if(e.Effect!=DragDropEffects.None)SelectedIndex=id;};
        button.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Left || e.KeyCode==Keys.Right){int delta=e.KeyCode==Keys.Right?1:-1;if(RightToLeft==RightToLeft.Yes)delta=-delta;SelectedIndex=(selected+delta+TabCount)%TabCount;buttons[selected].Focus();e.Handled=true;}};
        buttons.Add(button);navigation.ColumnCount=buttons.Count;navigation.ColumnStyles.Clear();for(int i=0;i<buttons.Count;i++)navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F/buttons.Count));navigation.Controls.Add(button,index,0);
        if(selected<0)SelectedIndex=0;
    }
    public Rectangle GetTabRect(int index){Rectangle r=buttons[index].Bounds;r.Offset(navigation.Left,navigation.Top);return r;}
}

public sealed class DeskPagesField:Panel {
    public DeskPagesField(Control input){
        DoubleBuffered=true;BackColor=Color.White;Padding=new Padding(12,11,12,8);Height=42;MinimumSize=new Size(60,42);Margin=new Padding(0,3,0,3);
        var text=input as TextBox;if(text!=null)text.BorderStyle=BorderStyle.None;
        var combo=input as ComboBox;if(combo!=null){combo.FlatStyle=FlatStyle.Flat;Padding=new Padding(8,7,8,7);}
        input.Dock=DockStyle.Fill;input.Margin=Padding.Empty;Controls.Add(input);
    }
    protected override void OnEnter(EventArgs e){base.OnEnter(e);Invalidate();}
    protected override void OnLeave(EventArgs e){base.OnLeave(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;var r=new Rectangle(0,0,Math.Max(1,Width-1),Math.Max(1,Height-1));using(var path=DeskPagesTheme.Round(r,8))using(var pen=new Pen(ContainsFocus?DeskPagesTheme.Accent:DeskPagesTheme.Line)){e.Graphics.DrawPath(pen,path);}}
}

public sealed class DeskPagesDataList:ListView {
    private readonly ImageList spacing=new ImageList();private bool sizing;
    public DeskPagesDataList(){View=View.Details;Dock=DockStyle.Fill;BorderStyle=BorderStyle.None;HeaderStyle=ColumnHeaderStyle.None;FullRowSelect=true;HideSelection=false;OwnerDraw=true;ShowItemToolTips=true;BackColor=Color.White;DoubleBuffered=true;spacing.ImageSize=new Size(1,36);spacing.Images.Add(new Bitmap(1,36));SmallImageList=spacing;}
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);FitColumns();}
    public void FitColumns(){if(sizing || Columns.Count==0)return;sizing=true;try{int available=Math.Max(1,ClientSize.Width-SystemInformation.VerticalScrollBarWidth-6);for(int i=0;i<Columns.Count;i++){int width=(int)(available*Weight(i,Columns.Count));if(Columns[i].Width!=width)Columns[i].Width=width;}}finally{sizing=false;}}
    protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(Visible)FitColumns();}
    public static float Weight(int index,int count){return count==2?(index==0?.33F:.67F):(index==0?.50F:.50F/Math.Max(1,count-1));}
    protected override void OnDrawItem(DrawListViewItemEventArgs e){if(View!=View.Details)e.DrawDefault=true;}
    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e){
        bool selected=e.Item.Selected;Color color=selected?Color.FromArgb(234,239,255):(e.ItemIndex%2==0?Color.White:Color.FromArgb(248,250,253));using(var fill=new SolidBrush(color))e.Graphics.FillRectangle(fill,e.Bounds);
        Rectangle r=e.Bounds;r.Inflate(-10,0);var flags=TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|(RightToLeft==RightToLeft.Yes?TextFormatFlags.Right|TextFormatFlags.RightToLeft:TextFormatFlags.Left);
        TextRenderer.DrawText(e.Graphics,e.SubItem.Text,Font,r,selected?DeskPagesTheme.Accent:DeskPagesTheme.Ink,flags);
        if(Focused && e.Item.Focused && e.ColumnIndex==0)ControlPaint.DrawFocusRectangle(e.Graphics,e.Item.Bounds);
    }
    protected override void Dispose(bool disposing){if(disposing)spacing.Dispose();base.Dispose(disposing);}
}

public sealed class DeskPagesTable:Panel {
    private readonly ListView list;private readonly Label empty;
    public DeskPagesTable(ListView source,string title,string hint){
        DoubleBuffered=true;Dock=DockStyle.Fill;BackColor=Color.White;Padding=new Padding(10);
        list=source;var area=new Panel{Dock=DockStyle.Fill,BackColor=Color.White};area.Controls.Add(list);
        empty=new Label{Dock=DockStyle.Fill,Text=title+Environment.NewLine+hint,TextAlign=ContentAlignment.MiddleCenter,ForeColor=DeskPagesTheme.Muted,Padding=new Padding(12,4,12,4),BackColor=Color.White};area.Controls.Add(empty);
        var head=new TableLayoutPanel{Dock=DockStyle.Top,Height=34,ColumnCount=list.Columns.Count,BackColor=Color.FromArgb(244,247,252)};
        for(int i=0;i<list.Columns.Count;i++){head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100*DeskPagesDataList.Weight(i,list.Columns.Count)));head.Controls.Add(new Label{Text=list.Columns[i].Text,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=DeskPagesTheme.Muted,Padding=new Padding(9,0,0,0),Font=new Font("Segoe UI",9,FontStyle.Bold)},i,0);}
        Controls.Add(area);Controls.Add(head);UpdateEmpty();
    }
    public void UpdateEmpty(){bool show=list.Items.Count==0;empty.Visible=show;if(show)empty.BringToFront();var data=list as DeskPagesDataList;if(data!=null)data.FitColumns();}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(var path=DeskPagesTheme.Round(new Rectangle(0,0,Math.Max(1,Width-1),Math.Max(1,Height-1)),6))using(var pen=new Pen(DeskPagesTheme.Line))e.Graphics.DrawPath(pen,path);}
}

public sealed class DeskPagesSettingsCard:TableLayoutPanel {
    public DeskPagesSettingsCard(){DoubleBuffered=true;AutoSize=true;Dock=DockStyle.Top;ColumnCount=1;ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Padding=new Padding(14);Margin=new Padding(0,0,0,12);BackColor=Color.White;}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(var path=DeskPagesTheme.Round(new Rectangle(0,0,Math.Max(1,Width-1),Math.Max(1,Height-1)),6))using(var pen=new Pen(DeskPagesTheme.Line))e.Graphics.DrawPath(pen,path);}
}
