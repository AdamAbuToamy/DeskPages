#pragma once
#include <cstdint>
namespace deskpages {
// A cached preview may move before Explorer is ready, but cannot retire early.
struct PreviewGate {
 bool active=false,ready=false,valid=false;uint64_t epoch=0;
 void Begin(uint64_t value){active=true;ready=false;valid=true;epoch=value;}
 void Confirm(uint64_t value,bool sameLayout){valid=valid && epoch==value && sameLayout;}
 void Complete(){ready=true;}
 bool CanRetire()const{return !active || ready;}
 bool CanRetire(uint64_t now,uint64_t motionFence)const{return CanRetire() && now>=motionFence;}
 void Reset(){active=ready=valid=false;epoch=0;}
};
}
