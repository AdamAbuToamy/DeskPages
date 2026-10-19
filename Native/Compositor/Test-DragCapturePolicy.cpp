#include "DragCapturePolicy.h"
#include <cassert>
#include <cstdio>
int main(){
 using deskpages::AllowDragValidationLayer;
 assert(AllowDragValidationLayer(true,true,true,true,false,4));
 assert(AllowDragValidationLayer(true,true,true,true,false,8));
 assert(!AllowDragValidationLayer(false,true,true,true,false,4));
 assert(!AllowDragValidationLayer(true,false,true,true,false,4));
 assert(!AllowDragValidationLayer(true,true,false,true,false,4));
 assert(!AllowDragValidationLayer(true,true,true,false,false,4));
 assert(!AllowDragValidationLayer(true,true,true,true,true,4));
 assert(!AllowDragValidationLayer(true,true,true,true,false,0));
 assert(!AllowDragValidationLayer(true,true,true,true,false,9));
 assert(!AllowDragValidationLayer(true,true,true,true,false,255));
 std::puts("PASS: verified transparent broker may validate desktop pixels; wrong owner/image, nonlayered, colorkey, no constant alpha and opaque overlays rejected.");
}
