using System;
using System.Runtime.InteropServices;

public static class DpOriginalViewRefresh
{
    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void Slot3(); void Slot4(); void Slot5();
        void Slot6(); void Slot7();

        [PreserveSig]
        int Refresh();
    }

    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId,uint flags,string item1,IntPtr item2);
    public static void NotifyAttributes(string[] paths){
        if(paths==null)return;
        foreach(string path in paths)if(!string.IsNullOrEmpty(path))SHChangeNotify(0x800,0x1005,path,IntPtr.Zero); // ATTRIBUTES | PATHW | FLUSH
    }
    public static void Run(object view)
    {
        Marshal.ThrowExceptionForHR(((IShellView)view).Refresh());
    }
}
