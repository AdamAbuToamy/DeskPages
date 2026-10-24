#pragma once
#include <wincrypt.h>
#include <compressapi.h>
#include <map>
#include <mutex>
#include <condition_variable>
#include <thread>
#include <algorithm>
// Windows user-bound, authenticated ciphertext; never plaintext image files.
// A bounded worker compresses/encrypts/writes after capture, away from navigation.
namespace deskpages {
class SnapshotDisk {
 struct Frame {std::wstring key,scope;int left=0,top=0,width=0,height=0;std::vector<unsigned char> pixels;uint64_t generation=0;};
 struct File {HANDLE handle=INVALID_HANDLE_VALUE;std::wstring key,scope;};
 std::mutex mutex;std::condition_variable wake;std::thread thread;bool stopping=false;
 std::map<int,Frame> pending;std::shared_ptr<Frame> writing;std::map<int,File> files;std::wstring root,scope;uint64_t generation=0;HANDLE directory=INVALID_HANDLE_VALUE;
 static constexpr size_t MaxRaw=160000000,MaxFile=200000000;
 static int Page(const std::wstring& key){auto n=key.find(L':');if(n==std::wstring::npos || !n || n>6)return 0;int id=0;for(size_t i=0;i<n;i++){if(key[i]<L'0'||key[i]>L'9')return 0;id=id*10+key[i]-L'0';}return id>0&&id<=99?id:0;}
 std::wstring Path(int id)const{return root+L"\\page-"+std::to_wstring(id)+L".dpc";}
 template<class T>static void Add(std::vector<unsigned char>& b,const T& v){auto p=reinterpret_cast<const unsigned char*>(&v);b.insert(b.end(),p,p+sizeof(v));}
 template<class T>static T Take(const std::vector<unsigned char>& b,size_t& at){if(at+sizeof(T)>b.size())throw std::runtime_error("Snapshot header truncated");T v;std::memcpy(&v,b.data()+at,sizeof(v));at+=sizeof(v);return v;}
 static void Text(std::vector<unsigned char>& b,const std::wstring& s){Add(b,uint32_t(s.size()));auto p=reinterpret_cast<const unsigned char*>(s.data());b.insert(b.end(),p,p+s.size()*sizeof(wchar_t));}
 static std::wstring Text(const std::vector<unsigned char>& b,size_t& at){auto n=Take<uint32_t>(b,at);if(n>1024 || at+size_t(n)*2>b.size())throw std::runtime_error("Snapshot text invalid");std::wstring s(n,L'\0');std::memcpy(&s[0],b.data()+at,n*2);at+=n*2;return s;}
 static std::vector<unsigned char> Crypt(const std::vector<unsigned char>& bytes,bool protect){
  DATA_BLOB in{DWORD(bytes.size()),const_cast<BYTE*>(bytes.data())},out{};BOOL ok=protect?CryptProtectData(&in,L"DeskPages private page snapshot",nullptr,nullptr,nullptr,CRYPTPROTECT_UI_FORBIDDEN,&out):CryptUnprotectData(&in,nullptr,nullptr,nullptr,nullptr,CRYPTPROTECT_UI_FORBIDDEN,&out);
  if(!ok)throw std::runtime_error("Windows snapshot protection failed");
  std::vector<unsigned char> result(out.pbData,out.pbData+out.cbData);SecureZeroMemory(out.pbData,out.cbData);LocalFree(out.pbData);return result;
 }
 static std::vector<unsigned char> Pack(const Frame& f){
  COMPRESSOR_HANDLE compressor=nullptr;if(!CreateCompressor(COMPRESS_ALGORITHM_XPRESS_HUFF,nullptr,&compressor))throw std::runtime_error("Create snapshot compressor failed");
  SIZE_T needed=0;Compress(compressor,const_cast<unsigned char*>(f.pixels.data()),f.pixels.size(),nullptr,0,&needed);
  std::vector<unsigned char> packed(needed);bool ok=needed && Compress(compressor,const_cast<unsigned char*>(f.pixels.data()),f.pixels.size(),packed.data(),packed.size(),&needed);CloseCompressor(compressor);if(!ok)throw std::runtime_error("Snapshot compression failed");packed.resize(needed);
  std::vector<unsigned char> b;Add(b,uint64_t(0x4450534e41503032ull));Text(b,f.key);Text(b,f.scope);Add(b,f.left);Add(b,f.top);Add(b,f.width);Add(b,f.height);Add(b,uint64_t(f.pixels.size()));b.insert(b.end(),packed.begin(),packed.end());return Crypt(b,true);
 }
 static Frame Unpack(const std::vector<unsigned char>& cipher,bool pixels){
  auto b=Crypt(cipher,false);size_t at=0;if(Take<uint64_t>(b,at)!=0x4450534e41503032ull)throw std::runtime_error("Snapshot format invalid");
  Frame f;f.key=Text(b,at);f.scope=Text(b,at);f.left=Take<int>(b,at);f.top=Take<int>(b,at);f.width=Take<int>(b,at);f.height=Take<int>(b,at);auto raw=Take<uint64_t>(b,at);
  if(!Page(f.key)||f.width<=0||f.height<=0||uint64_t(f.width)*f.height>40000000||raw!=uint64_t(f.width)*f.height*4||raw>MaxRaw||at>=b.size())throw std::runtime_error("Snapshot dimensions invalid");
  if(pixels){DECOMPRESSOR_HANDLE d=nullptr;if(!CreateDecompressor(COMPRESS_ALGORITHM_XPRESS_HUFF,nullptr,&d))throw std::runtime_error("Create snapshot decompressor failed");f.pixels.resize(size_t(raw));SIZE_T actual=0;bool ok=Decompress(d,b.data()+at,b.size()-at,f.pixels.data(),f.pixels.size(),&actual);CloseDecompressor(d);if(!ok||actual!=raw)throw std::runtime_error("Snapshot decompression failed");}
  SecureZeroMemory(b.data(),b.size());return f;
 }
 std::vector<unsigned char> ReadLocked(int page){auto it=files.find(page);if(it==files.end())return {};auto h=it->second.handle;LARGE_INTEGER size{};if(!GetFileSizeEx(h,&size)||size.QuadPart<=0||uint64_t(size.QuadPart)>MaxFile)return {};SetFilePointer(h,0,nullptr,FILE_BEGIN);std::vector<unsigned char> bytes(size_t(size.QuadPart));DWORD got=0;if(!ReadFile(h,bytes.data(),DWORD(bytes.size()),&got,nullptr)||got!=bytes.size())return {};return bytes;}
 void Write(Frame& f){
  auto bytes=Pack(f);std::unique_lock<std::mutex> lock(mutex);if(f.generation!=generation||f.scope!=scope)return;
  int page=Page(f.key);auto path=Path(page),temp=path+L".tmp";HANDLE h=CreateFileW(temp.c_str(),GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_HIDDEN|FILE_ATTRIBUTE_SYSTEM,nullptr);if(h==INVALID_HANDLE_VALUE)throw std::runtime_error("Create protected snapshot failed");
  DWORD wrote=0;bool ok=WriteFile(h,bytes.data(),DWORD(bytes.size()),&wrote,nullptr)&&wrote==bytes.size()&&FlushFileBuffers(h);CloseHandle(h);if(!ok){DeleteFileW(temp.c_str());throw std::runtime_error("Write snapshot failed");}
  auto old=files.find(page);if(old!=files.end()){CloseHandle(old->second.handle);files.erase(old);}
  SetFileAttributesW(path.c_str(),FILE_ATTRIBUTE_NORMAL);
  if(!MoveFileExW(temp.c_str(),path.c_str(),MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH)){
   SetFileAttributesW(path.c_str(),FILE_ATTRIBUTE_HIDDEN|FILE_ATTRIBUTE_SYSTEM|FILE_ATTRIBUTE_READONLY);HANDLE prior=CreateFileW(path.c_str(),GENERIC_READ,0,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);if(prior!=INVALID_HANDLE_VALUE)files[page]={prior,L"",L""};DeleteFileW(temp.c_str());throw std::runtime_error("Commit snapshot failed");
  }
  SetFileAttributesW(path.c_str(),FILE_ATTRIBUTE_HIDDEN|FILE_ATTRIBUTE_SYSTEM|FILE_ATTRIBUTE_READONLY);h=CreateFileW(path.c_str(),GENERIC_READ,0,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);if(h==INVALID_HANDLE_VALUE)throw std::runtime_error("Lock snapshot failed");files[page]={h,f.key,f.scope};++writes;
 }
 void Run(){SetThreadPriority(GetCurrentThread(),THREAD_MODE_BACKGROUND_BEGIN);for(;;){std::shared_ptr<Frame> f;{std::unique_lock<std::mutex> lock(mutex);wake.wait(lock,[this]{return stopping||!pending.empty();});if(stopping&&pending.empty())break;auto it=pending.begin();f=std::make_shared<Frame>(std::move(it->second));writing=f;pending.erase(it);wake.notify_all();}try{Write(*f);}catch(...){++errors;} {std::lock_guard<std::mutex> lock(mutex);if(writing==f)writing.reset();wake.notify_all();}}}
public:
 std::atomic<uint64_t> hits{0},misses{0},writes{0},errors{0};
 ~SnapshotDisk(){Stop();}
 void Configure(const std::wstring& folder,const std::wstring& stamp){
  std::lock_guard<std::mutex> lock(mutex);if(folder.empty()||stamp.empty())return;
  if(root.empty()){root=folder;CreateDirectoryW(root.c_str(),nullptr);directory=CreateFileW(root.c_str(),FILE_LIST_DIRECTORY,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_EXISTING,FILE_FLAG_BACKUP_SEMANTICS,nullptr);if(directory==INVALID_HANDLE_VALUE){root.clear();throw std::runtime_error("Cannot lock snapshot directory");}
   WIN32_FIND_DATAW data{};HANDLE scan=FindFirstFileW((root+L"\\page-*.dpc").c_str(),&data);if(scan!=INVALID_HANDLE_VALUE){do{try{std::wstring name=data.cFileName;size_t end=0;int id=std::stoi(name.substr(5),&end);if(id<=0||id>99||name.substr(5+end)!=L".dpc")continue;auto path=Path(id);HANDLE h=CreateFileW(path.c_str(),GENERIC_READ,0,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);if(h!=INVALID_HANDLE_VALUE)files[id]={h,L"",L""};}catch(...){}}while(FindNextFileW(scan,&data));FindClose(scan);}}
  if(scope!=stamp){scope=stamp;++generation;pending.clear();}
  if(!thread.joinable()){stopping=false;thread=std::thread([this]{Run();});}
 }
 void Save(const std::wstring& key,int l,int t,int w,int h,const std::vector<unsigned char>& pixels){
  if(!Page(key)||pixels.empty()||pixels.size()>MaxRaw)return;
  std::unique_lock<std::mutex> lock(mutex);if(root.empty()||stopping)return;
  // Bound queued raw images; coalesce multiple updates of the same page.
  wake.wait(lock,[this,&key]{return stopping || pending.size()<4 || pending.find(Page(key))!=pending.end();});if(stopping)return;
  Frame f;f.key=key;f.scope=scope;f.left=l;f.top=t;f.width=w;f.height=h;f.pixels=pixels;f.generation=generation;pending[Page(key)]=std::move(f);wake.notify_one();
 }
 std::wstring Known(int page){
  try{std::vector<unsigned char> bytes;std::wstring stamp;{std::lock_guard<std::mutex> lock(mutex);auto queued=pending.find(page);if(queued!=pending.end() && queued->second.scope==scope)return queued->second.key;if(writing && Page(writing->key)==page && writing->scope==scope)return writing->key;auto it=files.find(page);if(it==files.end())return L"";stamp=scope;if(!it->second.key.empty())return it->second.scope==stamp?it->second.key:L"";bytes=ReadLocked(page);}if(bytes.empty())return L"";auto f=Unpack(bytes,false);std::lock_guard<std::mutex> lock(mutex);if(scope!=stamp)return L"";auto it=files.find(page);if(it!=files.end()){if(!it->second.key.empty())return it->second.scope==stamp?it->second.key:L"";it->second.key=f.key;it->second.scope=f.scope;}return f.scope==stamp?f.key:L"";}catch(...){++errors;return L"";}
 }
 bool Get(const std::wstring& key,int l,int t,int w,int h,std::vector<unsigned char>& pixels){
  try{std::vector<unsigned char> bytes;std::wstring stamp;uint64_t version;{std::lock_guard<std::mutex> lock(mutex);stamp=scope;version=generation;
   const Frame* ready=nullptr;auto queued=pending.find(Page(key));if(queued!=pending.end())ready=&queued->second;else if(writing && Page(writing->key)==Page(key))ready=writing.get();
   if(ready && ready->scope==scope && ready->key==key && ready->left==l && ready->top==t && ready->width==w && ready->height==h){pixels=ready->pixels;++hits;return true;}
   bytes=ReadLocked(Page(key));}if(bytes.empty()){++misses;return false;}auto f=Unpack(bytes,true);{std::lock_guard<std::mutex> lock(mutex);if(version!=generation||f.scope!=stamp||f.key!=key||f.left!=l||f.top!=t||f.width!=w||f.height!=h){++misses;return false;}}pixels=std::move(f.pixels);++hits;return true;}catch(...){++errors;++misses;return false;}
 }
 void Stop(){ {std::lock_guard<std::mutex> lock(mutex);stopping=true;wake.notify_all();}if(thread.joinable())thread.join();std::lock_guard<std::mutex> lock(mutex);for(auto& p:files)CloseHandle(p.second.handle);files.clear();if(directory!=INVALID_HANDLE_VALUE){CloseHandle(directory);directory=INVALID_HANDLE_VALUE;}root.clear();scope.clear();}
};
}
