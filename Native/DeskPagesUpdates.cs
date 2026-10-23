using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public sealed class DeskPagesUpdateRelease {
    public Version Version;public string Page,Installer;
}
public sealed class DeskPagesUpdates:IDisposable {
    public const string Repository="https://github.com/AdamAbuToamy/DeskPages";
    public static readonly Version Current=new Version("0.8.33");
    private readonly Control owner=new Control();private readonly Action<DeskPagesUpdateRelease> announce;
    private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    private DateTime next=DateTime.UtcNow.AddSeconds(60);private int busy;private volatile bool disposed;private string lastAnnounced;private Form window;
    private readonly string preferences=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPages","update-settings.txt");
    public bool Automatic {get;private set;}
    public DeskPagesUpdateRelease Available {get;private set;}
    public DeskPagesUpdates(Action<DeskPagesUpdateRelease> announce){
        IntPtr handle=owner.Handle;this.announce=announce;Automatic=true;
        try{if(File.Exists(preferences))Automatic=File.ReadAllText(preferences).Trim()!="off";}catch{}
        timer.Interval=30000;timer.Tick+=delegate{if(Automatic && DateTime.UtcNow>=next){next=DateTime.UtcNow.AddHours(6);Check(false,null);}};timer.Start();
    }
    public void SetAutomatic(bool value){Directory.CreateDirectory(Path.GetDirectoryName(preferences));string temp=preferences+".tmp";File.WriteAllText(temp,value?"on":"off");if(File.Exists(preferences))File.Replace(temp,preferences,null);else File.Move(temp,preferences);Automatic=value;}
    public static DeskPagesUpdateRelease Select(string json,Version current){
        var serializer=new JavaScriptSerializer{MaxJsonLength=2097152};var releases=serializer.DeserializeObject(json) as object[];
        if(releases==null)throw new InvalidDataException("Invalid release response.");DeskPagesUpdateRelease best=null;
        foreach(object raw in releases){var r=raw as Dictionary<string,object>;if(r==null || Flag(r,"draft"))continue;
            string tag=Value(r,"tag_name");var match=Regex.Match(tag,@"^v([0-9]+\.[0-9]+\.[0-9]+)(?:-alpha\.[0-9]+)?$");Version version;
            if(!match.Success || !Version.TryParse(match.Groups[1].Value,out version) || version<=current || (best!=null && version<=best.Version))continue;
            string page=Repository+"/releases/tag/"+tag;if(Value(r,"html_url")!=page)continue;
            var assets=r.ContainsKey("assets")?r["assets"] as object[]:null;if(assets==null)continue;
            string name="DeskPages-Setup-"+version+".exe",url=Repository+"/releases/download/"+tag+"/"+name;
            foreach(object asset in assets){var a=asset as Dictionary<string,object>;if(a!=null && Value(a,"name")==name && Value(a,"state")=="uploaded" && Value(a,"browser_download_url")==url){best=new DeskPagesUpdateRelease{Version=version,Page=page,Installer=url};break;}}
        }return best;
    }
    private static string Value(Dictionary<string,object> d,string key){object value;return d.TryGetValue(key,out value)?Convert.ToString(value):"";}
    private static bool Flag(Dictionary<string,object> d,string key){object value;return d.TryGetValue(key,out value) && value is bool && (bool)value;}
    private static string Fetch(){
        ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
        var request=(HttpWebRequest)WebRequest.Create("https://api.github.com/repos/AdamAbuToamy/DeskPages/releases?per_page=20");
        request.UserAgent="DeskPages/"+Current;request.Accept="application/vnd.github+json";request.Timeout=15000;request.ReadWriteTimeout=15000;request.AllowAutoRedirect=false;
        using(var response=(HttpWebResponse)request.GetResponse()){
            if(response.StatusCode!=HttpStatusCode.OK)throw new IOException("GitHub release check failed.");
            using(var input=response.GetResponseStream())using(var output=new MemoryStream()){
                byte[] buffer=new byte[8192];int read;while((read=input.Read(buffer,0,buffer.Length))>0){if(output.Length+read>2097152)throw new InvalidDataException("Release response too large.");output.Write(buffer,0,read);}return Encoding.UTF8.GetString(output.ToArray());
            }
        }
    }
    public void Check(bool manual,Action<string> result){
        if(Interlocked.CompareExchange(ref busy,1,0)!=0){if(result!=null)result(L("A check is already running.","الفحص جارٍ بالفعل.","הבדיקה כבר פועלת."));return;}
        ThreadPool.QueueUserWorkItem(delegate{DeskPagesUpdateRelease found=null;string error=null;try{found=Select(Fetch(),Current);}catch{error=L("Couldn't check. Try again when you're online.","تعذر الفحص. حاول مجددًا عند الاتصال بالإنترنت.","לא ניתן לבדוק. נסו שוב כשיש חיבור.");}
            Interlocked.Exchange(ref busy,0);if(disposed)return;try{owner.BeginInvoke((Action)delegate{if(disposed)return;Available=found;if(result!=null)result(error??(found==null?L("You have the latest available version.","لديك أحدث إصدار متاح.","הגרסה העדכנית כבר מותקנת."):L("Update available: ","يتوفر تحديث: ","עדכון זמין: ")+found.Version));if(found!=null && !manual && announce!=null && lastAnnounced!=found.Version.ToString()){lastAnnounced=found.Version.ToString();announce(found);}});}catch(InvalidOperationException){}
        });
    }
    public void OpenAvailable(){if(Available!=null)Process.Start(new ProcessStartInfo(Available.Page){UseShellExecute=true});}
    private static string L(string en,string ar,string he){return DeskPagesWorkspaceApi.L(en,ar,he);}
    public void Show(){
        if(window!=null && !window.IsDisposed){window.Show();window.Activate();return;}
        var form=new Form();window=form;DeskPagesFluent.Configure(form,L("DeskPages updates","تحديثات DeskPages","עדכוני DeskPages"),new System.Drawing.Size(540,300));
        var layout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};form.Controls.Add(layout);
        layout.Controls.Add(new Label{Text=L("Installed version: ","الإصدار المثبت: ","גרסה מותקנת: ")+Current,AutoSize=true,Margin=new Padding(0,0,0,16)});
        var automatic=new CheckBox{Text=L("Check automatically every 6 hours","فحص تلقائي كل 6 ساعات","בדיקה אוטומטית כל 6 שעות"),Checked=Automatic,AutoSize=true,Margin=new Padding(0,0,0,16)};layout.Controls.Add(automatic);
        var status=new Label{AutoSize=true,MaximumSize=new System.Drawing.Size(470,0),Margin=new Padding(0,0,0,16)};layout.Controls.Add(status);
        automatic.CheckedChanged+=delegate{try{SetAutomatic(automatic.Checked);}catch(Exception ex){automatic.Checked=Automatic;status.Text=ex.Message;}};
        var open=DeskPagesFluent.Button(L("Open download page","فتح صفحة التحميل","פתיחת דף ההורדה"),OpenAvailable,true);open.Visible=Available!=null;
        Action check=delegate{status.Text=L("Checking GitHub…","جارٍ فحص GitHub…","בודק ב-GitHub…");Check(true,delegate(string text){if(form.IsDisposed)return;status.Text=text;open.Visible=Available!=null;});};
        layout.Controls.Add(DeskPagesFluent.Button(L("Check now","فحص الآن","בדיקה כעת"),check,false));layout.Controls.Add(open);
        layout.Controls.Add(new Label{Text=L("Updates install only when you choose. Saved pages are preserved.","تثبيت التحديث باختيارك. تبقى صفحاتك المحفوظة.","עדכונים מותקנים רק לבחירתכם. הדפים השמורים נשמרים."),AutoSize=true,MaximumSize=new System.Drawing.Size(470,0)});
        form.Shown+=delegate{check();};form.Show();
    }
    public void Dispose(){disposed=true;timer.Stop();timer.Dispose();if(window!=null)window.Dispose();owner.Dispose();}
}
