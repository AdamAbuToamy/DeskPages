using System;
using System.Runtime.InteropServices;
using ComData=System.Runtime.InteropServices.ComTypes.IDataObject;

internal sealed class ShellDragImage:IDeskPagesDragImageSink,IDisposable {
    [StructLayout(LayoutKind.Sequential)]private struct Point{public int X,Y;}
    [ComImport,Guid("4657278B-411B-11D2-839A-00C04FD918D0"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropTargetHelper {
        void DragEnter(IntPtr target,[MarshalAs(UnmanagedType.Interface)] ComData data,ref Point point,uint effect);
        void DragLeave();
        void DragOver(ref Point point,uint effect);
        void Drop([MarshalAs(UnmanagedType.Interface)] ComData data,ref Point point,uint effect);
        void Show([MarshalAs(UnmanagedType.Bool)] bool show);
    }
    private IDropTargetHelper helper;
    private IDropTargetHelper Get(){if(helper==null)helper=(IDropTargetHelper)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("4657278A-411B-11D2-839A-00C04FD918D0"),true));return helper;}
    private static ComData Data(object data){var value=data as ComData;if(value==null)throw new InvalidOperationException("Original OLE IDataObject is unavailable");return value;}
    public void Enter(IntPtr target,object data,int x,int y,uint effect){var point=new Point{X=x,Y=y};Get().DragEnter(target,Data(data),ref point,effect);}
    public void Over(int x,int y,uint effect){var point=new Point{X=x,Y=y};Get().DragOver(ref point,effect);}
    public void Leave(){if(helper!=null)helper.DragLeave();}
    public void Drop(object data,int x,int y,uint effect){var point=new Point{X=x,Y=y};Get().Drop(Data(data),ref point,effect);}
    public void Dispose(){if(helper!=null){Marshal.ReleaseComObject(helper);helper=null;}}
}
