using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

internal sealed class GroupCreateSignal:IDisposable {
    private readonly EventWaitHandle signal;
    public GroupCreateSignal(string name=null){signal=new EventWaitHandle(false,EventResetMode.AutoReset,name??("Local\\DeskPagesTaskbarGroupsAdd-"+WindowsIdentity.GetCurrent().User.Value));}
    public void Request(){signal.Set();}
    public bool Take(){return signal.WaitOne(0);}
    public void Dispose(){signal.Dispose();}
}
// Message dispatch and popup ownership only; this window can never be shown.
internal sealed class GroupCommandWindow:Form {
    private readonly Action add;private IntPtr hotkeyHandle;
    private readonly HashSet<int> registered=new HashSet<int>();
    internal int RegistrationCount{get{return registered.Count;}}
    public GroupCommandWindow(Action create){add=create;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;FormBorderStyle=FormBorderStyle.None;Bounds=new Rectangle(-32000,-32000,1,1);Text="DeskPages group commands";}
    protected override void SetVisibleCore(bool value){base.SetVisibleCore(false);}
    protected override void OnHandleCreated(EventArgs e){
        base.OnHandleCreated(e);hotkeyHandle=Handle;
        // Win+Plus belongs to Magnifier. Add Alt; support the main keyboard
        // plus/equal key (with or without Shift) and numeric keypad Plus.
        uint[] keys={0xBB,0xBB,0x6B,0x6B};
        for(int i=0;i<keys.Length;i++)if(RegisterHotKey(hotkeyHandle,1000+i,0x4009U+(i%2==1?4U:0U),keys[i]))registered.Add(1000+i);
    }
    protected override void WndProc(ref Message message){
        if(message.Msg==0x312 && registered.Contains(message.WParam.ToInt32())){BeginInvoke((Action)delegate{if(!IsDisposed)add();});return;}
        base.WndProc(ref message);
    }
    protected override void OnHandleDestroyed(EventArgs e){foreach(int id in registered)UnregisterHotKey(hotkeyHandle,id);registered.Clear();hotkeyHandle=IntPtr.Zero;base.OnHandleDestroyed(e);}
    [DllImport("user32.dll")]private static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")]private static extern bool UnregisterHotKey(IntPtr hwnd,int id);
}
