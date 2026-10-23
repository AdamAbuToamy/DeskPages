using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

// Some classic apps set only a process-wide Shell AppID, which cannot be
// queried through the packaged-process API. Their installed shortcuts provide
// an exact AppID -> executable mapping. No shortcut is executed or modified.
internal static class ShortcutIdentity {
    [ComImport,Guid("000214F9-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface ILink {
        void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder path,int length,IntPtr findData,uint flags);
    }
    private static readonly object gate=new object();
    private static Dictionary<string,HashSet<string>> cached;private static DateTime readAt;
    public static IDictionary<string,HashSet<string>> Map {
        get{lock(gate){if(cached==null || (DateTime.UtcNow-readAt).TotalSeconds>60){cached=Read();readAt=DateTime.UtcNow;}return cached;}}
    }
    private static Dictionary<string,HashSet<string>> Read(){
        var map=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach(string root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Microsoft","Internet Explorer","Quick Launch","User Pinned","TaskBar")}){if(string.IsNullOrEmpty(root)||!Directory.Exists(root))continue;
            string[] files;try{files=Directory.GetFiles(root,"*.lnk",SearchOption.AllDirectories);}catch(IOException){continue;}catch(UnauthorizedAccessException){continue;}
            int count=0;foreach(string file in files){
                if(++count>2048)break;object link=null;
                try{
                    link=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046")));
                    ((IPersistFile)link).Load(file,0);
                    string id=WindowShell.ReadStore((WindowShell.IStore)link,5);if(string.IsNullOrEmpty(id))continue;
                    var path=new StringBuilder(32768);((ILink)link).GetPath(path,path.Capacity,IntPtr.Zero,4);
                    string target=Environment.ExpandEnvironmentVariables(path.ToString());if(!Path.IsPathRooted(target)||!target.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))continue;
                    HashSet<string> paths;if(!map.TryGetValue(id,out paths)){paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);map.Add(id,paths);}paths.Add(Path.GetFullPath(target));
                }catch(COMException){ }catch(IOException){ }catch(UnauthorizedAccessException){ }catch(ArgumentException){ }
                finally{if(link!=null)Marshal.ReleaseComObject(link);}
            }
        }return map;
    }
}
