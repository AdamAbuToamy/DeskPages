using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class OriginalDesktopReader
{
    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(
            ref Guid service, ref Guid iid,
            [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }

    [ComImport]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        // Unused methods retain the native interface order.
        void GetWindow();
        void ContextSensitiveHelp();
        void InsertMenus();
        void SetMenu();
        void RemoveMenus();
        void SetStatusText();
        void EnableModeless();
        void TranslateAccelerator();
        void BrowseObject();
        void GetViewStateStream();
        void GetControlWindow();
        void SendControlMessage();

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

    [ComImport]
    [Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        void GetCurrentViewMode();
        void SetCurrentViewMode();
        void GetFolder();

        [PreserveSig]
        int Item(int index, out IntPtr pidl);

        [PreserveSig]
        int ItemCount(uint flags, out int count);

        void Items();
        void GetSelectionMarkedItem();
        void GetFocusedItem();

        [PreserveSig]
        int GetItemPosition(IntPtr pidl, out Point position);
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetNameFromIDList(
        IntPtr pidl, uint kind, out IntPtr name);

    [DllImport("shell32.dll")]
    private static extern uint ILGetSize(IntPtr pidl);

    public sealed class IconInfo
    {
        public string Name { get; set; }
        public string Key { get; set; }
        public string Id { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }

    private static void Check(int result)
    {
        Marshal.ThrowExceptionForHR(result);
    }

    public static IconInfo[] Read(object desktop)
    {
        object browserObject = null;
        object viewObject = null;

        try
        {
            var provider = (IServiceProvider)desktop;
            Guid service = new Guid(
                "4C96BE40-915C-11CF-99D3-00AA004AE837");
            Guid browserId = typeof(IShellBrowser).GUID;

            Check(provider.QueryService(
                ref service, ref browserId, out browserObject));

            Check(((IShellBrowser)browserObject)
                .QueryActiveShellView(out viewObject));

            var view = (IFolderView)viewObject;
            int count;
            Check(view.ItemCount(2, out count));

            var result = new List<IconInfo>();

            for (int i = 0; i < count; i++)
            {
                IntPtr pidl = IntPtr.Zero;
                IntPtr text = IntPtr.Zero;

                try
                {
                    Check(view.Item(i, out pidl));
                    Point position;
                    Check(view.GetItemPosition(pidl, out position));

                    int nameResult = SHGetNameFromIDList(
                        pidl, 0, out text);

                    string name = nameResult >= 0 && text != IntPtr.Zero
                        ? Marshal.PtrToStringUni(text)
                        : "(name unavailable)";

                    string stableKey = name;
                    IntPtr keyText;
                    if (SHGetNameFromIDList(pidl, 0x80028000, out keyText) >= 0 && keyText != IntPtr.Zero)
                    {
                        try { stableKey = Marshal.PtrToStringUni(keyText); }
                        finally { Marshal.FreeCoTaskMem(keyText); }
                    }
                    int size = checked((int)ILGetSize(pidl));
                    if (size < 2 || size > 1048576) throw new InvalidOperationException("Invalid PIDL.");
                    byte[] bytes = new byte[size];
                    Marshal.Copy(pidl, bytes, 0, size);

                    result.Add(new IconInfo {
                        Key = stableKey, Id = Convert.ToBase64String(bytes),
                        Name = name,
                        X = position.X,
                        Y = position.Y
                    });
                }
                finally
                {
                    if (text != IntPtr.Zero)
                        Marshal.FreeCoTaskMem(text);
                    if (pidl != IntPtr.Zero)
                        Marshal.FreeCoTaskMem(pidl);
                }
            }

            return result.ToArray();
        }
        finally
        {
            if (viewObject != null)
                Marshal.ReleaseComObject(viewObject);
            if (browserObject != null)
                Marshal.ReleaseComObject(browserObject);
        }
    }
}
