#pragma once
#include <vector>
#include <cstddef>
#include <cstdlib>
namespace deskpages {
inline bool UniformFrame(const std::vector<unsigned char>& pixels) {
    if(pixels.size()<4)return true;
    for(std::size_t i=4;i<pixels.size();i++)
        if(i%4!=3 && std::abs(int(pixels[i])-int(pixels[i%4]))>2)return false;
    return true;
}
// Inspect every pixel: isolated icons must not be lost by a sparse sample grid.
inline bool SameFrame(const std::vector<unsigned char>& a,const std::vector<unsigned char>& b) {
    if(a.empty() || a.size()!=b.size())return false;
    for(std::size_t i=0;i<a.size();i++)
        if(i%4!=3 && std::abs(int(a[i])-int(b[i]))>2)return false;
    return true;
}
}
