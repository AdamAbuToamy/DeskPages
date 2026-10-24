#include "SnapshotPipeline.h"
#include "SnapshotScenePolicy.h"
#include <cassert>
#include <iostream>
using namespace deskpages;
static uint64_t Clock(){return uint64_t(std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now().time_since_epoch()).count());}
template<class F>void Wait(F ready){auto end=Clock()+5000;while(!ready() && Clock()<end)std::this_thread::sleep_for(std::chrono::milliseconds(2));assert(ready());}
int main(){
 SnapshotCache cache(4096,99);std::atomic<uint64_t> epoch{1};std::atomic<int> saves{0},reads{0};std::atomic<bool> reading{false},releaseRead{false},shooting{false},releaseShot{false};auto foreground=std::this_thread::get_id();
 SnapshotArea area{0,0,2,2};
 SnapshotPipeline pipe(cache,epoch,Clock,[](std::wstring,std::wstring){},[](int page){return std::to_wstring(page)+L":layout:stamp";},
  [&](const std::wstring& key,SnapshotArea)->SnapshotPipeline::Image{assert(std::this_thread::get_id()!=foreground);++reads;if(key.find(L"8:")==0){reading=true;while(!releaseRead)std::this_thread::yield();}return std::make_shared<const std::vector<unsigned char>>(16,42);},
  [&](const std::wstring&,SnapshotArea,const std::vector<unsigned char>&){assert(std::this_thread::get_id()!=foreground);++saves;},
  [&](SnapshotArea)->SnapshotPipeline::Image{assert(std::this_thread::get_id()!=foreground);shooting=true;while(!releaseShot)std::this_thread::yield();return std::make_shared<const std::vector<unsigned char>>(16,91);});
 pipe.Configure(L"test",L"scope");pipe.Prime({1,2,3},area);
 Wait([&]{return pipe.preloads.load()==3;});
 auto key=ResidentKey(L"1:layout:stamp",area);auto original=cache.GetShared(key,1,Clock());assert(original && (*original)[0]==42);
 pipe.Prime({1,2,3,8},area);Wait([&]{return reading.load();});
 // Deliberately stalled disk decoder must not hold the navigation RAM lock.
 int before=reads;auto start=Clock();for(int i=0;i<20000;i++)assert(cache.GetShared(key,1,Clock())==original);assert(Clock()-start<2000 && reads==before);
 pipe.Input();releaseRead=true;
 assert(pipe.Warm(L"1:new:stamp",area));Wait([&]{return shooting.load();});
 pipe.Input();releaseShot=true;Wait([&]{return pipe.warmResult.load()==-1;});assert(cache.GetShared(key,1,Clock())==original);
 pipe.Invalidate(2);assert(pipe.Known(2).empty());assert(cache.GetShared(key,1,Clock())==original);
 assert(cache.GetShared(ResidentKey(L"3:layout:stamp",area),1,Clock()));
 auto fresh=std::make_shared<const std::vector<unsigned char>>(16,77);
 for(int i=0;i<1000;i++)assert(pipe.Keep(L"2:new:stamp",area,fresh,1));
 pipe.Stop();assert(saves>0);assert(pipe.deferredWrites==0);
 // Epochs must not rewind when an old background result arrives late.
 assert(cache.Put(L"fresh",{1,2},2,Clock()));assert(!cache.Put(L"late",{3,4},1,Clock()));assert(cache.GetShared(L"fresh",2,Clock()));
 SnapshotCache pinned(64,99);pinned.Protect({1,2,3});for(int p=1;p<=3;p++)assert(pinned.Put(std::to_wstring(p)+L":hot",std::vector<unsigned char>(16),1,0));
 for(int p=4;p<1000;p++)assert(pinned.Put(std::to_wstring(p)+L":cold",std::vector<unsigned char>(16),1,0));
 for(int p=1;p<=3;p++)assert(pinned.GetShared(std::to_wstring(p)+L":hot",1,3600000));
 assert(pinned.Bytes()<=64);
 for(int i=0;i<100000;i++){assert(!ScenePowerEvent(0xa));assert(!ScenePowerEvent(0x8013));}assert(ScenePowerEvent(4)&&ScenePowerEvent(7)&&ScenePowerEvent(0x12));
 std::cout<<"PASS: background-only disk/capture callbacks, 20,000 RAM hits during a stalled read, input-cancelled capture, 1,000 coalesced persistence requests, selective invalidation, stale epoch rejection, protected neighbours under eviction pressure, and 100,000 ordinary power broadcasts preserving cache.\n";
}
