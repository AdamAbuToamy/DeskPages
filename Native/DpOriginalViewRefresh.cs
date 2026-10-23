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

    public static void Run(object view)
    {
        Marshal.ThrowExceptionForHR(((IShellView)view).Refresh());
    }
}
