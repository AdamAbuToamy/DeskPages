#pragma once
#include <vector>
#include <string>
#include <cstdint>
#include <utility>
#include <memory>
#include <mutex>
#include <set>
namespace deskpages {
// Immutable shared CPU images; short metadata locks, no file I/O under lock.
class SnapshotCache {
    struct Entry {std::wstring key;std::shared_ptr<const std::vector<unsigned char>> pixels;uint64_t at,used;};
    std::vector<Entry> entries;uint64_t epoch=0,clock=0;size_t bytes=0;
    mutable std::mutex mutex;std::set<int> protectedPages;
    size_t limit;unsigned maxPages;uint64_t lifetime;
    static int Page(const std::wstring& key){try{return std::stoi(key);}catch(...){return 0;}}
    void Remove(size_t i){bytes-=entries[i].pixels->size();entries.erase(entries.begin()+i);}
    bool Sync(uint64_t value){if(value<epoch)return false;if(value!=epoch){entries.clear();bytes=0;epoch=value;}return true;}
public:
    explicit SnapshotCache(size_t budget=96ull*1024*1024,unsigned pages=4,uint64_t ttl=0):limit(budget),maxPages(pages),lifetime(ttl){}
    void Clear(){std::lock_guard<std::mutex> lock(mutex);entries.clear();bytes=0;}
    void Protect(const std::vector<int>& pages){std::lock_guard<std::mutex> lock(mutex);protectedPages=std::set<int>(pages.begin(),pages.end());}
    void RemovePrefix(const std::wstring& prefix,const std::wstring& except=L""){
        std::lock_guard<std::mutex> lock(mutex);if(prefix.empty())return;
        for(size_t i=0;i<entries.size();)if(entries[i].key.compare(0,prefix.size(),prefix)==0 && entries[i].key!=except)Remove(i);else ++i;
    }
    size_t Bytes()const{std::lock_guard<std::mutex> lock(mutex);return bytes;}size_t Count()const{std::lock_guard<std::mutex> lock(mutex);return entries.size();}
    size_t Limit()const{return limit;}
    std::shared_ptr<const std::vector<unsigned char>> GetShared(const std::wstring& key,uint64_t generation,uint64_t now){
        std::lock_guard<std::mutex> lock(mutex);if(!Sync(generation))return {};
        for(size_t i=0;i<entries.size();i++)if(entries[i].key==key){
            if(now<entries[i].at || (lifetime && now-entries[i].at>=lifetime)){Remove(i);return {};}
            entries[i].used=++clock;return entries[i].pixels;
        }return {};
    }
    bool Get(const std::wstring& key,uint64_t generation,uint64_t now,std::vector<unsigned char>& result){
        auto pixels=GetShared(key,generation,now);if(!pixels)return false;result=*pixels;return true;
    }
    bool Put(const std::wstring& key,std::vector<unsigned char> pixels,uint64_t generation,uint64_t now){
        return PutShared(key,std::make_shared<const std::vector<unsigned char>>(std::move(pixels)),generation,now);
    }
    bool PutShared(const std::wstring& key,std::shared_ptr<const std::vector<unsigned char>> pixels,uint64_t generation,uint64_t now){
        std::lock_guard<std::mutex> lock(mutex);
        if(!Sync(generation) || key.empty() || !pixels || pixels->empty() || pixels->size()>limit || !maxPages)return false;
        auto next=entries;size_t nextBytes=bytes;
        for(size_t i=0;i<next.size();i++)if(next[i].key==key){nextBytes-=next[i].pixels->size();next.erase(next.begin()+i);break;}
        while(!next.empty() && (next.size()>=maxPages || nextBytes+pixels->size()>limit)){
            size_t oldest=next.size();for(size_t i=0;i<next.size();i++)if(!protectedPages.count(Page(next[i].key)) && (oldest==next.size() || next[i].used<next[oldest].used))oldest=i;
            if(oldest==next.size())return false;
            nextBytes-=next[oldest].pixels->size();next.erase(next.begin()+oldest);
        }
        nextBytes+=pixels->size();next.push_back({key,std::move(pixels),now,++clock});entries.swap(next);bytes=nextBytes;return true;
    }
};
}
