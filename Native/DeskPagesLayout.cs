using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class DeskPagesLayout
{
    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid iid,
            [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IBrowser
    {
        void Slot3(); void Slot4(); void Slot5(); void Slot6();
        void Slot7(); void Slot8(); void Slot9(); void Slot10();
        void Slot11(); void Slot12(); void Slot13(); void Slot14();

        [PreserveSig]
        int QueryActiveShellView(
            [MarshalAs(UnmanagedType.IUnknown)] out object view);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IView
    {
        [PreserveSig] int GetCurrentViewMode(out uint mode);
        void Slot4(); void Slot5();

        [PreserveSig]
        int Item(int index, out IntPtr pidl);

        [PreserveSig]
        int ItemCount(uint flags, out int count);

        void Slot8(); void Slot9();
        [PreserveSig] int GetFocusedItem(out int index);

        [PreserveSig]
        int GetItemPosition(IntPtr pidl, out Point position);

        [PreserveSig] int GetSpacing(out Point spacing);
        void Slot13();
        [PreserveSig] int GetAutoArrange();
        void Slot15();

        [PreserveSig]
        int SelectAndPositionItems(
            uint count,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)]
            IntPtr[] items,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)]
            Point[] positions,
            uint flags);
    }

    // IFolderView2 inherits all fourteen IFolderView slots, followed by seven
    // methods before SetCurrentFolderFlags (verified against shobjidl.h).
    [ComImport,Guid("1AF3A467-214F-4298-908E-06B03E0B39F9"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IViewFlags {
        void Slot3();void Slot4();void Slot5();void Slot6();void Slot7();void Slot8();void Slot9();
        void Slot10();void Slot11();void Slot12();void Slot13();void Slot14();void Slot15();void Slot16();
        void Slot17();void Slot18();void Slot19();void Slot20();void Slot21();void Slot22();void Slot23();
        [PreserveSig] int SetCurrentFolderFlags(uint mask,uint flags);
        [PreserveSig] int GetCurrentFolderFlags(out uint flags);
    }
    public static bool RepairVisibleState(object desktop,bool shellHidden){
        if(shellHidden)return false;
        object view=GetView(desktop);
        try{
            var flags=(IViewFlags)view;
            return DeskPagesVisibilitySync.RevealIfStale(false,
                delegate {uint value;Check(flags.GetCurrentFolderFlags(out value));return value;},
                delegate(uint mask,uint value){Check(flags.SetCurrentFolderFlags(mask,value));});
        }finally{if(view!=null)Marshal.ReleaseComObject(view);}
    }

    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr FindWindowW(string cls,string name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr FindWindowExW(IntPtr parent,IntPtr after,string cls,string name);
    private delegate bool EnumCallback(IntPtr hwnd,IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback,IntPtr data);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd,ref Point point);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr SendMessageTimeoutW(IntPtr hwnd,uint msg,IntPtr wp,IntPtr lp,uint flags,uint timeout,out IntPtr result);
    private static IntPtr IconWindow(){
        IntPtr view=FindWindowExW(FindWindowW("Progman",null),IntPtr.Zero,"SHELLDLL_DefView",null);
        if(view==IntPtr.Zero)EnumWindows(delegate(IntPtr hwnd,IntPtr data){view=FindWindowExW(hwnd,IntPtr.Zero,"SHELLDLL_DefView",null);return view==IntPtr.Zero;},IntPtr.Zero);
        if(view==IntPtr.Zero)throw new InvalidOperationException("Desktop view is unavailable");
        IntPtr icons=FindWindowExW(view,IntPtr.Zero,"SysListView32",null);
        if(icons==IntPtr.Zero)throw new InvalidOperationException("Desktop icon window is unavailable");return icons;
    }
    public static int[] ContextGeometry(int screenX,int screenY){
        IntPtr icons=IconWindow();var point=new Point{X=screenX,Y=screenY};
        var area=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(screenX,screenY)).WorkingArea;
        var first=new Point{X=area.Left,Y=area.Top};var last=new Point{X=area.Right,Y=area.Bottom};
        if(!ScreenToClient(icons,ref point)||!ScreenToClient(icons,ref first)||!ScreenToClient(icons,ref last))throw new InvalidOperationException("Desktop coordinate conversion failed");
        return new int[]{point.X,point.Y,first.X,first.Y,last.X,last.Y};
    }
    [StructLayout(LayoutKind.Sequential)] private struct FileInfo {
        public uint Attributes,CreatedLow,CreatedHigh,AccessLow,AccessHigh,WriteLow,WriteHigh;
        public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file,out FileInfo info);
    public static string CreationIdentity(string path){
        using(var handle=CreateFileW(path,0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero)){
            FileInfo info;
            if(handle.IsInvalid || !GetFileInformationByHandle(handle,out info))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return info.Volume.ToString("X8")+":"+info.IndexHigh.ToString("X8")+info.IndexLow.ToString("X8");
        }
    }
    public static string EditingId(object desktop){
        object view=GetView(desktop);IntPtr pidl=IntPtr.Zero;
        try{int index;int hr=((IView)view).GetFocusedItem(out index);Check(hr);if(hr!=0 || index<0)return null;
            Check(((IView)view).Item(index,out pidl));return Identity(pidl);}
        finally{if(pidl!=IntPtr.Zero)Marshal.FreeCoTaskMem(pidl);if(view!=null)Marshal.ReleaseComObject(view);}
    }
    public static bool IsLabelEditing(){
        IntPtr result;
        if(SendMessageTimeoutW(IconWindow(),0x1018,IntPtr.Zero,IntPtr.Zero,2,100,out result)==IntPtr.Zero)return true;
        return result!=IntPtr.Zero;
    }

    [DllImport("shell32.dll")]
    private static extern uint ILGetSize(IntPtr pidl);

    public sealed class Entry
    {
        public string Id { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }

    private static void Check(int hr)
    {
        Marshal.ThrowExceptionForHR(hr);
    }

    private static object GetView(object desktop)
    {
        object browser = null;
        try
        {
            Guid service = new Guid(
                "4C96BE40-915C-11CF-99D3-00AA004AE837");
            Guid iid = typeof(IBrowser).GUID;

            Check(((IServiceProvider)desktop).QueryService(
                ref service, ref iid, out browser));

            object view;
            Check(((IBrowser)browser).QueryActiveShellView(out view));
            return view;
        }
        finally
        {
            if (browser != null) Marshal.ReleaseComObject(browser);
        }
    }

    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);
    public static string DescribeView(object desktop){
        object view=GetView(desktop);
        try{
            var shell=(IView)view;uint mode;int count;int modeHr=shell.GetCurrentViewMode(out mode);int countHr=shell.ItemCount(2,out count);
            int arrangeHr=shell.GetAutoArrange();
            string flagText="unknown";
            try{uint currentFlags;Check(((IViewFlags)view).GetCurrentFolderFlags(out currentFlags));flagText="0x"+currentFlags.ToString("X8");}catch(COMException){}catch(InvalidCastException){}
            return "folderFlags="+flagText+"; visible="+(countHr>=0?count.ToString():"unknown")+"; viewMode="+(modeHr>=0?mode.ToString():"unknown")+
                "; arrangeHRESULT=0x"+arrangeHr.ToString("X8")+"; iconWindowEnabled="+IsWindowEnabled(IconWindow());
        }finally{if(view!=null)Marshal.ReleaseComObject(view);}
    }
    public static bool SnapToGrid(object desktop){
        object view=GetView(desktop);
        try{uint flags;Check(((IViewFlags)view).GetCurrentFolderFlags(out flags));return (flags & 4U)!=0;}
        finally{if(view!=null)Marshal.ReleaseComObject(view);}
    }
    public static bool AutoArrange(object desktop){
        object view=GetView(desktop);
        try{int hr=((IView)view).GetAutoArrange();Check(hr);return hr==0;}
        finally{if(view!=null)Marshal.ReleaseComObject(view);}
    }
    public static Entry Spacing(object desktop){
        object view=GetView(desktop);
        try{Point spacing;Check(((IView)view).GetSpacing(out spacing));if(spacing.X<16 || spacing.Y<16)throw new InvalidOperationException("Invalid Explorer icon spacing");return new Entry{X=spacing.X,Y=spacing.Y};}
        finally{if(view!=null)Marshal.ReleaseComObject(view);}
    }
    private static string Identity(IntPtr pidl)
    {
        int size = checked((int)ILGetSize(pidl));
        if (size < 2 || size > 1048576)
            throw new InvalidOperationException("Invalid icon identity.");

        byte[] bytes = new byte[size];
        Marshal.Copy(pidl, bytes, 0, size);
        return Convert.ToBase64String(bytes);
    }

    public static Entry[] Save(object desktop)
    {
        object viewObject = GetView(desktop);
        try
        {
            var view = (IView)viewObject;
            int count;
            Check(view.ItemCount(2, out count));
            var entries = new List<Entry>();

            for (int i = 0; i < count; i++)
            {
                IntPtr pidl = IntPtr.Zero;
                try
                {
                    Check(view.Item(i, out pidl));
                    Point point;
                    Check(view.GetItemPosition(pidl, out point));
                    entries.Add(new Entry {
                        Id = Identity(pidl), X = point.X, Y = point.Y
                    });
                }
                finally
                {
                    if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
                }
            }
            return entries.ToArray();
        }
        finally { Marshal.ReleaseComObject(viewObject); }
    }

    public static int Restore(
        object desktop, string[] ids, int[] xs, int[] ys)
    {
        if (ids.Length != xs.Length || ids.Length != ys.Length)
            throw new ArgumentException("Invalid layout data.");

        var saved = new Dictionary<string, Point>();
        for (int i = 0; i < ids.Length; i++)
            saved[ids[i]] = new Point { X = xs[i], Y = ys[i] };

        object viewObject = GetView(desktop);
        var allocated = new List<IntPtr>();

        try
        {
            var view = (IView)viewObject;
            int count;
            Check(view.ItemCount(2, out count));

            var items = new List<IntPtr>();
            var positions = new List<Point>();

            for (int i = 0; i < count; i++)
            {
                IntPtr pidl;
                Check(view.Item(i, out pidl));
                allocated.Add(pidl);

                Point point;
                if (saved.TryGetValue(Identity(pidl), out point))
                {
                    items.Add(pidl);
                    positions.Add(point);
                }
            }

            if (items.Count > 0)
                Check(view.SelectAndPositionItems(
                    (uint)items.Count, items.ToArray(),
                    positions.ToArray(), 0x80));

            return items.Count;
        }
        finally
        {
            foreach (IntPtr pidl in allocated)
                Marshal.FreeCoTaskMem(pidl);
            Marshal.ReleaseComObject(viewObject);
        }
    }
}
