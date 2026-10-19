using System;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;

public static class DeskPagesInstallLocation {
    private static bool TrustedOwner(string sid){return sid=="S-1-5-18" || sid=="S-1-5-32-544" || sid=="S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";}
    public static bool UnsafeRights(FileSystemRights rights,bool programRoot){
        FileSystemRights dangerous=FileSystemRights.DeleteSubdirectoriesAndFiles|FileSystemRights.ChangePermissions|FileSystemRights.TakeOwnership;
        if(programRoot)dangerous|=FileSystemRights.WriteData|FileSystemRights.AppendData|FileSystemRights.WriteAttributes|FileSystemRights.WriteExtendedAttributes|FileSystemRights.Delete;
        return (rights&dangerous)!=0;
    }
    public static void CheckAncestors(string path,bool includeRoot){
        var current=new DirectoryInfo(includeRoot?path:Path.GetDirectoryName(path));
        bool programRoot=includeRoot;
        for(;current!=null;current=current.Parent){
            if(!current.Exists){programRoot=false;continue;}
            if((current.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Choose a real local folder, without links or junctions.");
            var acl=current.GetAccessControl(AccessControlSections.Access|AccessControlSections.Owner);
            string owner=((SecurityIdentifier)acl.GetOwner(typeof(SecurityIdentifier))).Value;
            if(!TrustedOwner(owner))throw new UnauthorizedAccessException("Choose a protected parent folder, for example Program Files or a folder owned by Administrators on another drive.");
            foreach(FileSystemAccessRule rule in acl.GetAccessRules(true,true,typeof(SecurityIdentifier))){
                if(rule.AccessControlType!=AccessControlType.Allow || (rule.PropagationFlags&PropagationFlags.InheritOnly)!=0)continue;
                string who=((SecurityIdentifier)rule.IdentityReference).Value;
                if(!TrustedOwner(who) && UnsafeRights(rule.FileSystemRights,programRoot))throw new UnauthorizedAccessException("This location allows non-administrators to replace startup code. Choose a protected folder.");
            }
            programRoot=false;
        }
    }
    public static string Resolve(string selected){
        if(string.IsNullOrWhiteSpace(selected) || !Path.IsPathRooted(selected) || selected.StartsWith(@"\\") || selected.IndexOf('"')>=0)throw new ArgumentException("Choose an absolute local installation folder.");
        string path=Path.GetFullPath(selected).TrimEnd(Path.DirectorySeparatorChar);
        string root=Path.GetPathRoot(path);
        if(path.Length<=root.TrimEnd(Path.DirectorySeparatorChar).Length || path.IndexOf(':',2)>=0)throw new ArgumentException("Choose a folder, not a drive root.");
        var drive=new DriveInfo(root);if(!drive.IsReady || drive.DriveType!=DriveType.Fixed || (drive.DriveFormat!="NTFS" && drive.DriveFormat!="ReFS"))throw new ArgumentException("Choose a fixed NTFS/ReFS drive.");
        string profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows),programFiles=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach(string excluded in new[]{profile,windows})if(path.Equals(excluded,StringComparison.OrdinalIgnoreCase)||path.StartsWith(excluded+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Choose a folder outside Windows and user data.");
        if(path.Equals(programFiles,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Choose an application subfolder, not Program Files itself.");
        if(Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length!=0){
            string accountFile=Path.Combine(path,"installed-account.txt");
            string sid=WindowsIdentity.GetCurrent().User.Value;
            if(File.Exists(accountFile) && File.ReadAllText(accountFile).Trim()!=sid)throw new UnauthorizedAccessException("This installation belongs to another Windows account.");
            string folder=Path.GetFileName(path);
            if(folder.StartsWith("DeskPages-S-1-",StringComparison.OrdinalIgnoreCase) && !folder.Equals("DeskPages-"+sid,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("Choose an installation folder for your own Windows account.");
            string guard=Path.Combine(path,"DeskPagesStartup.exe");
            if(!File.Exists(guard)||!File.Exists(Path.Combine(path,"Agent.ps1"))||AssemblyName.GetAssemblyName(guard).Name!="DeskPagesStartup")throw new IOException("Choose an empty folder or an existing DeskPages installation.");
        }
        CheckAncestors(path,false);return path;
    }
    public static bool IsProtectedGuardian(string guardian){
        try{if(!File.Exists(guardian) || Path.GetFileName(guardian)!="DeskPagesStartup.exe")return false;CheckAncestors(Path.GetDirectoryName(Path.GetFullPath(guardian)),true);return true;}catch{return false;}
    }
}
