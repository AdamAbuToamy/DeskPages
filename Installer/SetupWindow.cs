using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal sealed class SetupWindow:Form {
    private static readonly Color Ink=Color.FromArgb(24,35,55),Muted=Color.FromArgb(100,111,130),Accent=Color.FromArgb(33,101,222);
    private readonly Label heading,description,folderLabel,note,status,pathSummary;
    private readonly TextBox location;
    private readonly TableLayoutPanel field;
    private readonly Button browse,primary,secondary,openLog;
    private readonly ProgressBar progress;
    private readonly PictureBox successMark;
    private readonly string sid;
    private readonly Action<string,string,Action<string>,CancellationToken> install;
    private bool running,finished,committing,closeWhenFinished;
    private CancellationTokenSource cancellation;
    public bool InstallFailed{get;private set;}
    private readonly Image brand;
    public SetupWindow(string account,Action<string,string,Action<string>,CancellationToken> installer){
        sid=account;install=installer;
        SuspendLayout();Text="DeskPages Setup";ClientSize=new Size(880,600);MinimumSize=new Size(720,490);
        StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.Sizable;MaximizeBox=true;
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;Font=new Font("Segoe UI",10);BackColor=Color.White;
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);brand=LoadBrand();
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty,Padding=Padding.Empty};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,238));root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(root);
        var side=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,BackColor=Color.FromArgb(16,28,49),Padding=new Padding(28),Margin=Padding.Empty};
        side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<7;i++)side.RowStyles.Add(new RowStyle(i==3?SizeType.Percent:SizeType.AutoSize,i==3?100:0));
        side.Controls.Add(new PictureBox{Size=new Size(72,72),Image=brand,SizeMode=PictureBoxSizeMode.Zoom,Margin=new Padding(0,0,0,18)},0,0);
        var brandTitle=new SingleLineWordmark{Text="DeskPages",Name="DeskPagesWordmark",Font=new Font("Segoe UI",25,FontStyle.Bold),ForeColor=Color.White,BackColor=Color.Transparent,Anchor=AnchorStyles.Left|AnchorStyles.Right,Margin=new Padding(0,0,0,10)};side.Controls.Add(brandTitle,0,1);
        var tagline=TextLabel("More room.\r\nSame desktop.",18,Color.FromArgb(190,211,244),FontStyle.Regular);tagline.Margin=new Padding(0,20,0,12);side.Controls.Add(tagline,0,2);
        var creatorTitle=TextLabel("CREATED BY",9,Color.FromArgb(143,163,194),FontStyle.Bold);side.Controls.Add(creatorTitle,0,4);
        var creator=TextLabel("Adam Abu Toamy",11,Color.White,FontStyle.Bold);side.Controls.Add(creator,0,5);
        var version=TextLabel("Version 0.8.29",9,Color.FromArgb(143,163,194),FontStyle.Regular);version.Margin=new Padding(0,12,0,0);side.Controls.Add(version,0,6);
        side.SizeChanged+=delegate {int width=Math.Max(100,side.ClientSize.Width-side.Padding.Horizontal);foreach(Control child in side.Controls)if(child is Label)child.MaximumSize=new Size(width,0);};
        root.Controls.Add(side,0,0);
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty,Padding=Padding.Empty};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.Controls.Add(body,1,0);
        var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true,Margin=Padding.Empty};
        var flow=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(32,30,32,24),Margin=Padding.Empty};scroll.Controls.Add(flow);body.Controls.Add(scroll,0,0);
        var eyebrow=TextLabel("WELCOME TO DESKPAGES",9,Accent,FontStyle.Bold);eyebrow.Margin=new Padding(0,0,0,20);
        heading=TextLabel("Install DeskPages",23,Ink,FontStyle.Bold);heading.Margin=new Padding(0,0,0,16);
        description=TextLabel("Make space for your files with desktop pages.\r\nYour saved pages stay with your Windows account.",10,Muted,FontStyle.Regular);description.Margin=new Padding(0,0,0,24);
        folderLabel=TextLabel("Installation folder",10,Ink,FontStyle.Bold);folderLabel.Margin=new Padding(0,0,0,10);
        field=new TableLayoutPanel{AutoSize=false,Height=48,ColumnCount=2,RowCount=1,Margin=new Padding(0,0,0,14),Padding=Padding.Empty};field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));field.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));field.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        location=new TextBox{Anchor=AnchorStyles.Left|AnchorStyles.Right,Margin=new Padding(0,4,10,4),BorderStyle=BorderStyle.FixedSingle,Font=new Font("Segoe UI",10)};
        string defaultPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"DeskPages-"+sid);
        using(var key=Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DeskPages-"+sid)){if(key!=null)defaultPath=(string)key.GetValue("InstallLocation",defaultPath);}location.Text=defaultPath;
        browse=ButtonStyle("Browse…",false);field.Controls.Add(location,0,0);field.Controls.Add(browse,1,0);
        browse.Click+=delegate {using(var picker=new FolderBrowserDialog{Description="Choose the DeskPages installation folder",SelectedPath=location.Text}){if(picker.ShowDialog(this)==DialogResult.OK)location.Text=picker.SelectedPath;}};
        note=TextLabel("Starts automatically when you sign in",9,Muted,FontStyle.Regular);note.Margin=new Padding(0,0,0,20);
        progress=new ProgressBar{Height=8,Style=ProgressBarStyle.Marquee,MarqueeAnimationSpeed=28,Visible=false,Margin=new Padding(0,8,0,16)};
        status=TextLabel("",10,Muted,FontStyle.Regular);
        pathSummary=TextLabel("",9,Muted,FontStyle.Regular);pathSummary.Visible=false;
        successMark=new PictureBox{Width=52,Height=52,Image=DrawSuccess(),Visible=false,SizeMode=PictureBoxSizeMode.Zoom,Margin=new Padding(0,0,0,20)};
        openLog=ButtonStyle("Open setup log",false);openLog.Visible=false;
        openLog.Click+=delegate {System.Diagnostics.Process.Start("notepad.exe","\""+Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPages","setup.log")+"\"");};
        var legal=new FlowLayoutPanel{AutoSize=true,FlowDirection=FlowDirection.LeftToRight,WrapContents=false,Margin=new Padding(0,0,0,16)};
        foreach(string file in new[]{"PRIVACY.txt","LICENSE.txt"}){
            string document=file;
            var link=new LinkLabel{Text=file=="PRIVACY.txt"?"Privacy":"License",AutoSize=true,Margin=new Padding(0,0,18,0),LinkColor=Accent};
            link.LinkClicked+=delegate {ShowDocument(document);};legal.Controls.Add(link);
        }
        flow.Controls.AddRange(new Control[]{eyebrow,heading,description,folderLabel,field,note,legal,successMark,progress,status,pathSummary,openLog});
        scroll.SizeChanged+=delegate {
            int width=Math.Max(180,scroll.ClientSize.Width-flow.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth);
            foreach(Control child in flow.Controls){if(child is Label)child.MaximumSize=new Size(width,0);else if(child==field || child==progress)child.Width=width;}
            field.Height=field.GetPreferredSize(new Size(width,0)).Height;
            foreach(Control child in flow.Controls){if(child==field || child==progress)child.Width=width;}
        };
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.RightToLeft,WrapContents=true,Padding=new Padding(18),Margin=Padding.Empty,BackColor=Color.FromArgb(249,250,252)};
        primary=ButtonStyle("Install DeskPages",true);secondary=ButtonStyle("Cancel",false);
        primary.Click+=delegate {if(finished){Close();return;}BeginInstall();};secondary.Click+=delegate{if(running)RequestCancel();else Close();};footer.Controls.AddRange(new Control[]{primary,secondary});body.Controls.Add(footer,0,1);
        FormClosing+=delegate(object sender,FormClosingEventArgs e){if(running){e.Cancel=true;RequestCancel();}};AcceptButton=primary;CancelButton=secondary;
        Shown+=delegate {Rectangle area=Screen.FromControl(this).WorkingArea;MinimumSize=new Size(Math.Min(MinimumSize.Width,area.Width-20),Math.Min(MinimumSize.Height,area.Height-20));if(Width>area.Width-20 || Height>area.Height-20){Bounds=new Rectangle(area.Left+10,area.Top+10,Math.Min(Width,area.Width-20),Math.Min(Height,area.Height-20));}};
        ResumeLayout(true);
    }
    private void ShowDocument(string file){
        try{using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("DeskPages.Payload"))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read))using(var reader=new StreamReader(zip.GetEntry(file).Open()))
        using(var dialog=new Form{Text=file=="PRIVACY.txt"?"Privacy":"License",Size=new Size(560,480),StartPosition=FormStartPosition.CenterParent,MinimizeBox=false,MaximizeBox=false,Font=Font}){
            dialog.Controls.Add(new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,Text=reader.ReadToEnd(),BackColor=Color.White,Margin=new Padding(16)});dialog.ShowDialog(this);
        }}catch(Exception ex){MessageBox.Show(this,ex.Message,"DeskPages");}
    }
    private static Label TextLabel(string text,float size,Color color,FontStyle style){return new Label{Text=text,AutoSize=true,AutoEllipsis=false,UseCompatibleTextRendering=false,ForeColor=color,BackColor=Color.Transparent,Font=new Font("Segoe UI",size,style),Margin=new Padding(0,0,0,10)};}
    private static Button ButtonStyle(string text,bool accent){
        var b=new Button{Text=text,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,MinimumSize=new Size(108,38),Padding=new Padding(12,7,12,7),Margin=new Padding(6,0,0,0),FlatStyle=FlatStyle.Flat,BackColor=accent?Accent:Color.White,ForeColor=accent?Color.White:Ink,Cursor=Cursors.Hand,Font=new Font("Segoe UI",9,FontStyle.Bold),UseVisualStyleBackColor=false};
        b.FlatAppearance.BorderColor=accent?Accent:Color.FromArgb(215,222,233);b.FlatAppearance.BorderSize=1;return b;
    }
    private static Image LoadBrand(){using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("DeskPages.Payload"))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read))using(var input=zip.GetEntry("Assets/DeskPages.ico").Open())using(var memory=new MemoryStream()){input.CopyTo(memory);memory.Position=0;using(var icon=new Icon(memory,new Size(128,128)))return icon.ToBitmap();}}
    private static Image DrawSuccess(){var image=new Bitmap(104,104);using(var g=Graphics.FromImage(image)){g.SmoothingMode=SmoothingMode.AntiAlias;using(var b=new SolidBrush(Color.FromArgb(227,247,239)))g.FillEllipse(b,2,2,100,100);using(var p=new Pen(Color.FromArgb(25,145,99),8)){p.StartCap=p.EndCap=LineCap.Round;g.DrawLines(p,new[]{new Point(28,53),new Point(45,70),new Point(77,35)});}}return image;}
    private void BeginInstall(){
        string chosen;try{chosen=DeskPagesInstallLocation.Resolve(location.Text);}catch(Exception ex){MessageBox.Show(this,ex.Message,"Choose an installation folder",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
        location.Text=chosen;InstallFailed=false;running=true;committing=false;closeWhenFinished=false;cancellation=new CancellationTokenSource();primary.Visible=false;AcceptButton=null;secondary.Enabled=true;secondary.Text="Cancel";field.Visible=false;folderLabel.Visible=false;note.Visible=false;openLog.Visible=false;
        heading.Text="Installing DeskPages";description.Text="Setting up your desktop pages.\r\nThis window will confirm when DeskPages is ready.";status.Text="Preparing installation…";progress.Visible=true;
        new Thread(delegate(){string error=null;bool cancelled=false;try{install(sid,chosen,delegate(string message){BeginInvoke((Action)delegate{if(message=="@commit"){committing=true;secondary.Text="Close when finished";}else status.Text=message;});},cancellation.Token);}catch(OperationCanceledException){cancelled=true;}catch(Exception ex){error=ex.Message;}BeginInvoke((Action)delegate{CompleteInstall(error,chosen,cancelled);});}){IsBackground=true}.Start();
    }
    private void RequestCancel(){
        if(!running || !secondary.Enabled)return;
        if(committing){closeWhenFinished=true;secondary.Text="Closing when finished…";status.Text="Finishing the installation safely before closing.";}
        else{cancellation.Cancel();secondary.Text="Cancelling…";status.Text="Stopping at the next safe point…";closeWhenFinished=true;}
        secondary.Enabled=false;
    }
    private void CompleteInstall(string error,string chosen,bool cancelled){
        running=false;progress.Visible=false;primary.Visible=true;AcceptButton=primary;primary.Enabled=true;secondary.Enabled=true;cancellation.Dispose();cancellation=null;
        if(cancelled){heading.Text="Installation cancelled";description.Text="Setup stopped before changing the installed app.";status.Text="";primary.Text="Try again";secondary.Text="Close";field.Visible=true;folderLabel.Visible=true;if(closeWhenFinished)Close();return;}
        if(error!=null){InstallFailed=true;heading.Text="Installation stopped";description.Text="DeskPages did not finish setting up.\r\nYou can review the log and try again.";field.Visible=true;folderLabel.Visible=true;openLog.Visible=true;status.Text=error;primary.Text="Try again";secondary.Text="Close";return;}
        finished=true;heading.Text="DeskPages is ready";description.Text="Installation completed successfully.\r\nDeskPages is running in the notification area.";successMark.Visible=true;pathSummary.Text="Installed in: "+chosen;pathSummary.Visible=true;status.Text="";primary.Text="Finish";secondary.Visible=false;if(closeWhenFinished)Close();
    }
    protected override void Dispose(bool disposing){if(disposing){if(brand!=null)brand.Dispose();if(successMark!=null && successMark.Image!=null)successMark.Image.Dispose();}base.Dispose(disposing);}
}

// Brand names must never wrap at a letter when Windows scales the sidebar.
internal sealed class SingleLineWordmark:Control {
    private const TextFormatFlags Flags=TextFormatFlags.SingleLine|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter|TextFormatFlags.Left;
    public SingleLineWordmark(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);Height=56;}
    internal Font FitFont(Graphics graphics){
        float size=Font.SizeInPoints;
        for(;;){
            var candidate=new Font(Font.FontFamily,size,Font.Style,GraphicsUnit.Point);
            var measured=TextRenderer.MeasureText(graphics,Text,candidate,new Size(int.MaxValue,int.MaxValue),Flags);
            if((measured.Width<=Math.Max(1,ClientSize.Width) && measured.Height<=Math.Max(1,ClientSize.Height)) || size<=1f)return candidate;
            candidate.Dispose();size=Math.Max(1f,size-0.25f);
        }
    }
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Invalidate();}
    protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(ClientSize.Width<1 || ClientSize.Height<1)return;using(var font=FitFont(e.Graphics))TextRenderer.DrawText(e.Graphics,Text,font,ClientRectangle,ForeColor,Flags);}
}
