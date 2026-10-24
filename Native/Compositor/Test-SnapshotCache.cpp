#include "SnapshotCache.h"
#include "PreviewGate.h"
#include <cassert>
#include <iostream>
int main(){using deskpages::SnapshotCache;std::vector<unsigned char> out;
 SnapshotCache c(12,2,100);assert(c.Put(L"page1",{1,2,3,4},1,0));assert(c.Get(L"page1",1,1,out)&&out[0]==1);
 assert(!c.Get(L"different-layout",1,1,out));assert(c.Put(L"page2",{5,6,7,8},1,2));assert(c.Get(L"page1",1,3,out));
 assert(c.Put(L"page3",{9,10,11,12},1,4));assert(!c.Get(L"page2",1,5,out));assert(c.Bytes()==8&&c.Count()==2);
 assert(!c.Get(L"page1",2,6,out)&&c.Bytes()==0);assert(c.Put(L"page1",{1,2,3,4},2,10));assert(!c.Get(L"page1",2,110,out));
 assert(!c.Put(L"huge",std::vector<unsigned char>(13),2,111));assert(c.Bytes()==0);
 assert(c.Put(L"page1",{1,2,3,4},2,112));assert(c.Put(L"page1",{8,8},2,113));assert(c.Bytes()==2&&c.Count()==1);
 c.Clear();assert(c.Bytes()==0&&c.Count()==0);
 SnapshotCache four;for(int i=0;i<8;i++)assert(four.Put(std::to_wstring(i),std::vector<unsigned char>(3840*2160*4),1,i));
 assert(four.Bytes()<=96ull*1024*1024 && four.Count()==3);
 SnapshotCache persistent(12,2);assert(persistent.Put(L"ready",{1,2,3,4},1,0));assert(persistent.Get(L"ready",1,3600000,out));assert(!persistent.Get(L"ready",2,3600001,out));
 deskpages::PreviewGate gate;assert(gate.CanRetire());gate.Begin(3);assert(!gate.CanRetire());
 gate.Confirm(3,true);assert(gate.valid && !gate.CanRetire());gate.Complete();assert(gate.CanRetire());
 gate.Begin(3);gate.Confirm(4,true);assert(!gate.valid && !gate.CanRetire());gate.Complete();assert(gate.CanRetire());
 gate.Begin(3);gate.Confirm(3,false);assert(!gate.valid);gate.Reset();assert(!gate.active && gate.CanRetire());
 // A changed image/key arriving before the animation fence must never cut
 // motion. A slow desktop must keep its cover after motion, until verified.
 long handoffs=0;
 for(uint64_t ready=0;ready<1500;ready+=3)for(bool same:{false,true})for(uint64_t now=0;now<1500;now+=5){
  gate.Begin(9);gate.Confirm(same?9:10,same);
  if(now>=ready)gate.Complete();
  assert(gate.CanRetire(now,440)==(now>=ready && now>=440));handoffs++;
 }
 gate.Reset();assert(!gate.CanRetire(379,380) && gate.CanRetire(380,380));
 SnapshotCache selective(64,8);assert(selective.Put(L"1:old|explorer|bounds",{1,2},1,0));assert(selective.Put(L"2:kept|explorer|bounds",{3,4},1,0));assert(selective.Put(L"12:kept|explorer|bounds",{5,6},1,0));
 selective.RemovePrefix(L"1:");assert(!selective.Get(L"1:old|explorer|bounds",1,1,out));assert(selective.Get(L"2:kept|explorer|bounds",1,1,out));assert(selective.Get(L"12:kept|explorer|bounds",1,1,out));
 assert(selective.Put(L"2:new|explorer|bounds",{9,10},1,2));selective.RemovePrefix(L"2:",L"2:new|explorer|bounds");assert(!selective.Get(L"2:kept|explorer|bounds",1,3,out));assert(selective.Get(L"2:new|explorer|bounds",1,3,out));
 std::cout<<"PASS: hits, changed-layout miss, LRU, epochs, TTL, oversized frame rejection, replacement, clear 4K byte cap, unchanged-image retention, "<<handoffs<<" fast/slow/changed-image handoff timings, selective page removal and replacement preserving other pages.\n";
}
