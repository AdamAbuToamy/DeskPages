using System;
using System.Runtime.InteropServices;
using System.Threading;

public static class DeskPagesThreadService {
    [StructLayout(LayoutKind.Sequential)]struct Power {public uint Version,ControlMask,StateMask;}
    [StructLayout(LayoutKind.Sequential)]struct Memory {public uint Priority;}
    [DllImport("kernel32.dll")]static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll",EntryPoint="SetThreadInformation")]static extern bool SetPower(IntPtr thread,int kind,ref Power value,int size);
    [DllImport("kernel32.dll",EntryPoint="SetThreadInformation")]static extern bool SetMemory(IntPtr thread,int kind,ref Memory value,int size);
    public static void Foreground(bool inputThread){
        try{
            if(inputThread && Thread.CurrentThread.Priority<ThreadPriority.AboveNormal)Thread.CurrentThread.Priority=ThreadPriority.AboveNormal;
            var power=new Power{Version=1,ControlMask=1,StateMask=0};SetPower(GetCurrentThread(),3,ref power,12);
            var memory=new Memory{Priority=5};SetMemory(GetCurrentThread(),0,ref memory,4);
        }catch(EntryPointNotFoundException){}catch(System.ComponentModel.Win32Exception){}catch(UnauthorizedAccessException){}
    }
}
