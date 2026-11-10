#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#define _WIN32_WINNT 0x0A00
#include <windows.h>
#include <vector>
#include <string>
#include <atomic>
#include <cstdint>
#include <cstring>
#include <memory>
#include <stdexcept>
#include <iostream>
#include "SnapshotDisk.h"
static void Require(bool ok,const char* why){if(!ok)throw std::runtime_error(why);}
int wmain(int argc,wchar_t** argv){
 if(argc==3 && std::wstring(argv[1])==L"--restore"){
  try{deskpages::SnapshotDisk reopened;reopened.Configure(argv[2],L"scope-a");std::vector<unsigned char> pixels;
   Require(reopened.Known(1)==L"1:layout-a" && reopened.Get(L"1:layout-a",-128,0,64,64,pixels),"Fresh process could not restore aged snapshot");
   for(size_t i=0;i<pixels.size();i++)Require(pixels[i]==static_cast<unsigned char>(i*17+13),"Aged disk pixels changed");
   reopened.Stop();return 0;
  }catch(...){return 1;}
 }
 wchar_t temp[MAX_PATH]{};GetTempPathW(MAX_PATH,temp);std::wstring root=std::wstring(temp)+L"DeskPages-cache-test-"+std::to_wstring(GetCurrentProcessId());CreateDirectoryW(root.c_str(),nullptr);try{
 std::vector<unsigned char> pixels(64*64*4);for(size_t i=0;i<pixels.size();i++)pixels[i]=static_cast<unsigned char>(i*17+13);
 deskpages::SnapshotDisk disk;disk.Configure(root,L"scope-a");disk.Save(L"1:layout-a",-128,0,64,64,pixels);
 for(int i=0;i<500 && disk.writes.load()==0 && disk.errors.load()==0;i++)Sleep(10);
 Require(disk.writes.load()>0,"Encrypted asynchronous write did not finish");Require(disk.errors.load()==0,"Snapshot I/O error");
 std::vector<unsigned char> actual;Require(disk.Get(L"1:layout-a",-128,0,64,64,actual)&&actual==pixels,"Protected snapshot round trip differs");Require(disk.Known(1)==L"1:layout-a","Saved identity not available");
 std::wstring path=root+L"\\page-1.dpc";HANDLE opened=CreateFileW(path.c_str(),GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,nullptr,OPEN_EXISTING,0,nullptr);Require(opened==INVALID_HANDLE_VALUE,"Snapshot was openable while locked");Require(!DeleteFileW(path.c_str()),"Locked snapshot was deletable");
 for(int page=2;page<=9;page++)disk.Save(std::to_wstring(page)+L":layout-a",-128,0,64,64,pixels);
 disk.Stop();
 SetFileAttributesW(path.c_str(),FILE_ATTRIBUTE_NORMAL);
 HANDLE aged=CreateFileW(path.c_str(),FILE_WRITE_ATTRIBUTES,0,nullptr,OPEN_EXISTING,0,nullptr);Require(aged!=INVALID_HANDLE_VALUE,"Cannot age fixture");
 FILETIME time;GetSystemTimeAsFileTime(&time);ULARGE_INTEGER ticks;ticks.LowPart=time.dwLowDateTime;ticks.HighPart=time.dwHighDateTime;ticks.QuadPart-=366ull*24*60*60*10000000;
 time.dwLowDateTime=ticks.LowPart;time.dwHighDateTime=ticks.HighPart;bool dated=SetFileTime(aged,&time,nullptr,&time)!=FALSE;CloseHandle(aged);Require(dated,"Cannot set year-old timestamp");
 wchar_t exe[MAX_PATH]{};GetModuleFileNameW(nullptr,exe,MAX_PATH);std::wstring command=L"\""+std::wstring(exe)+L"\" --restore \""+root+L"\"";
 STARTUPINFOW startup{};startup.cb=sizeof(startup);PROCESS_INFORMATION child{};Require(CreateProcessW(exe,command.data(),nullptr,nullptr,FALSE,CREATE_NO_WINDOW,nullptr,nullptr,&startup,&child)!=FALSE,"Cannot start restoration fixture");
 DWORD wait=WaitForSingleObject(child.hProcess,10000),code=1;if(wait!=WAIT_OBJECT_0){TerminateProcess(child.hProcess,1);WaitForSingleObject(child.hProcess,5000);}GetExitCodeProcess(child.hProcess,&code);CloseHandle(child.hProcess);CloseHandle(child.hThread);Require(wait==WAIT_OBJECT_0 && code==0,"Fresh process failed to restore year-old disk record without capture");
 disk.Configure(root,L"scope-a");for(int page=2;page<=9;page++)Require(disk.Get(std::to_wstring(page)+L":layout-a",-128,0,64,64,actual)&&actual==pixels,"Queued page lost after shutdown/reopen");Require(disk.Known(1)==L"1:layout-a","Identity not restored after reopening");Require(disk.Get(L"1:layout-a",-128,0,64,64,actual)&&actual==pixels,"Persisted frame not restored after reopening");
 Require(!disk.Get(L"1:different-layout",-128,0,64,64,actual),"Different layout reused old image");Require(!disk.Get(L"1:layout-a",0,0,64,64,actual),"Different monitor geometry reused old image");
 disk.Configure(root,L"scope-b");Require(disk.Known(1).empty()&&!disk.Get(L"1:layout-a",-128,0,64,64,actual),"Changed scene reused old image");disk.Stop();
 SetFileAttributesW(path.c_str(),FILE_ATTRIBUTE_NORMAL);HANDLE h=CreateFileW(path.c_str(),GENERIC_WRITE,0,nullptr,OPEN_EXISTING,0,nullptr);Require(h!=INVALID_HANDLE_VALUE,"Cannot build corruption fixture");unsigned char junk[16]{};DWORD wrote;WriteFile(h,junk,sizeof(junk),&wrote,nullptr);CloseHandle(h);
 disk.Configure(root,L"scope-a");Require(disk.Known(1).empty()&&!disk.Get(L"1:layout-a",-128,0,64,64,actual),"Tampered ciphertext accepted");disk.Stop();SetFileAttributesW(path.c_str(),FILE_ATTRIBUTE_NORMAL);DeleteFileW(path.c_str());for(int page=2;page<=9;page++){auto p=root+L"\\page-"+std::to_wstring(page)+L".dpc";SetFileAttributesW(p.c_str(),FILE_ATTRIBUTE_NORMAL);DeleteFileW(p.c_str());}RemoveDirectoryW(root.c_str());
 std::cout<<"PASS: Windows compression + current-user DPAPI round trip, asynchronous write, exclusive file open/delete protection, fresh-process restoration of a 366-day-old file without capture, layout/scene/geometry rejection and corrupted-ciphertext rejection. No desktop screenshot captured.\n";return 0;
}catch(const std::exception& e){std::cerr<<"FAIL: "<<e.what()<<"\n";return 1;}}
