#pragma once
#include <vector>
#include <string>
#include <cstdint>
#include <utility>
namespace deskpages {
// Worker-thread owned. Retained CPU bytes only; DWM/GPU surfaces are separate.
class SnapshotCache {
    struct Entry {std::wstring key;std::vector<unsigned char> pixels;uint64_t at,used;};
    std::vector<Entry> entries;uint64_t epoch=0,clock=0;size_t bytes=0;
    size_t limit;unsigned maxPages;uint64_t lifetime;
    void Remove(size_t i){bytes-=entries[i].pixels.size();entries.erase(entries.begin()+i);}
    void Sync(uint64_t value){if(value!=epoch){Clear();epoch=value;}}
public:
    explicit SnapshotCache(size_t budget=96ull*1024*1024,unsigned pages=4,uint64_t ttl=0):limit(budget),maxPages(pages),lifetime(ttl){}
    void Clear(){entries.clear();bytes=0;}
    void RemovePrefix(const std::wstring& prefix,const std::wstring& except=L""){
        if(prefix.empty())return;
        for(size_t i=0;i<entries.size();)if(entries[i].key.compare(0,prefix.size(),prefix)==0 && entries[i].key!=except)Remove(i);else ++i;
    }
    size_t Bytes()const{return bytes;}size_t Count()const{return entries.size();}
    bool Get(const std::wstring& key,uint64_t generation,uint64_t now,std::vector<unsigned char>& result){
        Sync(generation);for(size_t i=0;i<entries.size();i++)if(entries[i].key==key){
            if(now<entries[i].at || (lifetime && now-entries[i].at>=lifetime)){Remove(i);return false;}
            entries[i].used=++clock;result=entries[i].pixels;return true;
        }return false;
    }
    bool Put(const std::wstring& key,std::vector<unsigned char> pixels,uint64_t generation,uint64_t now){
        Sync(generation);if(key.empty() || pixels.empty() || pixels.size()>limit || !maxPages)return false;
        for(size_t i=0;i<entries.size();i++)if(entries[i].key==key){Remove(i);break;}
        while(!entries.empty() && (entries.size()>=maxPages || bytes+pixels.size()>limit)){
            size_t oldest=0;for(size_t i=1;i<entries.size();i++)if(entries[i].used<entries[oldest].used)oldest=i;Remove(oldest);
        }
        bytes+=pixels.size();entries.push_back({key,std::move(pixels),now,++clock});return true;
    }
};
}
