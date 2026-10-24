// Compile as a separate EXE, never as the distributed compositor DLL.
#define DESKPAGES_GRAPHICS_FIXTURE
#include "Compositor.cpp"
#include <commctrl.h>
#include <iostream>
#include <thread>

namespace {
struct GraphicsFixture {
 static void Pump(){MSG m;while(PeekMessageW(&m,nullptr,0,0,PM_REMOVE)){TranslateMessage(&m);DispatchMessageW(&m);}}
 static void Run(){
  INITCOMMONCONTROLSEX controls{sizeof(controls),ICC_LISTVIEW_CLASSES};InitCommonControlsEx(&controls);
  // All windows belong to this fixture. No Explorer visibility, membership,
  // registry, files, positions, or shell refresh operations are changed.
  HWND backdrop=CreateWindowExW(0,L"STATIC",L"DeskPages isolated graphics fixture",WS_POPUP|WS_VISIBLE,30,30,480,260,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
  WinCheck(backdrop!=nullptr,"Fixture backdrop");
  HWND list=CreateWindowExW(0,WC_LISTVIEWW,L"",WS_CHILD|WS_VISIBLE,0,0,1,1,backdrop,nullptr,GetModuleHandleW(nullptr),nullptr);
  WinCheck(list!=nullptr,"Fixture empty icon list");
  std::atomic<bool> running{true};
  std::thread load([&]{while(running){for(volatile int i=0;i<40000;i++){}Sleep(1);}});
  int transitions=0,samples=0;
  try{
   Renderer renderer;renderer.Prepare();
   const auto handles=GetGuiResources(GetCurrentProcess(),GR_GDIOBJECTS);
   for(int repeat=0;repeat<2;repeat++)for(int direction:{-1,1})for(int delay:{0,100,650})for(bool changed:{false,true}){
    Job begin;begin.op=Op::Begin;begin.left=30;begin.top=30;begin.width=480;begin.height=260;
    begin.pixels.resize(480*260*4);
    for(size_t i=0;i<begin.pixels.size();i+=4){begin.pixels[i]=130;begin.pixels[i+1]=50;begin.pixels[i+2]=20;begin.pixels[i+3]=255;}
    renderer.Begin(begin);
    // Use the production cached Play/CompletePreview/Tick path with an owned
    // zero-item list view instead of Explorer's desktop view.
    renderer.preview.Begin(snapshotRevision.load());renderer.iconWindow=list;renderer.expectedIcons=0;
    Job play;play.op=Op::Play;play.direction=direction;play.targetPixels=begin.pixels;
    for(size_t i=0;i<play.targetPixels.size();i+=4){play.targetPixels[i]=20;play.targetPixels[i+1]=110;play.targetPixels[i+2]=170;}
    renderer.Play(play);
    auto start=GetTickCount64();bool complete=false;int targetSamples=0;
    while(visible && GetTickCount64()-start<2400){
     auto elapsed=GetTickCount64()-start;
     if(!complete && elapsed>=static_cast<ULONGLONG>(delay)){
      if(changed)renderer.preview.Confirm(snapshotRevision.load()+1,false);
      renderer.CompletePreview();complete=true;
      WinCheck(visible!=0 || elapsed>=380,"Cached handoff cut the animation before its motion fence");
     }
     Pump();renderer.Tick();
     if(elapsed<360)WinCheck(visible!=0,"Animation retired before curve completed");
     if(!complete)WinCheck(visible!=0,"Slow desktop exposed before confirmation");
     if(visible){
      HDC screen=GetDC(nullptr);COLORREF c=GetPixel(screen,270,160);ReleaseDC(nullptr,screen);
      bool old=GetRValue(c)==20 && GetGValue(c)==50 && GetBValue(c)==130;
      bool next=GetRValue(c)==170 && GetGValue(c)==110 && GetBValue(c)==20;
      WinCheck(old||next,"Synthetic cover contained blank or unrelated window pixels");
      if(next)targetSamples++;samples++;
     }
     Sleep(8);
    }
    WinCheck(!visible && complete && targetSamples>0,"Cached animation failed to complete/present target");transitions++;
   }
   renderer.Abort();Pump();
   WinCheck(GetGuiResources(GetCurrentProcess(),GR_GDIOBJECTS)<=handles+8,"Repeated transitions leaked GDI objects");
  }catch(...){running=false;load.join();DestroyWindow(backdrop);throw;}
  running=false;load.join();DestroyWindow(backdrop);
  std::cout<<"PASS: production D3D11/DWM cached handoff under CPU contention; "<<transitions<<" transitions, "<<samples<<" actual synthetic-window pixel samples; both directions, immediate/delayed/slow confirmation, key changes and GDI bound. Not a physical touchpad/display certification.\n";
 }
};
}
int main(){
 SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
 auto hr=CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);
 try{GraphicsFixture::Run();if(SUCCEEDED(hr))CoUninitialize();return 0;}
 catch(const std::exception& ex){std::cerr<<ex.what()<<"\n";if(SUCCEEDED(hr))CoUninitialize();return 1;}
}
