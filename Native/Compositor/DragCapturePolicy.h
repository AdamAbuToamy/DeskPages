#pragma once
namespace deskpages {
// Sparse pixel validation may look through only this exact, bounded-opacity
// in-package broker. This never changes the actual Explorer-only capture source.
inline bool AllowDragValidationLayer(bool ownerMatches,bool exactBrokerImage,bool layered,bool constantAlpha,bool colorKey,unsigned int alpha){
    return ownerMatches && exactBrokerImage && layered && constantAlpha && !colorKey && alpha>0 && alpha<=8;
}
}
