using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// Precision Touchpad HID collection (Digitizers 0x0D / Touch Pad 0x05).
// Reads HID descriptors through the Windows HID parser; no driver, overlay,
// device-file handle, input suppression or synthetic mouse events.
public sealed class DeskPagesTouchpad : IDisposable
{
    public sealed class Contact
    {
        public int Id;
        public double X,Y; // Descriptor-normalized 0..1, independent of screen DPI.
        public bool Tip,Confidence;
    }
    public sealed class FrameAssembler
    {
        private readonly Dictionary<int,Contact> pending=new Dictionary<int,Contact>();
        private int expected;
        private uint scan;
        private long since;
        public int Dropped {get;private set;}
        public void Reset() {pending.Clear();expected=0;}
        public bool Add(uint stamp,int count,Contact[] part,long ms,out Contact[] frame)
        {
            frame=null;
            if(count<0 || count>5 || part==null) {Dropped++;Reset();return false;}
            if(expected>0 && (stamp!=scan || ms-since>100)) {Dropped++;Reset();}
            if(count>0) {
                if(expected>0) Dropped++;
                Reset();expected=count;scan=stamp;since=ms;
            }
            if(expected==0) {
                // A parallel zero-contact report terminates a gesture. A hybrid
                // continuation with contacts but no initial count is discarded.
                bool allLifted=true;foreach(Contact c in part)if(c.Tip)allLifted=false;
                if(count==0 && allLifted) {frame=new Contact[0];return true;}
                return false;
            }
            int needed=expected-pending.Count;
            foreach(Contact contact in part) {
                if(needed<=0) break;
                // The final HID packet may contain unused padding slots.
                if(pending.ContainsKey(contact.Id)) continue;
                pending.Add(contact.Id,contact);needed--;
            }
            if(pending.Count!=expected) return false;
            frame=new Contact[pending.Count];pending.Values.CopyTo(frame,0);Reset();return true;
        }
    }
    public sealed class SwipeRecognizer
    {
        public double DistanceThreshold=0.075;
        private bool baseline,latched,blocked;
        private int id0,id1;
        private double x0,y0,x1,y1,span;
        private long last=-10000;
        public void Cancel() {baseline=false;latched=false;blocked=true;}
        public int Update(Contact[] frame,long ms,bool allowed)
        {
            if(frame==null) {Cancel();return 0;}
            // A long gap is a new sequence; incomplete HID frames never invoke us.
            if(ms-last>350) {baseline=false;latched=false;blocked=false;}
            last=ms;
            var active=new List<Contact>();
            foreach(Contact c in frame) {
                if(!c.Tip) continue;
                if(!c.Confidence || double.IsNaN(c.X) || double.IsNaN(c.Y) ||
                    c.X<0 || c.X>1 || c.Y<0 || c.Y>1) {Cancel();return 0;}
                active.Add(c);
            }
            if(active.Count==0) {baseline=false;latched=false;blocked=false;return 0;}
            if(!allowed || active.Count>2) {Cancel();return 0;}
            if(latched && active.Count==2){
                active.Sort(delegate(Contact a,Contact b){return a.Id.CompareTo(b.Id);});
                if(id0!=active[0].Id || id1!=active[1].Id){latched=false;baseline=false;}
            }
            if(latched || blocked) return 0;
            if(active.Count!=2) {baseline=false;return 0;}
            active.Sort(delegate(Contact a,Contact b) {return a.Id.CompareTo(b.Id);});
            Contact a0=active[0],a1=active[1];
            if(a0.Id==a1.Id) {Cancel();return 0;}
            if(!baseline || id0!=a0.Id || id1!=a1.Id) {
                baseline=true;id0=a0.Id;id1=a1.Id;
                x0=a0.X;y0=a0.Y;x1=a1.X;y1=a1.Y;
                span=Distance(x1-x0,y1-y0);return 0;
            }
            double dx0=a0.X-x0,dx1=a1.X-x1,dy0=a0.Y-y0,dy1=a1.Y-y1;
            double dx=(dx0+dx1)/2,dy=(dy0+dy1)/2;
            double nowSpan=Distance(a1.X-a0.X,a1.Y-a0.Y);
            if(Math.Abs(nowSpan-span)>Math.Max(0.035,span*0.25)) {Cancel();return 0;}
            // Both fingers must translate together. Opposite motion is pinch/rotation.
            if(Math.Abs(dx)<DistanceThreshold || Math.Abs(dx)<Math.Abs(dy)*1.8 ||
                Math.Abs(dx0)<0.025 || Math.Abs(dx1)<0.025 || Math.Sign(dx0)!=Math.Sign(dx1) ||
                Math.Abs(dx0)<Math.Abs(dy0)*1.3 || Math.Abs(dx1)<Math.Abs(dy1)*1.3) return 0;
            latched=true;return Math.Sign(dx);
        }
        private static double Distance(double x,double y) {return Math.Sqrt(x*x+y*y);}
    }
    // Exact RAWINPUT framing, kept pure so 32/64-bit and batched HID reports can be tested.
    public static bool GetReportBounds(byte[] bytes,int pointerBytes,out int offset,out int size,out int count)
    {
        offset=size=count=0;
        if(bytes==null || (pointerBytes!=4 && pointerBytes!=8)) return false;
        int header=8+2*pointerBytes;
        if(bytes.Length<header+8 || BitConverter.ToUInt32(bytes,0)!=2) return false;
        uint declared=BitConverter.ToUInt32(bytes,4),s=BitConverter.ToUInt32(bytes,header),n=BitConverter.ToUInt32(bytes,header+4);
        if(declared>bytes.Length || declared<header+8 || s==0 || n==0 || s>65536 || n>1024 ||
            (ulong)(header+8)+(ulong)s*n>declared) return false;
        offset=header+8;size=(int)s;count=(int)n;return true;
    }
    [StructLayout(LayoutKind.Sequential)] private struct RawDevice {public ushort Page,Usage;public uint Flags;public IntPtr Target;}
    [StructLayout(LayoutKind.Sequential)] private struct Caps
    {
        public ushort Usage,UsagePage,InputLength,OutputLength,FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=17)] public ushort[] Reserved;
        public ushort Links,InputButtons,InputValues,InputIndices,OutputButtons,OutputValues,OutputIndices,FeatureButtons,FeatureValues,FeatureIndices;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ValueCap
    {
        public ushort Page;public byte ReportId,Alias;public ushort BitField,Link,LinkUsage,LinkPage;
        public byte IsRange,IsStringRange,IsDesignatorRange,IsAbsolute,HasNull,Reserved;
        public ushort BitSize,ReportCount;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=5)] public ushort[] Reserved2;
        public uint UnitsExp,Units;public int LogicalMin,LogicalMax,PhysicalMin,PhysicalMax;
        public ushort UsageMin,UsageMax,StringMin,StringMax,DesignatorMin,DesignatorMax,DataMin,DataMax;
        public bool Contains(ushort usage) {return IsRange!=0?usage>=UsageMin && usage<=UsageMax:usage==UsageMin;}
    }
    [DllImport("user32.dll",SetLastError=true)] private static extern bool RegisterRawInputDevices(RawDevice[] devices,uint count,uint size);
    [DllImport("user32.dll",SetLastError=true)] private static extern uint GetRawInputDeviceInfoW(IntPtr device,uint command,IntPtr data,ref uint size);
    [DllImport("hid.dll")] private static extern uint HidP_GetCaps(IntPtr preparsed,out Caps caps);
    [DllImport("hid.dll")] private static extern uint HidP_GetValueCaps(int reportType,[Out] ValueCap[] caps,ref ushort length,IntPtr preparsed);
    [DllImport("hid.dll")] private static extern uint HidP_GetUsageValue(int reportType,ushort page,ushort link,ushort usage,out uint value,IntPtr preparsed,IntPtr report,uint length);
    [DllImport("hid.dll")] private static extern uint HidP_GetUsages(int reportType,ushort page,ushort link,[Out] ushort[] usages,ref uint count,IntPtr preparsed,IntPtr report,uint length);
    private const uint Success=0x00110000;
    private sealed class DeviceState : IDisposable
    {
        public IntPtr Preparsed;
        public ValueCap[] Values;
        public readonly FrameAssembler Frames=new FrameAssembler();
        public readonly SwipeRecognizer Gesture=new SwipeRecognizer();
        public void Dispose() {if(Preparsed!=IntPtr.Zero){Marshal.FreeHGlobal(Preparsed);Preparsed=IntPtr.Zero;}}
        public bool Value(ushort page,ushort usage,ushort? link,byte id,IntPtr report,uint length,out uint value,out ValueCap cap)
        {
            value=0;cap=new ValueCap();
            foreach(ValueCap c in Values) {
                if(c.Page!=page || !c.Contains(usage) || c.ReportId!=id || (link.HasValue && c.Link!=link.Value)) continue;
                if(HidP_GetUsageValue(0,page,c.Link,usage,out value,Preparsed,report,length)==Success){cap=c;return true;}
            }
            return false;
        }
        public bool Contacts(IntPtr report,uint length,out uint scan,out int count,out Contact[] contacts)
        {
            scan=0;count=0;contacts=null;
            byte id=Marshal.ReadByte(report);uint rawCount;ValueCap cap;
            if(!Value(0x0D,0x54,null,id,report,length,out rawCount,out cap) || rawCount>5 ||
                !Value(0x0D,0x56,null,id,report,length,out scan,out cap)) return false;
            count=(int)rawCount;var list=new List<Contact>();var links=new HashSet<ushort>();
            foreach(ValueCap c in Values) {
                if(c.Page!=0x0D || !c.Contains(0x51) || c.ReportId!=id || !links.Add(c.Link)) continue;
                uint contactId,x,y;ValueCap xcap,ycap;
                if(!Value(0x0D,0x51,c.Link,id,report,length,out contactId,out cap) ||
                    !Value(1,0x30,c.Link,id,report,length,out x,out xcap) ||
                    !Value(1,0x31,c.Link,id,report,length,out y,out ycap)) return false;
                if(xcap.LogicalMin<0 || ycap.LogicalMin<0 || xcap.LogicalMax<=xcap.LogicalMin || ycap.LogicalMax<=ycap.LogicalMin) return false;
                var usages=new ushort[64];uint used=(uint)usages.Length;
                if(HidP_GetUsages(0,0x0D,c.Link,usages,ref used,Preparsed,report,length)!=Success || used>usages.Length) return false;
                bool tip=false,confidence=false;
                for(int u=0;u<(int)used;u++){if(usages[u]==0x42)tip=true;if(usages[u]==0x47)confidence=true;}
                list.Add(new Contact {Id=unchecked((int)contactId),X=((double)x-xcap.LogicalMin)/(xcap.LogicalMax-xcap.LogicalMin),
                    Y=((double)y-ycap.LogicalMin)/(ycap.LogicalMax-ycap.LogicalMin),Tip=tip,Confidence=confidence});
            }
            // With zero count, no slots belong to this frame. Continuations of a
            // hybrid frame do have zero count, so retain slots until assembly.
            contacts=list.ToArray();return true;
        }
    }
    private readonly Dictionary<IntPtr,DeviceState> devices=new Dictionary<IntPtr,DeviceState>();
    private readonly HashSet<IntPtr> rejected=new HashSet<IntPtr>();
    private bool registered;
    public long Packets {get;private set;}
    public long ValidFrames {get;private set;}
    public long Errors {get;private set;}
    public long Recognized {get;private set;}
    public long LastFrameMs {get;private set;}
    public long LastGestureMs {get;private set;}
    public int Contacts {get;private set;}
    public int MaximumContacts {get;private set;}
    public string Status {get;private set;}
    public DeskPagesTouchpad() {Status="not registered";LastFrameMs=-10000;LastGestureMs=-10000;}
    public bool Register(IntPtr window)
    {
        var device=new RawDevice {Page=0x0D,Usage=5,Flags=0x2100,Target=window}; // INPUTSINK | DEVNOTIFY
        registered=RegisterRawInputDevices(new RawDevice[]{device},1,(uint)Marshal.SizeOf(typeof(RawDevice)));
        Status=registered?"HID 0D/05 background registration ready":"HID registration failed, Win32="+Marshal.GetLastWin32Error();
        return registered;
    }
    private DeviceState Open(IntPtr device)
    {
        uint length=0;
        if(GetRawInputDeviceInfoW(device,0x20000005,IntPtr.Zero,ref length)==uint.MaxValue || length==0 || length>1048576) throw new InvalidOperationException("HID descriptor size unavailable");
        var state=new DeviceState();
        try {
            state.Preparsed=Marshal.AllocHGlobal((int)length);
            if(GetRawInputDeviceInfoW(device,0x20000005,state.Preparsed,ref length)==uint.MaxValue) throw new InvalidOperationException("HID descriptor read failed");
            Caps caps;
            if(HidP_GetCaps(state.Preparsed,out caps)!=Success || caps.UsagePage!=0x0D || caps.Usage!=5 || caps.InputValues==0 || caps.InputValues>512) throw new InvalidOperationException("Not a supported Precision Touchpad descriptor");
            ushort count=caps.InputValues;state.Values=new ValueCap[count];
            if(HidP_GetValueCaps(0,state.Values,ref count,state.Preparsed)!=Success) throw new InvalidOperationException("HID value capabilities unavailable");
            if(count<state.Values.Length)Array.Resize(ref state.Values,count);
            return state;
        } catch {state.Dispose();throw;}
    }
    public double DistanceThreshold=0.075;
    public int Process(IntPtr packet,uint length,long ms,bool allowed)
    {
        Packets++;
        DeviceState state=null;
        try {
            if(length>1048576) return 0;
            var bytes=new byte[length];Marshal.Copy(packet,bytes,0,(int)length);
            int offset,size,count;
            if(!GetReportBounds(bytes,IntPtr.Size,out offset,out size,out count)){Errors++;return 0;}
            IntPtr device=Marshal.ReadIntPtr(packet,8);
            if(device==IntPtr.Zero || rejected.Contains(device)) return 0;
            if(!devices.TryGetValue(device,out state)) {
                try {state=Open(device);devices.Add(device,state);Status="Precision Touchpad HID descriptor parsed";}
                catch(Exception ex){rejected.Add(device);Errors++;Status=ex.Message;return 0;}
            }
            int emitted=0;
            for(int r=0;r<count;r++) {
                uint scan;int contacts;Contact[] part,frame;
                if(!state.Contacts(IntPtr.Add(packet,offset+r*size),(uint)size,out scan,out contacts,out part)) {
                    Errors++;state.Frames.Reset();state.Gesture.Cancel();Status="Unsupported or incomplete HID contact report";continue;
                }
                int dropped=state.Frames.Dropped;
                bool complete=state.Frames.Add(scan,contacts,part,ms,out frame);
                if(state.Frames.Dropped!=dropped){Errors++;state.Gesture.Cancel();}
                if(!complete)continue;
                ValidFrames++;LastFrameMs=ms;Contacts=0;
                foreach(Contact c in frame)if(c.Tip)Contacts++;
                MaximumContacts=Math.Max(MaximumContacts,Contacts);
                state.Gesture.DistanceThreshold=DistanceThreshold;
                int direction=state.Gesture.Update(frame,ms,allowed);
                if(direction!=0){Recognized++;LastGestureMs=ms;emitted=direction;}
            }
            return emitted;
        } catch(Exception ex) {
            Errors++;Status="HID parsing error: "+ex.Message;
            if(state!=null){state.Frames.Reset();state.Gesture.Cancel();}
            return 0;
        }
    }
    public void Remove(IntPtr device)
    {
        DeviceState state;if(devices.TryGetValue(device,out state)){state.Dispose();devices.Remove(device);}
        rejected.Remove(device);
    }
    public string Diagnostics()
    {
        return Status+"\r\nHID packets / valid frames / errors: "+Packets+" / "+ValidFrames+" / "+Errors+
            "\r\nHID contacts last / maximum: "+Contacts+" / "+MaximumContacts+
            "\r\nHID gestures recognized: "+Recognized;
    }
    public void Dispose()
    {
        if(registered) {
            var device=new RawDevice {Page=0x0D,Usage=5,Flags=1,Target=IntPtr.Zero};
            RegisterRawInputDevices(new RawDevice[]{device},1,(uint)Marshal.SizeOf(typeof(RawDevice)));registered=false;
        }
        foreach(DeviceState state in devices.Values)state.Dispose();devices.Clear();rejected.Clear();
    }
}
