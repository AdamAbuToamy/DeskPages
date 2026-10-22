using System;
using System.Runtime.InteropServices;

// Monitor interface paths survive DISPLAY1/DISPLAY2 renumbering. No screen
// configuration is changed; WinForms still supplies the live work-area bounds.
public static class DeskPagesDisplayIdentity {
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct Device {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Id;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Key;
    }
    public static string ForDevice(string name){var device=new Device{Size=Marshal.SizeOf(typeof(Device))};return EnumDisplayDevicesW(name,0,ref device,1) && !string.IsNullOrEmpty(device.Id)?device.Id:name;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool EnumDisplayDevicesW(string name,uint index,ref Device device,uint flags);
}
