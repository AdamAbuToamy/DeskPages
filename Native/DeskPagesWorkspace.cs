using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

// All callbacks run on the agent's STA. No watcher, hook or worker mutates state.
public static class DeskPagesWorkspaceApi {
    public static DeskPagesWorkspaceRequest Request;
    private static DeskPagesWorkspace window;
    public static string Language="en";
    public static bool ReduceMotion;
    public static bool PrimaryOnly;
    public static bool[] Input;
    public static int WheelThreshold=40,PointerThreshold=350,EdgeWidth=48,EdgeDwell=320;
    private static readonly Dictionary<int,string> names=new Dictionary<int,string>();
    public static void Configure(string language,int wheel,int pointer,int edge,int dwell,bool reduce){Language=language;WheelThreshold=wheel;PointerThreshold=pointer;EdgeWidth=edge;EdgeDwell=dwell;ReduceMotion=reduce;}
    public static string FallbackName(int id){return new DeskPagesPageNames(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPages","HiddenPages","page-names.txt")).Get(id);}
    public static void SetNames(string[] rows){names.Clear();foreach(string row in rows){int i=row.IndexOf('\t'),id;if(i>0 && int.TryParse(row.Substring(0,i),out id))names[id]=row.Substring(i+1);}}
    public static string Name(int id){string name;return names.TryGetValue(id,out name)?name:FallbackName(id);}
    private static bool opening;
    private static DeskPagesSimpleSettings settings;
    public static void Open(){
        if(Request==null || opening)return;opening=true;
        try{if(window==null || window.IsDisposed || window.Locale!=Language){if(window!=null)window.Dispose();window=new DeskPagesWorkspace();}window.OpenNearPointer();window.QueueRefresh();}
        catch(Exception ex){DeskPagesStartupGate.Log("pages: "+ex.ToString());DeskPagesHost.ShowError(ex.Message);}finally{opening=false;}
    }
    public static void OpenSettings(){
        if(Request==null)return;if(window!=null)window.Hide();
        try{if(settings==null || settings.IsDisposed){settings=new DeskPagesSimpleSettings();settings.Show();settings.QueueRefresh();}else{settings.Show();settings.Activate();}}
        catch(Exception ex){DeskPagesStartupGate.Log("settings: "+ex.ToString());DeskPagesHost.ShowError(ex.Message);}
    }
    public static void MaybeWelcome(){try{if(DeskPagesTourStore.Seen || Request==null)return;var rows=Execute("pages");int count=0;foreach(var row in rows){var f=row.Split('\t');if(f.Length==6 && f[3]!="True")count++;}if(count<=1)StartTour();else DeskPagesTourStore.Mark();}catch(Exception ex){DeskPagesStartupGate.Log("welcome: "+ex.Message);}}
    public static void StartTour(){if(settings!=null)settings.Hide();Open();if(window!=null && !window.IsDisposed)window.BeginTour();}
    public static void RefreshSettingsLanguage(){if(settings!=null){settings.Dispose();settings=null;}OpenSettings();}
    public static string[] Execute(string command,params string[] args){return DeskPagesHost.WorkspaceCommand(delegate{return Request(command,args)??new string[0];});}
    public static Action CheckUpdatesAction;
    public static void CheckUpdates(){if(CheckUpdatesAction!=null)CheckUpdatesAction();}
    public static void PinSelected(bool pin){try{Execute(pin?"pin-selected":"unpin-selected");}catch(Exception ex){DeskPagesHost.ShowError(ex.Message);}}
    public static void Close(){if(window!=null)window.Dispose();if(settings!=null)settings.Dispose();window=null;settings=null;Request=null;}
    public static string L(string en,string ar,string he){return Language=="ar"?ar:(Language=="he"?he:en);}
}

public sealed class DeskPagesWorkspace:Form {
    private readonly ListBox pages=new ListBox{Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,IntegralHeight=false,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=70};
    private readonly System.Windows.Forms.Timer load=new System.Windows.Forms.Timer{Interval=80};
    private readonly List<Page> rows=new List<Page>();
    private readonly ContextMenuStrip menu=new ContextMenuStrip();
    public readonly string Locale;private bool sized;
    private sealed class Page {public int Id,Count;public string Name;public bool Active,Locked;public override string ToString(){return Name;}}
    private static string L(string en,string ar,string he){return DeskPagesWorkspaceApi.L(en,ar,he);}
    public DeskPagesWorkspace(){
        Locale=DeskPagesWorkspaceApi.Language;DeskPagesFluent.Configure(this,L("DeskPages","DeskPages","DeskPages"),new Size(400,420));
        var grid=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));grid.RowStyles.Add(new RowStyle(SizeType.Absolute,68));grid.RowStyles.Add(new RowStyle(SizeType.Percent,100));grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(grid);
        var title=DeskPagesFluent.Heading(L("Desktop pages","صفحات سطح المكتب","דפי שולחן העבודה"),L("Win + arrows to switch on the desktop","Win + الأسهم للتنقل على السطح","Win + חצים למעבר בשולחן העבודה"));grid.Controls.Add(title,0,0);
        pages.BackColor=DeskPagesTheme.Canvas;pages.AccessibleName=L("Desktop pages","صفحات سطح المكتب","דפי שולחן העבודה");pages.DrawItem+=DrawPage;grid.Controls.Add(pages,0,1);
        pages.DoubleClick+=delegate{Go();};pages.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Enter){Go();e.Handled=true;}};
        pages.ContextMenuStrip=menu;pages.MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Right){int index=pages.IndexFromPoint(e.Location);if(index>=0)pages.SelectedIndex=index;}};
        menu.Opening+=delegate{Safe(BuildMenu);};
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Margin=new Padding(0,10,0,0)};actions.Controls.Add(DeskPagesFluent.Button(L("Open","فتح","פתיחה"),Go,true));addPage=DeskPagesFluent.Button(L("+ Page","+ صفحة","+ דף"),delegate{Safe(delegate{DeskPagesWorkspaceApi.Execute("add",L("New page","صفحة جديدة","דף חדש"));RefreshData();if(tour.PageAdded(rows.Count))ShowNavigation();});},false);actions.Controls.Add(addPage);actions.Controls.Add(DeskPagesFluent.Button(L("Settings","الإعدادات","הגדרות"),DeskPagesWorkspaceApi.OpenSettings,false));grid.Controls.Add(actions,0,2);
        VisibleChanged+=delegate{if(!Visible){if(tour.Step>0)FinishTour();StopCoach();if(walkthrough!=null && !walkthrough.IsDisposed)walkthrough.Close();}};
        load.Tick+=delegate{load.Stop();RefreshData();};FormClosing+=delegate(object sender,FormClosingEventArgs e){if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};
    }
    private readonly DeskPagesTourPolicy tour=new DeskPagesTourPolicy();
    private DeskPagesActionButton addPage;private DeskPagesTourCoach coach;private DeskPagesNavigationTour walkthrough;
    public void BeginTour(){RefreshData();tour.Start(rows.Count);if(tour.Step==1){StopCoach();addPage.Primary=true;addPage.Invalidate();coach=new DeskPagesTourCoach(this,addPage,FinishTour);coach.Show(this);coach.Place();}else ShowNavigation();}
    private void StopCoach(){if(coach!=null){coach.Dispose();coach=null;}if(addPage!=null){addPage.Primary=false;addPage.Invalidate();}}
    private void FinishTour(){tour.Finish();StopCoach();DeskPagesTourStore.Mark();}
    private void ShowNavigation(){StopCoach();if(walkthrough!=null && !walkthrough.IsDisposed){walkthrough.Activate();return;}walkthrough=new DeskPagesNavigationTour(FinishTour);walkthrough.Show(this);}
    private Page Selected(){if(pages.SelectedIndex<0 || pages.SelectedIndex>=rows.Count)throw new InvalidOperationException(L("Select a page.","اختر صفحة.","בחרו דף."));return rows[pages.SelectedIndex];}
    private void Safe(Action action){try{action();}catch(Exception ex){DeskPagesStartupGate.Log("pages: "+ex.ToString());DeskPagesHost.ShowError(ex.Message);}}
    private void Go(){Safe(delegate{DeskPagesWorkspaceApi.Execute("open",Selected().Id.ToString());Hide();});}
    private void BuildMenu(){
        menu.Items.Clear();if(pages.SelectedIndex>=0){var p=Selected();menu.Items.Add(L("Rename…","تغيير الاسم…","שינוי שם…"),null,delegate{Safe(delegate{Rename(p);});});menu.Items.Add(p.Locked?L("Unlock icon positions","فك قفل ترتيب الأيقونات","שחרור מיקומי סמלים"):L("Lock icon positions","قفل ترتيب الأيقونات","נעילת מיקומי סמלים"),null,delegate{Safe(delegate{DeskPagesWorkspaceApi.Execute(p.Locked?"unlock":"lock",p.Id.ToString());RefreshData();});});menu.Items.Add(L("Delete empty page…","حذف الصفحة الفارغة…","מחיקת דף ריק…"),null,delegate{Safe(delegate{if(MessageBox.Show(this,L("Delete this empty page?","حذف هذه الصفحة الفارغة؟","למחוק דף ריק זה?"),"DeskPages",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes){DeskPagesWorkspaceApi.Execute("delete",p.Id.ToString());RefreshData();}});});}
        var archived=DeskPagesWorkspaceApi.Execute("pages");ToolStripMenuItem restore=null;foreach(var row in archived){var f=row.Split('\t');if(f.Length==6 && f[3]=="True"){if(restore==null){restore=new ToolStripMenuItem(L("Archived pages","الصفحات المؤرشفة","דפים בארכיון"));menu.Items.Add(restore);}string id=f[0];restore.DropDownItems.Add(f[1],null,delegate{Safe(delegate{DeskPagesWorkspaceApi.Execute("unarchive",id);RefreshData();});});}}
    }
    private void Rename(Page page){using(var dialog=new Form()){DeskPagesFluent.Configure(dialog,L("Page name","اسم الصفحة","שם הדף"),new Size(320,120));var input=new TextBox{Text=page.Name,MaxLength=40};var field=new DeskPagesField(input){Dock=DockStyle.Top};dialog.Controls.Add(field);var save=DeskPagesFluent.Button(L("Save","حفظ","שמירה"),delegate{dialog.DialogResult=DialogResult.OK;},true);save.Dock=DockStyle.Bottom;dialog.Controls.Add(save);dialog.AcceptButton=save;if(dialog.ShowDialog(this)==DialogResult.OK){DeskPagesWorkspaceApi.Execute("rename",page.Id.ToString(),input.Text);RefreshData();}}}
    public void QueueRefresh(){load.Stop();load.Start();}
    public void RefreshData(){Safe(delegate{int id=pages.SelectedIndex>=0 && pages.SelectedIndex<rows.Count?rows[pages.SelectedIndex].Id:-1;rows.Clear();pages.BeginUpdate();try{pages.Items.Clear();foreach(string row in DeskPagesWorkspaceApi.Execute("pages")){var f=row.Split('\t');if(f.Length!=6 || f[3]=="True")continue;var p=new Page{Id=int.Parse(f[0]),Name=f[1],Count=int.Parse(f[2]),Active=f[5]=="True",Locked=f[4]=="True"};rows.Add(p);pages.Items.Add(p);if(p.Id==id || (id<0 && p.Active))pages.SelectedIndex=rows.Count-1;}}finally{pages.EndUpdate();}if(!sized){sized=true;int previous=Height;ClientSize=new Size(400,Math.Max(300,Math.Min(440,160+rows.Count*70)));Location=new Point(Location.X,Location.Y+previous-Height);}});}
    private void DrawPage(object sender,DrawItemEventArgs e){
        if(e.Index<0 || e.Index>=rows.Count)return;var p=rows[e.Index];bool selected=(e.State&DrawItemState.Selected)!=0;bool rtl=RightToLeft==RightToLeft.Yes;e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(var bg=new SolidBrush(BackColor))e.Graphics.FillRectangle(bg,e.Bounds);var r=Rectangle.Inflate(e.Bounds,-2,-3);using(var path=DeskPagesTheme.Round(r,5))using(var fill=new SolidBrush(selected?Color.FromArgb(233,243,251):Color.White))using(var border=new Pen(selected?DeskPagesTheme.Accent:DeskPagesTheme.Line)){e.Graphics.FillPath(fill,path);e.Graphics.DrawPath(border,path);}
        int x=rtl?r.Right-48:r.Left+12;var badge=new Rectangle(x,r.Top+16,30,30);using(var pen=new Pen(DeskPagesTheme.Muted))e.Graphics.DrawRectangle(pen,badge.Left+5,badge.Top+3,19,22);TextRenderer.DrawText(e.Graphics,p.Id.ToString(),Font,badge,DeskPagesTheme.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        var text=new Rectangle(rtl?r.Left+12:r.Left+54,r.Top+9,r.Width-68,22);var flags=TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter|(rtl?TextFormatFlags.Right|TextFormatFlags.RightToLeft:TextFormatFlags.Left);TextRenderer.DrawText(e.Graphics,p.Name,Font,text,DeskPagesTheme.Ink,flags);text.Y+=24;string status=p.Active?L("Current page","الصفحة الحالية","הדף הנוכחי"):p.Count+" "+L("items","عنصر","פריטים");if(p.Locked)status+=" · "+L("Locked","مقفلة","נעול");TextRenderer.DrawText(e.Graphics,status,Font,text,p.Active?DeskPagesTheme.Accent:DeskPagesTheme.Muted,flags);if((e.State&DrawItemState.Focus)!=0){r.Inflate(-3,-3);ControlPaint.DrawFocusRectangle(e.Graphics,r);}
    }
    public void OpenNearPointer(){var area=Screen.FromPoint(Cursor.Position).WorkingArea;Location=new Point(Math.Max(area.Left,Math.Min(Cursor.Position.X-Width+10,area.Right-Width)),Math.Max(area.Top,Math.Min(Cursor.Position.Y-Height-12,area.Bottom-Height)));Show();Activate();}
    protected override void Dispose(bool disposing){if(disposing){StopCoach();if(walkthrough!=null)walkthrough.Dispose();load.Dispose();menu.Dispose();}base.Dispose(disposing);}
}
