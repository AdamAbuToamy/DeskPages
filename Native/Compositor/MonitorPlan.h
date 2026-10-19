#pragma once
#include <vector>
#include <cstdint>
namespace deskpages {
struct Region {int x,y,width,height;bool animate;};
inline bool ValidRegions(const std::vector<Region>& regions,int width,int height){
    if(width<=0 || height<=0 || width>16384 || height>16384 || int64_t(width)*height>40000000 || regions.empty() || regions.size()>32)return false;
    for(size_t i=0;i<regions.size();i++){
        const auto& r=regions[i];if(r.x<0 || r.y<0 || r.width<=0 || r.height<=0 || int64_t(r.x)+r.width>width || int64_t(r.y)+r.height>height)return false;
        for(size_t j=0;j<i;j++){const auto& p=regions[j];if(r.x<p.x+p.width && r.x+r.width>p.x && r.y<p.y+p.height && r.y+r.height>p.y)return false;}
    }
    return true;
}
inline float SourceOffset(const Region& r,int direction,double eased){return float(-r.x+(r.animate?direction*r.width*eased:0));}
inline float TargetOffset(const Region& r,int direction,double eased){return float(-r.x-(r.animate?direction*r.width*(1-eased):0));}
}
