using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Managed bridge only. Motion, presentation fences, capture and lifecycle live
// in the native D3D11/DirectComposition renderer, never in a WPF frame loop.
public static class DeskPagesAnimation
{
    private static string libraryPath;
    private static IntPtr library;
    private static string tracePath;
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern IntPtr LoadLibraryExW(string file,IntPtr reserved,uint flags);
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int DPInitialize();
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int DPBegin(IntPtr pixels,int width,int height,int stride,int left,int top);
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int DPPlay(IntPtr pixels,int width,int height,int stride,int direction);
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int DPConfirmTarget(int iconCount);
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl)] private static extern void DPAbort();
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl)] private static extern void DPShutdown();
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int DPGetState();
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl)] private static extern uint DPGetCaptureSample();
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)] private static extern int DPGetReport(StringBuilder text,int length);
    public static bool SnapshotCacheEnabled;public static string SnapshotSourceKey="",SnapshotTargetKey="";
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl)]static extern void DPInvalidateSnapshots();
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl)]static extern int DPClearSnapshots();
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl)]static extern ulong DPGetSnapshotValue(int index);
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)]static extern int DPWarmSnapshot(string key,int left,int top,int width,int height);
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)]static extern int DPConfirmCachedTarget(int count,string key);
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)]static extern int DPBeginDesktopCachedRegions(int left,int top,int width,int height,int[] regions,int count,string source);
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl)]static extern void DPInvalidateSnapshotPage(int page);
    public static void InvalidateSnapshotPage(int page){if(library!=IntPtr.Zero)DPInvalidateSnapshotPage(page);}
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)]static extern int DPConfigureSnapshotStore(string folder,string scope);
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)]static extern int DPGetSavedSnapshotKey(int page,StringBuilder key,int count);
    public static void ConfigureSnapshotStore(string folder,string scope){Initialize();if(DPConfigureSnapshotStore(folder,scope)==0)throw new IOException("Cannot configure the protected snapshot store.");}
    public static string SavedSnapshotKey(int page){Initialize();var key=new StringBuilder(512);return DPGetSavedSnapshotKey(page,key,key.Capacity)>0?key.ToString():"";}
    public static void InvalidateSnapshots(){if(library!=IntPtr.Zero)DPInvalidateSnapshots();}
    public static void ClearSnapshots(){if(library!=IntPtr.Zero)DPClearSnapshots();}
    public static ulong SnapshotCacheBytes {get{return library==IntPtr.Zero?0:DPGetSnapshotValue(0);}}
    public static string DiskValuesCsv(){return library==IntPtr.Zero?"0,0,0":DPGetSnapshotValue(8)+","+DPGetSnapshotValue(9)+","+DPGetSnapshotValue(10);}
    public static ulong CaptureCalls {get{return library==IntPtr.Zero?0:DPGetSnapshotValue(4);}}
    public static string SnapshotValuesCsv(){if(library==IntPtr.Zero)return "0,0,0,0,0,0,0";return DPGetSnapshotValue(5)+","+DPGetSnapshotValue(6)+","+DPGetSnapshotValue(0)+","+DPGetSnapshotValue(1)+","+DPGetSnapshotValue(2)+","+DPGetSnapshotValue(3)+","+DPGetSnapshotValue(4);}
    public static bool WarmSnapshot(string key,Rectangle bounds){if(!SnapshotCacheEnabled || string.IsNullOrEmpty(key))return false;Initialize();bool ok=DPWarmSnapshot(key,bounds.Left,bounds.Top,bounds.Width,bounds.Height)!=0;if(!ok)Trace("idle snapshot skipped: "+Metrics);return ok;}
    public static void Configure(string baseDirectory) {
        libraryPath=Path.Combine(baseDirectory,"Native","DeskPagesCompositor.dll");
        tracePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPages","HiddenPages","agent.log");
    }
    public static void Trace(string message) {
        try {if(tracePath!=null){Directory.CreateDirectory(Path.GetDirectoryName(tracePath));File.AppendAllText(tracePath,DateTime.UtcNow.ToString("o")+" animation: "+message+Environment.NewLine);}}
        catch(IOException){}catch(UnauthorizedAccessException){}
    }
    public static void ConfirmDesktopTarget(int iconCount) {
        Initialize();if((SnapshotCacheEnabled?DPConfirmCachedTarget(iconCount,SnapshotTargetKey):DPConfirmTarget(iconCount))==0)throw new InvalidOperationException(Metrics);
    }
    public static void Initialize()
    {
        if(library!=IntPtr.Zero)return;
        if(IntPtr.Size!=8)throw new PlatformNotSupportedException("This renderer requires 64-bit Windows PowerShell.");
        if(string.IsNullOrEmpty(libraryPath) || !File.Exists(libraryPath))throw new FileNotFoundException("Native compositor DLL is missing. Extract the complete DeskPages package.",libraryPath);
        library=LoadLibraryExW(libraryPath,IntPtr.Zero,0x900); // DLL's folder + System32 dependencies
        if(library==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not load the native compositor.");
    }
    public static void VerifyNativeAvailable() {Initialize();if(DPInitialize()==0)throw new InvalidOperationException(Metrics);}
    public static string Metrics {
        get {
            if(library==IntPtr.Zero)return "native renderer not loaded";
            var report=new StringBuilder(1024);DPGetReport(report,report.Capacity);return report.ToString();
        }
    }
    public static bool IsVisible {get{return library!=IntPtr.Zero && (DPGetState()&1)!=0;}}
[DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl)]static extern int DPIsTransitionWindow(IntPtr window);
    public static bool IsTransitionWindow(IntPtr window){return library!=IntPtr.Zero && DPIsTransitionWindow(window)!=0;}
    public static bool MotionComplete {get{return library!=IntPtr.Zero && (DPGetState()&4)!=0;}}
    public static bool IsPlaying {get{return library!=IntPtr.Zero && (DPGetState()&2)!=0;}}
    public static uint CaptureSample {get{return DPGetCaptureSample();}}
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int DPBeginDesktop(int left,int top,int width,int height);
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int DPProbeWindowCapture(IntPtr source,int left,int top,int width,int height,out uint center);
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int DPBeginDesktopRegions(int left,int top,int width,int height,int[] regions,int count);
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)]static extern int DPBeginCachedPreview(int left,int top,int width,int height,int[] regions,int count,string source,string target,int direction);
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl)]static extern int DPCompleteCachedPreview();
    public static bool BeginCachedPreview(Rectangle bounds,string source,string target,int direction){
        Initialize();var data=new System.Collections.Generic.List<int>();
        foreach(var region in DesktopRegions(bounds))data.AddRange(region);
        return DPBeginCachedPreview(bounds.Left,bounds.Top,bounds.Width,bounds.Height,data.ToArray(),data.Count/5,source,target,direction)!=0;
    }
    public static void CompleteCachedPreview(){if(DPCompleteCachedPreview()==0)throw new InvalidOperationException(Metrics);}
    public static bool BeginDesktop(Rectangle bounds){
        Initialize();var data=new System.Collections.Generic.List<int>();
        foreach(var region in DesktopRegions(bounds))data.AddRange(region);
        return (SnapshotCacheEnabled?DPBeginDesktopCachedRegions(bounds.Left,bounds.Top,bounds.Width,bounds.Height,data.ToArray(),data.Count/5,SnapshotSourceKey):DPBeginDesktopRegions(bounds.Left,bounds.Top,bounds.Width,bounds.Height,data.ToArray(),data.Count/5))!=0;
    }
    public static Rectangle DesktopBounds {get{
        var screens=System.Windows.Forms.Screen.AllScreens;
        return DeskPagesCapturePlan.Bounds(System.Windows.Forms.Screen.PrimaryScreen.WorkingArea,Array.ConvertAll(screens,s=>s.WorkingArea),DeskPagesWorkspaceApi.PrimaryOnly);
    }}
    static System.Collections.Generic.IEnumerable<int[]> DesktopRegions(Rectangle bounds){
        foreach(var screen in System.Windows.Forms.Screen.AllScreens){
            var r=DeskPagesCapturePlan.Intersection(screen.WorkingArea,bounds);
            if(r.IsEmpty)continue;
            yield return new[]{r.Left-bounds.Left,r.Top-bounds.Top,r.Width,r.Height,(!DeskPagesWorkspaceApi.PrimaryOnly || screen.Primary)?1:0};
        }
    }
    public static uint ProbeWindow(IntPtr source,Rectangle bounds){Initialize();uint sample;if(DPProbeWindowCapture(source,bounds.Left,bounds.Top,bounds.Width,bounds.Height,out sample)==0)throw new InvalidOperationException(Metrics);return sample;}
    public static bool Begin(Bitmap before,Rectangle bounds)
    {
        Initialize();
        if(before.Width!=bounds.Width || before.Height!=bounds.Height)throw new ArgumentException("Source bitmap does not match physical screen bounds.");
        using(Bitmap copy=before.Clone(new Rectangle(0,0,before.Width,before.Height),PixelFormat.Format32bppArgb)) {
            BitmapData bits=copy.LockBits(new Rectangle(0,0,copy.Width,copy.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try {
                if(bits.Stride<copy.Width*4)throw new InvalidOperationException("Unsupported bitmap stride.");
                return DPBegin(bits.Scan0,copy.Width,copy.Height,bits.Stride,bounds.Left,bounds.Top)!=0;
            }finally{copy.UnlockBits(bits);}
        }
    }
    [DllImport("DeskPagesCompositor.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int DPBeginRegions(IntPtr pixels,int width,int height,int stride,int left,int top,int[] regions,int count);
    // Synthetic monitor fixture: uses the same native visual tree without
    // touching Explorer, attributes, registry, membership or real display modes.
    public static bool BeginRegions(Bitmap before,Rectangle bounds,int[] regions){
        if(before.Width!=bounds.Width || before.Height!=bounds.Height || regions==null || regions.Length%5!=0)throw new ArgumentException("Invalid simulated monitor atlas.");
        Initialize();using(Bitmap copy=before.Clone(new Rectangle(0,0,before.Width,before.Height),PixelFormat.Format32bppArgb)){
            BitmapData bits=copy.LockBits(new Rectangle(0,0,copy.Width,copy.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try{return DPBeginRegions(bits.Scan0,copy.Width,copy.Height,bits.Stride,bounds.Left,bounds.Top,regions,regions.Length/5)!=0;}finally{copy.UnlockBits(bits);}
        }
    }
    public static void Play(Bitmap after,int direction)
    {
        Initialize();
        using(Bitmap copy=after.Clone(new Rectangle(0,0,after.Width,after.Height),PixelFormat.Format32bppArgb)) {
            BitmapData bits=copy.LockBits(new Rectangle(0,0,copy.Width,copy.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try {if(DPPlay(bits.Scan0,copy.Width,copy.Height,bits.Stride,direction)==0)throw new InvalidOperationException(Metrics);}
            finally{copy.UnlockBits(bits);}
        }
    }
    public static void CaptureAndPlay(int direction)
    {
        Initialize();
        if(DPPlay(IntPtr.Zero,0,0,0,direction)==0)throw new InvalidOperationException(Metrics);
    }
    public static void Abort(){if(library!=IntPtr.Zero)DPAbort();}
    public static void Shutdown(){if(library!=IntPtr.Zero)DPShutdown();}
}
