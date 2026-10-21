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
int main(){wchar_t temp[MAX_PATH]{};GetTempPathW(MAX_PATH,temp);std::wstring root=std::wstring(temp)+L"DeskPages-cache-test-"+std::to_wstring(GetCurrentProcessId());CreateDirectoryW(root.c_str(),nullptr);try{
 std::vector<unsigned char> pixels(64*64*4);for(size_t i=0;i<pixels.size();i++)pixels[i]=static_cast<unsigned char>(i*17+13);
 deskpages::SnapshotDisk disk;disk.Configure(root,L"scope-a");disk.Save(L"1:layout-a",-128,0,64,64,pixels);
 for(int i=0;i<500 && disk.writes.load()==0 && disk.errors.load()==0;i++)Sleep(10);
 Require(disk.writes.load()>0,"Encrypted asynchronous write did not finish");Require(disk.errors.load()==0,"Snapshot I/O error");
 std::vector<unsigned char> actual;Require(disk.Get(L"1:layout-a",-128,0,64,64,actual)&&actual==pixels,"Protected snapshot round trip differs");Require(disk.Known(1)==L"1:layout-a","Saved identity not available");
 std::wstring path=root+L"\\page-1.dpc";HANDLE opened=CreateFileW(path.c_str(),GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,nullptr,OPEN_EXISTING,0,nullptr);Require(opened==INVALID_HANDLE_VALUE,"Snapshot was openable while locked");Require(!DeleteFileW(path.c_str()),"Locked snapshot was deletable");
 for(int page=2;page<=9;page++)disk.Save(std::to_wstring(page)+L":layout-a",-128,0,64,64,pixels);
 disk.Stop();disk.Configure(root,L"scope-a");for(int page=2;page<=9;page++)Require(disk.Get(std::to_wstring(page)+L":layout-a",-128,0,64,64,actual)&&actual==pixels,"Queued page lost after shutdown/reopen");Require(disk.Known(1)==L"1:layout-a","Identity not restored after reopening");Require(disk.Get(L"1:layout-a",-128,0,64,64,actual)&&actual==pixels,"Persisted frame not restored after reopening");
 Require(!disk.Get(L"1:different-layout",-128,0,64,64,actual),"Different layout reused old image");Require(!disk.Get(L"1:layout-a",0,0,64,64,actual),"Different monitor geometry reused old image");
 disk.Configure(root,L"scope-b");Require(disk.Known(1).empty()&&!disk.Get(L"1:layout-a",-128,0,64,64,actual),"Changed scene reused old image");disk.Stop();
 SetFileAttributesW(path.c_str(),FILE_ATTRIBUTE_NORMAL);HANDLE h=CreateFileW(path.c_str(),GENERIC_WRITE,0,nullptr,OPEN_EXISTING,0,nullptr);Require(h!=INVALID_HANDLE_VALUE,"Cannot build corruption fixture");unsigned char junk[16]{};DWORD wrote;WriteFile(h,junk,sizeof(junk),&wrote,nullptr);CloseHandle(h);
 disk.Configure(root,L"scope-a");Require(disk.Known(1).empty()&&!disk.Get(L"1:layout-a",-128,0,64,64,actual),"Tampered ciphertext accepted");disk.Stop();SetFileAttributesW(path.c_str(),FILE_ATTRIBUTE_NORMAL);DeleteFileW(path.c_str());for(int page=2;page<=9;page++){auto p=root+L"\\page-"+std::to_wstring(page)+L".dpc";SetFileAttributesW(p.c_str(),FILE_ATTRIBUTE_NORMAL);DeleteFileW(p.c_str());}RemoveDirectoryW(root.c_str());
 std::cout<<"PASS: Windows compression + current-user DPAPI round trip, asynchronous write, exclusive file open/delete protection, restart restoration, layout/scene/geometry rejection and corrupted-ciphertext rejection. No desktop screenshot captured.\n";return 0;
}catch(const std::exception& e){std::cerr<<"FAIL: "<<e.what()<<"\n";return 1;}}
