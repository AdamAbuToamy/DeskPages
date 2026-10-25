#pragma once
#include <cstdint>
namespace deskpages {
inline bool ScenePowerEvent(uintptr_t event){return event==4 || event==6 || event==7 || event==0x12;}
}
