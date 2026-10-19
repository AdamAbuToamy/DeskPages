#pragma once
// Isolated HWND rendering. Never copy pixels from the screen/monitor DC.
// The source application's PrintWindow implementation renders into our DIB.
inline std::vector<unsigned char> CaptureWindowPixels(HWND source,int left,int top,int width,int height) {
    RECT bounds{};WinCheck(IsWindow(source) && GetWindowRect(source,&bounds),"Find capture window bounds");
    const int windowWidth=bounds.right-bounds.left,windowHeight=bounds.bottom-bounds.top;
    const int offsetX=left-bounds.left,offsetY=top-bounds.top;
    WinCheck(width>0 && height>0 && windowWidth>0 && windowHeight>0 &&
        offsetX>=0 && offsetY>=0 && offsetX+width<=windowWidth && offsetY+height<=windowHeight &&
        static_cast<uint64_t>(windowWidth)*windowHeight<=40000000,"Validate isolated window crop");
    HDC reference=GetDC(source);WinCheck(reference!=nullptr,"Window reference DC");
    HDC memory=CreateCompatibleDC(reference);
    BITMAPINFO info{};info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);info.bmiHeader.biWidth=windowWidth;
    info.bmiHeader.biHeight=-windowHeight;info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;info.bmiHeader.biCompression=BI_RGB;
    void* bits=nullptr;HBITMAP bitmap=CreateDIBSection(reference,&info,DIB_RGB_COLORS,&bits,nullptr,0);
    if(!memory || !bitmap){if(bitmap)DeleteObject(bitmap);if(memory)DeleteDC(memory);ReleaseDC(source,reference);throw std::runtime_error("Create isolated window DIB failed");}
    HGDIOBJ prior=SelectObject(memory,bitmap);std::memset(bits,0,static_cast<size_t>(windowWidth)*windowHeight*4);
    bool ok=PrintWindow(source,memory,2)!=FALSE; // PW_RENDERFULLCONTENT
    if(!ok)ok=PrintWindow(source,memory,0)!=FALSE;
    ok=(GdiFlush()!=FALSE) && ok;
    std::vector<unsigned char> pixels;
    if(ok){
        pixels.resize(static_cast<size_t>(width)*height*4);
        for(int y=0;y<height;y++)std::memcpy(pixels.data()+static_cast<size_t>(y)*width*4,
            static_cast<unsigned char*>(bits)+(static_cast<size_t>(offsetY+y)*windowWidth+offsetX)*4,static_cast<size_t>(width)*4);
        for(size_t i=3;i<pixels.size();i+=4)pixels[i]=255;
    }
    SelectObject(memory,prior);DeleteObject(bitmap);DeleteDC(memory);ReleaseDC(source,reference);
    WinCheck(ok,"Render isolated window (no screen-capture fallback)");return pixels;
}
