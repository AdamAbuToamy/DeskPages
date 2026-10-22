using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

public sealed class DeskPagesMonitorPages:Form {
    private readonly CheckBox enabled=new CheckBox{AutoSize=true};
    private readonly FlowLayoutPanel rows=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(16)};
    private readonly Label status=new Label{Dock=DockStyle.Bottom,Height=56,Padding=new Padding(16,6,16,6)};
    private readonly Dictionary<string,ComboBox> choices=new Dictionary<string,ComboBox>();
    private sealed class Choice {public int Id;public string Name;public override string ToString(){return Name;}}
    private static string L(string en,string ar,string he){return DeskPagesWorkspaceApi.L(en,ar,he);}
    public DeskPagesMonitorPages(){
        Text=L("Pages on other displays","صفحات الشاشات الإضافية","דפים במסכים נוספים");ShowInTaskbar=false;ShowIcon=false;StartPosition=FormStartPosition.CenterParent;MinimumSize=new Size(480,360);ClientSize=new Size(580,440);AutoScaleMode=AutoScaleMode.Dpi;BackColor=DeskPagesModernStyle.Background;
        enabled.Text=L("Show a fixed page on each assigned display","عرض صفحة ثابتة على كل شاشة مخصصة","הצגת דף קבוע בכל מסך שהוקצה");enabled.Dock=DockStyle.Top;enabled.Padding=new Padding(16);enabled.Height=56;
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=54,Padding=new Padding(12),FlowDirection=FlowDirection.RightToLeft};
        buttons.Controls.Add(DeskPagesFluent.Button(L("Save","حفظ","שמירה"),Save,true));buttons.Controls.Add(DeskPagesFluent.Button(L("Refresh displays","تحديث الشاشات","רענון מסכים"),RefreshDisplays,false));
        rows.Resize+=delegate{foreach(Control control in rows.Controls){var combo=control as ComboBox;if(combo!=null)combo.Width=Math.Max(120,Math.Min(450,rows.ClientSize.Width-40));var label=control as Label;if(label!=null)label.MaximumSize=new Size(Math.Max(120,rows.ClientSize.Width-40),0);}};
        Controls.Add(rows);Controls.Add(enabled);Controls.Add(status);Controls.Add(buttons);Shown+=delegate{RefreshDisplays();};
    }
    protected override void OnShown(EventArgs e){base.OnShown(e);var area=Screen.FromControl(this).WorkingArea;MinimumSize=new Size(Math.Min(MinimumSize.Width,area.Width-48),Math.Min(MinimumSize.Height,area.Height-64));Size=new Size(Math.Min(Width,area.Width-48),Math.Min(Height,area.Height-64));Location=new Point(Math.Max(area.Left+16,Math.Min(Left,area.Right-Width-16)),Math.Max(area.Top+16,Math.Min(Top,area.Bottom-Height-16)));}
    private void RefreshDisplays(){try{
        var settings=DeskPagesWorkspaceApi.Execute("monitor-pages-settings");enabled.Checked=bool.Parse(settings[0]);choices.Clear();foreach(Control old in rows.Controls.Cast<Control>().ToArray()){rows.Controls.Remove(old);old.Dispose();}
        var pages=DeskPagesWorkspaceApi.Execute("pages").Select(v=>v.Split('\t')).Where(f=>f.Length==6 && f[3]!="True").ToArray();
        int number=2;
        foreach(string line in settings.Skip(1)){
            var f=line.Split('\t');if(f.Length!=4)continue;
            var label=new Label{AutoSize=true,MaximumSize=new Size(490,0),Text=L("Display ","الشاشة ","מסך ")+(number++)+" · "+f[1]+" · "+f[3],Margin=new Padding(0,12,0,6)};
            var combo=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=Math.Max(120,Math.Min(450,rows.ClientSize.Width-40)),Margin=new Padding(0,0,0,12)};
            combo.Items.Add(new Choice{Id=0,Name=L("No assigned page","دون صفحة مخصصة","ללא דף מוקצה")});
            foreach(var page in pages)combo.Items.Add(new Choice{Id=int.Parse(page[0]),Name=page[1]});
            combo.SelectedIndex=0;for(int i=0;i<combo.Items.Count;i++)if(((Choice)combo.Items[i]).Id==int.Parse(f[2]))combo.SelectedIndex=i;
            choices.Add(f[0],combo);rows.Controls.Add(label);rows.Controls.Add(combo);
        }
        status.Text=choices.Count==0?L("Connect another display, then refresh. Saved assignments return when it reconnects.","اربط شاشة إضافية ثم اضغط تحديث. تعود اختياراتك عند إعادة توصيلها.","חברו מסך נוסף ורעננו. ההקצאות נשמרות לחיבור הבא."):L("Main-display navigation uses the remaining pages. Keep at least one page for it.","التنقل على الشاشة الرئيسية يستخدم بقية الصفحات. اترك صفحة واحدة لها على الأقل.","הניווט במסך הראשי משתמש בדפים שנותרו. השאירו עבורו לפחות דף אחד.");
    }catch(Exception ex){status.Text=ex.Message;}}
    private void Save(){try{
        var selected=choices.Select(c=>new{Monitor=c.Key,Page=((Choice)c.Value.SelectedItem).Id}).ToArray();
        if(selected.Where(c=>c.Page!=0).GroupBy(c=>c.Page).Any(g=>g.Count()>1))throw new InvalidOperationException(L("Choose a different page for each display.","اختر صفحة مختلفة لكل شاشة.","בחרו דף שונה לכל מסך."));
        var args=new[]{enabled.Checked.ToString()}.Concat(selected.Select(c=>c.Monitor+"\t"+c.Page)).ToArray();
        DeskPagesWorkspaceApi.Execute("monitor-pages-save",args);DialogResult=DialogResult.OK;Close();
    }catch(Exception ex){status.Text=ex.Message;}}
}
