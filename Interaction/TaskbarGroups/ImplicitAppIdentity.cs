using System;
using System.IO;
using System.Runtime.InteropServices;

// Shell-generated classic taskbar IDs can be executable paths, with a known
// folder GUID prefix. Resolve that exact path; never infer a process from a label.
internal static class ImplicitAppIdentity {
    public static string PathFor(string id){
        if(string.IsNullOrWhiteSpace(id))return null;
        try{
            string path=Environment.ExpandEnvironmentVariables(id.Trim().Trim('"'));
            Guid folder;
            if(path.Length>39 && path[38]=='\\' && Guid.TryParse(path.Substring(0,38),out folder)){
                IntPtr value;int hr=SHGetKnownFolderPath(ref folder,0,IntPtr.Zero,out value);
                if(hr<0)return null;
                try{path=Path.Combine(Marshal.PtrToStringUni(value),path.Substring(39));}finally{Marshal.FreeCoTaskMem(value);}
            }
            if(!Path.IsPathRooted(path) || !path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))return null;
            return Path.GetFullPath(path);
        }catch(ArgumentException){return null;}catch(NotSupportedException){return null;}catch(PathTooLongException){return null;}
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern int SHGetKnownFolderPath(ref Guid folder,uint flags,IntPtr token,out IntPtr path);
}
