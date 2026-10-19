#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <shellapi.h>
#include <string>
#include <vector>

// Transitional GUI launcher for the existing PowerShell agent. Not a rewrite
// of the application engine and not a self-contained single-file application.
int WINAPI wWinMain(HINSTANCE,HINSTANCE,PWSTR,int) {
    wchar_t path[32768]{};
    DWORD length=GetModuleFileNameW(nullptr,path,32768);
    if(!length || length>=32768)return 1;
    std::wstring executable(path,length);
    std::wstring root=executable.substr(0,executable.find_last_of(L"\\/"));
    std::wstring script=L"Agent.ps1",options=L" -Resume";
    bool console=false;
    int argc=0;LPWSTR* argv=CommandLineToArgvW(GetCommandLineW(),&argc);
    if(!argv)return 1;
    if(argc>2){LocalFree(argv);MessageBoxW(nullptr,L"Use: DeskPages.exe [--recover | --install | --uninstall | --check]",L"DeskPages",MB_ICONINFORMATION);return 2;}
    if(argc==2){
        std::wstring mode=argv[1];
        if(mode==L"--recover")options=L" -Recover";
        else if(mode==L"--install"){script=L"Install-Startup.ps1";options=L"";}
        else if(mode==L"--uninstall"){script=L"Uninstall-Startup.ps1";options=L"";}
        else if(mode==L"--check"){options=L" -SelfTest";console=true;}
        else {LocalFree(argv);MessageBoxW(nullptr,L"Use: DeskPages.exe [--recover | --install | --uninstall | --check]",L"DeskPages",MB_ICONINFORMATION);return 2;}
    }
    LocalFree(argv);
    std::wstring scriptPath=root+L"\\"+script;
    if(GetFileAttributesW(scriptPath.c_str())==INVALID_FILE_ATTRIBUTES){
        MessageBoxW(nullptr,L"DeskPages installation is incomplete. Run DeskPages Setup again.",L"DeskPages",MB_ICONERROR);return 1;
    }
    wchar_t system[32768]{};UINT n=GetSystemDirectoryW(system,32768);
    if(!n || n>=32768)return 1;
    std::wstring powershell=std::wstring(system,n)+L"\\WindowsPowerShell\\v1.0\\powershell.exe";
    std::wstring command=L"\""+powershell+L"\" -NoProfile -STA -ExecutionPolicy Bypass";
    if(!console)command+=L" -WindowStyle Hidden";
    command+=L" -File \""+scriptPath+L"\""+options;
    std::vector<wchar_t> buffer(command.begin(),command.end());buffer.push_back(0);
    STARTUPINFOW start{};start.cb=sizeof(start);start.dwFlags=STARTF_USESHOWWINDOW;start.wShowWindow=console?SW_SHOWNORMAL:SW_HIDE;
    PROCESS_INFORMATION child{};
    if(!CreateProcessW(powershell.c_str(),buffer.data(),nullptr,nullptr,FALSE,console?CREATE_NEW_CONSOLE:CREATE_NO_WINDOW,nullptr,root.c_str(),&start,&child)){
        std::wstring message=L"Could not start Windows PowerShell. Win32 error: "+std::to_wstring(GetLastError());
        MessageBoxW(nullptr,message.c_str(),L"DeskPages",MB_ICONERROR);return 1;
    }
    CloseHandle(child.hThread);
    WaitForSingleObject(child.hProcess,INFINITE);
    DWORD code=1;GetExitCodeProcess(child.hProcess,&code);CloseHandle(child.hProcess);
    if(code!=0){
        std::wstring message=L"DeskPages stopped with exit code "+std::to_wstring(code)+L". Use Check.cmd or the existing agent.log for the detailed error. Keep all saved data and recovery tools.";
        MessageBoxW(nullptr,message.c_str(),L"DeskPages",MB_ICONWARNING);
    }
    return static_cast<int>(code);
}
