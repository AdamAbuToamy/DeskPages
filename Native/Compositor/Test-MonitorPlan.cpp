#include "TransitionMath.h"
#include "MonitorPlan.h"
#include <cassert>
#include <iostream>
#include <random>
int main(){
    using namespace deskpages;long checks=0;
    std::vector<std::vector<Region>> cases={{{0,0,1920,1040,true}},{{1920,0,1920,1040,true},{0,0,1920,1040,true}},{{0,1080,1920,1040,true},{1920,0,2560,1400,true}},{{0,1400,1920,1040,true},{0,0,2560,1400,true}},{{0,0,3840,2080,true},{3840,200,1920,1040,true}},{{0,0,1280,2000,true},{1280,100,1920,1040,true}}};
    std::mt19937 rng(87019);
    for(int i=0;i<1000;i++){int a=640+rng()%3200,b=640+rng()%3200,h=480+rng()%1600;cases.push_back({{0,0,a,h,true},{a,0,b,h,true}});}
    for(auto regions:cases)for(bool primaryOnly:{false,true})for(int direction:{-1,1}){
        if(primaryOnly)for(size_t j=1;j<regions.size();j++)regions[j].animate=false;
        int w=0,h=0;for(auto r:regions){w=std::max(w,r.x+r.width);h=std::max(h,r.y+r.height);}assert(ValidRegions(regions,w,h));checks++;
        for(auto r:regions)for(int frame=0;frame<=120;frame++){
            double ease=Ease(frame/120.0);double old=SourceOffset(r,direction,ease),incoming=TargetOffset(r,direction,ease);
            for(int sample=0;sample<19;sample++){
                double local=(sample+.5)*r.width/19;
                // Sample the exact atlas crops and offsets used by the native tree.
                double src=local-old,dst=local-incoming;
                bool from=src>=r.x && src<r.x+r.width,to=dst>=r.x && dst<r.x+r.width;
                if(r.animate){assert(from!=to || std::abs(src-r.x)<.002 || std::abs(dst-r.x)<.002);checks++;}
                else{assert(to && std::abs(incoming+r.x)<.001);checks++;}
                if(from){assert(src>=r.x-.001 && src<=r.x+r.width+.001);checks++;}
                if(to){assert(dst>=r.x-.001 && dst<=r.x+r.width+.001);checks++;}
            }
            if(frame==120){assert(std::abs(incoming+r.x)<.001);checks++;}
        }
    }
    assert(!ValidRegions({{0,0,100,100,true},{99,0,100,100,true}},199,100));
    assert(!ValidRegions({{-1,0,100,100,true}},100,100));assert(!ValidRegions({{0,0,20000,100,true}},20000,100));
    std::cout<<"PASS: monitor compositor simulator; "<<cases.size()<<" layouts; both directions, Primary-only/all, 121 frames, 19 samples per region; "<<checks<<" assertions. Native DWM/GPU not simulated.\n";
}
