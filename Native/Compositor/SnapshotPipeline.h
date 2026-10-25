#pragma once
#include "SnapshotCache.h"
#include <atomic>
#include <functional>
#include <map>
#include <condition_variable>
#include <thread>
#include <chrono>
namespace deskpages {
struct SnapshotArea {int left=0,top=0,width=0,height=0;};
inline std::wstring ResidentKey(const std::wstring& key,const SnapshotArea& a){return key+L"|"+std::to_wstring(a.left)+L","+std::to_wstring(a.top)+L","+std::to_wstring(a.width)+L","+std::to_wstring(a.height);}
// The navigation path uses only SnapshotCache and Known(). All filesystem,
// DPAPI, decompression, compression and idle captures run on this worker.
class SnapshotPipeline {
public:
 using Image=std::shared_ptr<const std::vector<unsigned char>>;
 using Load=std::function<Image(const std::wstring&,SnapshotArea)>;
 using Save=std::function<void(const std::wstring&,SnapshotArea,const std::vector<unsigned char>&)>;
 using Capture=std::function<Image(SnapshotArea)>;
private:
 struct Frame {std::wstring key;SnapshotArea area;Image pixels;uint64_t epoch=0,revision=0,serial=0;};
 SnapshotCache& cache;std::atomic<uint64_t>& epoch;
 std::function<uint64_t()> now;std::function<void(std::wstring,std::wstring)> configure;
 std::function<std::wstring(int)> known;Load load;Save save;Capture capture;std::function<void()> initialize;
 std::mutex mutex;std::condition_variable wake;std::thread worker;bool stop=false,configurePending=false,planPending=false,capturePending=false;
 std::wstring folder,scope;SnapshotArea area;std::vector<int> pages;
 std::map<int,std::wstring> keys;std::map<int,uint64_t> revisions;std::map<int,Frame> dirty;
 std::set<int> invalidPages;
 struct Deferred {Frame metadata;std::weak_ptr<const std::vector<unsigned char>> pixels;};std::map<int,Deferred> deferred;
 Frame requestedCapture;uint64_t requestSerial=0;size_t dirtyBytes=0;
 std::atomic<uint64_t> inputSerial{0},inputAt{0};
 std::atomic<bool> enabled{true};
 static int Page(const std::wstring& key){try{return std::stoi(key);}catch(...){return 0;}}
 void Start(){if(!worker.joinable()){stop=false;worker=std::thread([this]{Run();});}}
 void Run(){
  if(initialize)initialize();
  for(;;){
   Frame f;int operation=0;std::wstring root,stamp;SnapshotArea a;std::vector<int> order;uint64_t generation=0,serial=0,revision=0;
   {std::unique_lock<std::mutex> lock(mutex);wake.wait(lock,[this]{return stop||configurePending||!dirty.empty()||!deferred.empty()||capturePending||planPending;});
    if(stop && dirty.empty() && deferred.empty())break;
    if(configurePending){operation=1;root=folder;stamp=scope;configurePending=false;}
    else if(!dirty.empty()){operation=2;auto it=dirty.begin();f=std::move(it->second);dirtyBytes-=f.pixels->size();dirty.erase(it);}
    else if(!deferred.empty()){auto it=deferred.begin();f=it->second.metadata;f.pixels=it->second.pixels.lock();deferred.erase(it);if(f.pixels)operation=2;else ++errors;}
    else if(now()-inputAt.load()<200){wake.wait_for(lock,std::chrono::milliseconds(40));continue;}
    else if(capturePending){operation=3;f=requestedCapture;capturePending=false;serial=f.serial;revision=f.revision;}
    else if(planPending){operation=4;order=pages;a=area;generation=epoch.load();serial=inputSerial.load();planPending=false;}
   }
   try{
    if(operation==1)configure(root,stamp);
    else if(operation==2){bool valid;{std::lock_guard<std::mutex> lock(mutex);valid=f.epoch==epoch.load() && f.revision==revisions[Page(f.key)];}if(valid)save(f.key,f.area,*f.pixels);}
    else if(operation==3){
     {std::lock_guard<std::mutex> lock(mutex);if(!enabled || serial!=inputSerial.load() || f.epoch!=epoch.load() || revision!=revisions[Page(f.key)]){warmResult=-1;continue;}}
     auto image=capture(f.area);bool accepted=false;
     {std::lock_guard<std::mutex> lock(mutex);int page=Page(f.key);
      if(enabled && image && f.epoch==epoch.load() && revision==revisions[page] && serial==inputSerial.load()){
       accepted=cache.PutShared(ResidentKey(f.key,f.area),image,f.epoch,now());
       if(accepted){keys[page]=f.key;invalidPages.erase(page);f.pixels=image;QueueSave(f);++captures;}
      }
     }
     warmResult=accepted?2:-1;
    }else if(operation==4){
     const size_t frameBytes=size_t(a.width)*a.height*4;size_t admitted=0;
     for(int page:order){
      if(!frameBytes || admitted++>=cache.Limit()/frameBytes)break;
      if(!enabled || epoch.load()!=generation || inputSerial.load()!=serial){std::lock_guard<std::mutex> lock(mutex);planPending=enabled.load();break;}
      uint64_t rev;{std::lock_guard<std::mutex> lock(mutex);rev=revisions[page];if(invalidPages.count(page))continue;}
      auto key=known(page);if(key.empty())continue;
      {std::lock_guard<std::mutex> lock(mutex);if(revisions[page]!=rev || epoch.load()!=generation)continue;keys[page]=key;}
      auto resident=ResidentKey(key,a);if(cache.GetShared(resident,generation,now()))continue;
      auto image=load(key,a);if(!image)continue;
      std::lock_guard<std::mutex> lock(mutex);
      if(enabled && revisions[page]==rev && epoch.load()==generation && cache.PutShared(resident,image,generation,now()))++preloads;
     }
    }
   }catch(...){++errors;if(operation==3)warmResult=-1;}
  }
 }
 void QueueSave(const Frame& f){
  int page=Page(f.key);auto previous=dirty.find(page);size_t prior=previous==dirty.end()?0:previous->second.pixels->size();
  // Raw persistence backlog has a separate, explicit byte cap. Navigation
  // never waits for it. Normal captures coalesce by page, retaining latest data.
  if(dirtyBytes-prior+f.pixels->size()>cache.Limit()){++deferredWrites;Deferred d;d.metadata=f;d.metadata.pixels.reset();d.pixels=f.pixels;deferred[page]=std::move(d);wake.notify_one();return;}
  deferred.erase(page);
  dirtyBytes=dirtyBytes-prior+f.pixels->size();dirty[page]=f;wake.notify_one();
 }
public:
 std::atomic<int> warmResult{0};std::atomic<uint64_t> preloads{0},captures{0},errors{0},deferredWrites{0};
 SnapshotPipeline(SnapshotCache& c,std::atomic<uint64_t>& e,std::function<uint64_t()> clock,
  std::function<void(std::wstring,std::wstring)> config,std::function<std::wstring(int)> lookup,Load read,Save write,Capture shot,std::function<void()> init={}):
  cache(c),epoch(e),now(clock),configure(config),known(lookup),load(read),save(write),capture(shot),initialize(init){}
 ~SnapshotPipeline(){Stop();}
 void Configure(std::wstring root,std::wstring stamp){std::lock_guard<std::mutex> lock(mutex);enabled=true;folder=root;scope=stamp;configurePending=true;keys.clear();revisions.clear();invalidPages.clear();dirty.clear();deferred.clear();dirtyBytes=0;capturePending=false;warmResult=0;Start();wake.notify_one();}
 void Suspend(){std::lock_guard<std::mutex> lock(mutex);enabled=false;++inputSerial;planPending=capturePending=false;keys.clear();warmResult=-1;wake.notify_one();}
 void Input(){inputAt=now();++inputSerial;wake.notify_one();}
 void Prime(std::vector<int> order,SnapshotArea a){
  std::lock_guard<std::mutex> lock(mutex);if(!enabled)return;pages=std::move(order);area=a;planPending=true;
  cache.Protect(std::vector<int>(pages.begin(),pages.begin()+std::min<size_t>(3,pages.size())));Start();wake.notify_one();
 }
 std::wstring Known(int page){std::lock_guard<std::mutex> lock(mutex);auto it=keys.find(page);return it==keys.end()?L"":it->second;}
 void Invalidate(int page){std::lock_guard<std::mutex> lock(mutex);++revisions[page];invalidPages.insert(page);keys.erase(page);cache.RemovePrefix(std::to_wstring(page)+L":");}
 bool Keep(std::wstring key,SnapshotArea a,Image pixels,uint64_t generation){
  std::lock_guard<std::mutex> lock(mutex);if(!enabled || generation!=epoch.load() || !pixels)return false;
  int page=Page(key);if(!cache.PutShared(ResidentKey(key,a),pixels,generation,now())){++errors;return false;}
  cache.RemovePrefix(std::to_wstring(page)+L":",ResidentKey(key,a));keys[page]=key;invalidPages.erase(page);
  Frame f;f.key=key;f.area=a;f.pixels=pixels;f.epoch=generation;f.revision=revisions[page];QueueSave(f);Start();return true;
 }
 bool Warm(std::wstring key,SnapshotArea a){std::lock_guard<std::mutex> lock(mutex);if(!enabled || warmResult.load()==1)return false;requestedCapture={};requestedCapture.key=key;requestedCapture.area=a;requestedCapture.epoch=epoch.load();requestedCapture.revision=revisions[Page(key)];requestedCapture.serial=inputSerial.load();capturePending=true;++requestSerial;warmResult=1;Start();wake.notify_one();return true;}
 void Stop(){ {std::lock_guard<std::mutex> lock(mutex);stop=true;capturePending=planPending=false;wake.notify_all();}if(worker.joinable())worker.join();}
};
}
