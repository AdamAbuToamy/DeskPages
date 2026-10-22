using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

internal static class WindowShell {
    internal static readonly Guid AppKeys=new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    [StructLayout(LayoutKind.Sequential)]internal struct Key {public Guid Format;public uint Id;public Key(uint id){Format=AppKeys;Id=id;}}
    // PROPVARIANT is 24 bytes on x64. Only VT_EMPTY/VT_LPWSTR are accepted.
    [StructLayout(LayoutKind.Explicit,Size=24)]internal struct Value {
        [FieldOffset(0)]public ushort Kind;[FieldOffset(8)]public IntPtr Pointer;
        public static Value Text(string text){return text==null?new Value():new Value{Kind=31,Pointer=Marshal.StringToCoTaskMemUni(text)};}
    }
    [ComImport,Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]internal interface IStore {
        void GetCount(out uint count);void GetAt(uint index,out Key key);void GetValue(ref Key key,out Value value);void SetValue(ref Key key,ref Value value);void Commit();
    }
    public static string Get(IntPtr hwnd,uint id){
        IStore store;Guid iid=typeof(IStore).GUID;Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd,ref iid,out store));
        try{return ReadStore(store,id);}
        finally{Marshal.ReleaseComObject(store);}
    }
    internal static string ReadStore(IStore store,uint id){var key=new Key(id);Value value;store.GetValue(ref key,out value);try{if(value.Kind==0)return null;if(value.Kind!=31)throw new InvalidOperationException("Unexpected Shell property type");return Marshal.PtrToStringUni(value.Pointer);}finally{PropVariantClear(ref value);}}
    public static void Set(IntPtr hwnd,uint id,string text){
        IStore store;Guid iid=typeof(IStore).GUID;Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd,ref iid,out store));
        try{var key=new Key(id);var value=Value.Text(text);try{store.SetValue(ref key,ref value);}finally{PropVariantClear(ref value);}}finally{Marshal.ReleaseComObject(store);}
    }
    public static GroupWindow Identity(IntPtr hwnd){
        uint pid;GetWindowThreadProcessId(hwnd,out pid);if(pid==0 || !IsWindow(hwnd))return null;
        try{using(var p=Process.GetProcessById((int)pid)){
            var text=new StringBuilder(2048);GetWindowTextW(hwnd,text,text.Capacity);
            var window=new GroupWindow{Handle=hwnd.ToInt64(),ProcessId=(int)pid,Started=p.StartTime.ToUniversalTime().Ticks,Title=text.ToString(),ImagePath=ProcessPath(pid)};
            try{window.AppId=Get(hwnd,5);}catch(COMException){}catch(InvalidOperationException){}
            if(string.IsNullOrEmpty(window.AppId))window.AppId=ProcessAppId(pid);return window;
        }}
        catch(ArgumentException){return null;}catch(System.ComponentModel.Win32Exception){return null;}catch(COMException){return null;}
    }
    private static string ProcessAppId(uint pid){var process=OpenProcess(0x1000,false,pid);if(process==IntPtr.Zero)return null;try{uint length=0;GetApplicationUserModelId(process,ref length,null);if(length==0 || length>32768)return null;var s=new StringBuilder((int)length);return GetApplicationUserModelId(process,ref length,s)==0?s.ToString():null;}finally{CloseHandle(process);}}
    private static string ProcessPath(uint pid){var process=OpenProcess(0x1000,false,pid);if(process==IntPtr.Zero)return null;try{int length=32768;var s=new StringBuilder(length);return QueryFullProcessImageNameW(process,0,s,ref length)?s.ToString():null;}finally{CloseHandle(process);}}
    public static bool OnTaskbar(Point point,IntPtr taskbar){
        if(taskbar==IntPtr.Zero)return false;IntPtr hit=WindowFromPoint(point);
        if(hit==taskbar || IsChild(taskbar,hit) || GetAncestor(hit,2)==taskbar)return true;
        // WinUI can place the content bridge in an owned island rather than a
        // conventional child HWND. Require the real taskbar in its owner chain.
        for(int i=0;i<8 && hit!=IntPtr.Zero;i++){hit=GetWindow(hit,4);if(hit==taskbar)return true;}return false;
    }
    [StructLayout(LayoutKind.Sequential)]private struct GuiThreadInfo {public uint Size,Flags;public IntPtr Active,Focus,Capture,Menu,Move,Caret;public Rect CaretRect;}
    [DllImport("user32.dll")]private static extern bool GetGUIThreadInfo(uint thread,ref GuiThreadInfo info);
    public static bool WaitForDragRelease(IntPtr taskbar){
        uint pid;uint thread=GetWindowThreadProcessId(taskbar,out pid);if(thread==0)return false;
        return System.Threading.SpinWait.SpinUntil(delegate{var info=new GuiThreadInfo{Size=(uint)Marshal.SizeOf(typeof(GuiThreadInfo))};return GetGUIThreadInfo(thread,ref info) && info.Capture==IntPtr.Zero;},500);
    }
    public static bool Same(GroupWindow w){var h=new IntPtr(w.Handle);uint pid;if(!IsWindow(h))return false;GetWindowThreadProcessId(h,out pid);if(pid!=w.ProcessId)return false;try{using(var p=Process.GetProcessById(w.ProcessId))return p.StartTime.ToUniversalTime().Ticks==w.Started;}catch(ArgumentException){return false;}catch(System.ComponentModel.Win32Exception){return true;}}
    public static GroupWindow[] Windows(){
        var list=new List<GroupWindow>();int self=Process.GetCurrentProcess().Id;
        EnumWindows(delegate(IntPtr h,IntPtr unused){
            if(GroupLayoutPolicy.ShellSurface(Class(h)))return true;
            long style=GetWindowLongPtrW(h,-20).ToInt64();if(!IsWindowVisible(h) || (GetWindow(h,4)!=IntPtr.Zero && (style&0x40000)==0) || (style&0x80)!=0)return true;
            uint pid;GetWindowThreadProcessId(h,out pid);if(pid==self)return true;
            int cloaked;DwmGetWindowAttribute(h,14,out cloaked,4);if(cloaked!=0)return true;
            var w=Identity(h);if(w!=null && !string.IsNullOrWhiteSpace(w.Title))list.Add(w);return true;
        },IntPtr.Zero);return list.ToArray();
    }
    public static string Class(IntPtr h){var s=new StringBuilder(256);GetClassNameW(h,s,s.Capacity);return s.ToString();}
    public static Rectangle Bounds(IntPtr hwnd){Rect r;return GetWindowRect(hwnd,out r)?Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom):Rectangle.Empty;}
    public static void Activate(GroupWindow w){if(!Same(w))return;var h=new IntPtr(w.Handle);if(IsIconic(h))ShowWindow(h,9);SetForegroundWindow(h);}
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    private delegate bool EnumProc(IntPtr hwnd,IntPtr parameter);
    [DllImport("shell32.dll")]private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd,ref Guid iid,out IStore store);
    [DllImport("ole32.dll")]private static extern int PropVariantClear(ref Value value);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern int GetApplicationUserModelId(IntPtr process,ref uint length,StringBuilder appId);
    [DllImport("kernel32.dll")]private static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll")]private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern bool QueryFullProcessImageNameW(IntPtr process,uint flags,StringBuilder path,ref int length);
    [DllImport("user32.dll")]private static extern bool EnumWindows(EnumProc callback,IntPtr parameter);
    [DllImport("user32.dll")]private static extern bool IsChild(IntPtr parent,IntPtr child);
    [DllImport("user32.dll")]public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern bool ShowWindow(IntPtr hwnd,int command);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetWindowTextW(IntPtr hwnd,StringBuilder text,int length);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetClassNameW(IntPtr hwnd,StringBuilder text,int length);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd,int index);
    [DllImport("user32.dll")]private static extern IntPtr GetWindow(IntPtr hwnd,uint command);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("dwmapi.dll")]private static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out int value,int size);
    [DllImport("user32.dll")]public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern IntPtr FindWindowW(string cls,string name);
    [DllImport("user32.dll")]public static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern bool SetPropW(IntPtr hwnd,string name,IntPtr value);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern IntPtr GetPropW(IntPtr hwnd,string name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern IntPtr RemovePropW(IntPtr hwnd,string name);
}
