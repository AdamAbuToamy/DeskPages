#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#define _WIN32_WINNT 0x0A00
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <d2d1_1.h>
#include <dcomp.h>
#include <dwmapi.h>
#include <vector>
#include <string>
#include <atomic>
#include <stdexcept>
#include <cstring>
#include <cwchar>
#include <cstdint>
#include <new>
#include <memory>
#include "MonitorPlan.h"
#include "TransitionMath.h"
#include "FrameGate.h"
#include "SnapshotCache.h"
#include "SnapshotDisk.h"
#include "PreviewGate.h"
#include "DragCapturePolicy.h"

// Original implementation. Uses only documented Windows graphics APIs.
// Explorer's windows, COM view, layout and redraw flags are never modified here.
// Two opaque page textures are uploaded once, then DWM animates their offsets.
namespace {
template<class T> struct Ptr {
    T* p=nullptr;
    ~Ptr(){reset();}
    Ptr()=default;Ptr(const Ptr&)=delete;Ptr& operator=(const Ptr&)=delete;
    T* operator->() const{return p;}
    operator T*() const{return p;}
    T** put(){reset();return &p;}
    void reset(){if(p){p->Release();p=nullptr;}}
};
void Check(HRESULT hr,const char* step){if(FAILED(hr))throw std::runtime_error(std::string(step)+" HRESULT="+std::to_string(static_cast<unsigned long>(hr)));}
void WinCheck(bool ok,const char* step){if(!ok)throw std::runtime_error(std::string(step)+" Win32="+std::to_string(GetLastError()));}
#include "WindowCapture.h"
#include "DragCaptureWindow.h"
enum class Op {Prepare,Begin,BeginDesktop,ConfirmTarget,Play,BeginPreview,CompletePreview,WarmSnapshot,ClearSnapshots,InvalidatePage,Abort,Shutdown};
struct Job {
    std::atomic<long> refs{2};std::atomic<bool> cancelled{false};
    HANDLE done=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    Op op=Op::Prepare;int left=0,top=0,width=0,height=0,direction=1;
    int iconCount=-1;
    std::wstring sourceKey,targetKey;
    std::vector<deskpages::Region> regions;
    std::vector<unsigned char> pixels,targetPixels;bool accepted=true,ok=false;
    ~Job(){if(done)CloseHandle(done);}
    void release(){if(refs.fetch_sub(1)==1)delete this;}
};
constexpr UINT JobMessage=WM_APP+73;
HANDLE worker=nullptr,started=nullptr;
DWORD workerId=0;
SRWLOCK lifecycle=SRWLOCK_INIT,reportLock=SRWLOCK_INIT;
std::wstring report=L"DirectComposition not initialized";
std::atomic<HWND> coverWindow{nullptr};
std::atomic<int> visible{0},playing{0},motionEnded{0};
std::atomic<unsigned int> captureSample{0};
std::atomic<uint64_t> snapshotEpoch{1},snapshotRevision{1},snapshotBytes{0},sourceHits{0},targetHits{0},snapshotMisses{0},captureCalls{0},sourceMs{0},targetMs{0},warmMs{0};
deskpages::SnapshotDisk diskSnapshots;
void Report(const std::string& value){AcquireSRWLockExclusive(&reportLock);report.assign(value.begin(),value.end());ReleaseSRWLockExclusive(&reportLock);}

class Renderer {
    HWND hwnd=nullptr,desktopWindow=nullptr;
    bool desktopMode=false;
    deskpages::SnapshotCache snapshots;
    std::wstring incomingKey;
    std::wstring FrameKey(const std::wstring& key)const {DWORD pid=0;GetWindowThreadProcessId(desktopWindow,&pid);return key.empty()?L"":key+L"|"+std::to_wstring(pid)+L"|"+std::to_wstring(left)+L","+std::to_wstring(top)+L","+std::to_wstring(width)+L","+std::to_wstring(height);}
    bool GetFrame(const std::wstring& key,std::vector<unsigned char>& pixels){bool hit=!key.empty() && snapshots.Get(FrameKey(key),snapshotEpoch.load(),GetTickCount64(),pixels);if(!hit && !key.empty()){auto epoch=snapshotEpoch.load();hit=diskSnapshots.Get(key,left,top,width,height,pixels);if(hit && epoch==snapshotEpoch.load())snapshots.Put(FrameKey(key),pixels,epoch,GetTickCount64());else if(hit)hit=false;}snapshotBytes=snapshots.Bytes();return hit;}
    void KeepFrame(const std::wstring& key,const std::vector<unsigned char>& pixels,uint64_t generation){if(!key.empty() && generation==snapshotEpoch.load()){auto frame=FrameKey(key);auto separator=key.find(L':');if(separator!=std::wstring::npos)snapshots.RemovePrefix(key.substr(0,separator+1),frame);snapshots.Put(frame,pixels,generation,GetTickCount64());diskSnapshots.Save(key,left,top,width,height,pixels);}snapshotBytes=snapshots.Bytes();}
    void LocateDesktop(){
        iconWindow=nullptr;FindIcons(FindWindowW(L"Progman",nullptr),reinterpret_cast<LPARAM>(&iconWindow));
        if(!iconWindow)EnumWindows(FindIcons,reinterpret_cast<LPARAM>(&iconWindow));
        WinCheck(IsWindow(iconWindow),"Find Explorer desktop icon view");desktopWindow=GetAncestor(iconWindow,GA_ROOT);
        wchar_t cls[64]{};GetClassNameW(desktopWindow,cls,64);
        WinCheck(std::wcscmp(cls,L"Progman")==0 || std::wcscmp(cls,L"WorkerW")==0,"Capture only Explorer desktop owner");
    }
    Ptr<ID3D11Device> d3d;
    Ptr<ID3D11DeviceContext> d3dContext;
    Ptr<IDXGIDevice> dxgi;
    Ptr<ID2D1Factory1> d2dFactory;
    Ptr<ID2D1Device> d2d;
    Ptr<ID2D1DeviceContext> dc;
    Ptr<IDCompositionDevice> composition;
    Ptr<IDCompositionTarget> target;
    Ptr<IDCompositionVisual> root,oldVisual,newVisual;
    Ptr<IDCompositionSurface> oldSurface,newSurface;
    Ptr<IDCompositionAnimation> oldAnimation,newAnimation;
    struct MonitorVisual {deskpages::Region region;Ptr<IDCompositionVisual> group,oldPage,newPage;Ptr<IDCompositionAnimation> oldMotion,newMotion;};
    std::vector<std::unique_ptr<MonitorVisual>> monitors;
    deskpages::TransitionState state;
    deskpages::PreviewGate preview;double duration=deskpages::Duration;
    int left=0,top=0,width=0,height=0;
    ULONGLONG expiry=0,hideAt=0,motionEnds=0;
    DWORD sourceExplorer=0;
    LONGLONG playStart=0;
    bool capturedTarget=false;
    ULONGLONG settleMs=0;
    HWND iconWindow=nullptr;
    int expectedIcons=-1;
    bool pixelStable=false;
    static BOOL CALLBACK FindIcons(HWND window,LPARAM data) {
        HWND view=FindWindowExW(window,nullptr,L"SHELLDLL_DefView",nullptr);
        if(!view)return TRUE;
        HWND icons=FindWindowExW(view,nullptr,L"SysListView32",nullptr);
        if(!icons)return TRUE;
        *reinterpret_cast<HWND*>(data)=icons;return FALSE;
    }
    void VerifyIconCount() {
        DWORD_PTR count=0;
        WinCheck(IsWindow(iconWindow) && SendMessageTimeoutW(iconWindow,0x1004,0,0,SMTO_ABORTIFHUNG|SMTO_BLOCK,400,&count)!=0,"Read Explorer icon count");
        if(count!=static_cast<DWORD_PTR>(expectedIcons))throw std::runtime_error("Explorer icon count changed before capture; target image blocked");
    }
    void PaintTarget() {
        if(expectedIcons<0)return; // Synthetic test has its own background window.
        VerifyIconCount();
        WinCheck(IsWindowVisible(iconWindow)!=FALSE,"Original icon view is hidden");
        WinCheck(RedrawWindow(iconWindow,nullptr,nullptr,RDW_INVALIDATE|RDW_ERASE|RDW_UPDATENOW|RDW_ALLCHILDREN)!=FALSE,"Paint verified target icons behind cover");
        Check(DwmFlush(),"Target paint presentation fence");
    }
    static LRESULT CALLBACK Procedure(HWND window,UINT message,WPARAM wp,LPARAM lp) {
        Renderer* self=reinterpret_cast<Renderer*>(GetWindowLongPtrW(window,GWLP_USERDATA));
        if(message==WM_NCCREATE){self=reinterpret_cast<Renderer*>(reinterpret_cast<CREATESTRUCTW*>(lp)->lpCreateParams);SetWindowLongPtrW(window,GWLP_USERDATA,reinterpret_cast<LONG_PTR>(self));}
        if(self){
            if(message==WM_NCHITTEST)return self->preview.active?HTCLIENT:HTTRANSPARENT;
            if(message==WM_MOUSEACTIVATE)return self->preview.active?MA_NOACTIVATEANDEAT:MA_NOACTIVATE;
            if(message==WM_ERASEBKGND)return 1;
            if(message==WM_CLOSE){self->Abort();return 0;}
            if(message==WM_TIMER){
                try{self->Tick();}catch(const std::exception& e){self->Abort();Report(e.what());}
                catch(...){self->Abort();Report("Native timer callback failed");}
                return 0;
            }
            if(message==WM_DISPLAYCHANGE || message==WM_DPICHANGED || message==WM_QUERYENDSESSION || (message==WM_SETTINGCHANGE && wp==SPI_SETDESKWALLPAPER) || message==WM_THEMECHANGED || message==WM_DWMCOLORIZATIONCOLORCHANGED || message==WM_POWERBROADCAST){
                ++snapshotEpoch;++snapshotRevision;self->ClearSnapshots();
                self->Abort();Report("Transition cancelled: display/session configuration changed");return message==WM_QUERYENDSESSION?TRUE:0;
            }
        }
        return DefWindowProcW(window,message,wp,lp);
    }
    void Upload(Ptr<IDCompositionSurface>& surface,const std::vector<unsigned char>& pixels) {
        Check(composition->CreateSurface(width,height,DXGI_FORMAT_B8G8R8A8_UNORM,DXGI_ALPHA_MODE_PREMULTIPLIED,surface.put()),"CreateSurface");
        Ptr<IDXGISurface> dxgiSurface;POINT origin{};
        Check(surface->BeginDraw(nullptr,__uuidof(IDXGISurface),reinterpret_cast<void**>(dxgiSurface.put()),&origin),"Surface.BeginDraw");
        bool began=true;
        try {
            D2D1_BITMAP_PROPERTIES1 props{};
            props.pixelFormat={DXGI_FORMAT_B8G8R8A8_UNORM,D2D1_ALPHA_MODE_PREMULTIPLIED};
            props.dpiX=props.dpiY=96;props.bitmapOptions=D2D1_BITMAP_OPTIONS_TARGET|D2D1_BITMAP_OPTIONS_CANNOT_DRAW;
            Ptr<ID2D1Bitmap1> destination;
            Check(dc->CreateBitmapFromDxgiSurface(dxgiSurface,&props,destination.put()),"CreateBitmapFromDxgiSurface");
            D2D1_BITMAP_PROPERTIES1 inputProps{};
            inputProps.pixelFormat=props.pixelFormat;inputProps.dpiX=inputProps.dpiY=96;
            Ptr<ID2D1Bitmap1> input;
            Check(dc->CreateBitmap(D2D1_SIZE_U{static_cast<UINT32>(width),static_cast<UINT32>(height)},pixels.data(),width*4,&inputProps,input.put()),"Upload bitmap");
            dc->SetTarget(destination);dc->BeginDraw();
            dc->SetTransform(D2D1::Matrix3x2F::Identity());
            const D2D1_RECT_F rect={static_cast<float>(origin.x),static_cast<float>(origin.y),static_cast<float>(origin.x+width),static_cast<float>(origin.y+height)};
            // Clip to the surface's update region; do not clear adjacent atlas content.
            dc->PushAxisAlignedClip(rect,D2D1_ANTIALIAS_MODE_ALIASED);
            dc->DrawBitmap(input,&rect,1.0f,D2D1_INTERPOLATION_MODE_NEAREST_NEIGHBOR,nullptr,nullptr);
            dc->PopAxisAlignedClip();
            HRESULT drawResult=dc->EndDraw();dc->SetTarget(nullptr);Check(drawResult,"D2D EndDraw");
            Check(surface->EndDraw(),"Surface.EndDraw");began=false;
        }catch(...){dc->SetTarget(nullptr);if(began)surface->EndDraw();throw;}
    }
    std::vector<unsigned char> Capture() {
        WinCheck(desktopMode && IsWindow(desktopWindow),"Desktop-only capture source required");
        Check(DwmFlush(),"DwmFlush before isolated desktop render");
        ++captureCalls;auto pixels=CaptureWindowPixels(desktopWindow,left,top,width,height);
        unsigned int sample=0;std::memcpy(&sample,pixels.data()+(static_cast<size_t>(height/2)*width+width/2)*4,4);captureSample=sample;
        return pixels;
    }
    void ValidateSourceRender(const std::vector<unsigned char>& pixels,bool allowOccluded=false) {
        // Compare sparse exposed DESKTOP pixels only. This does not copy a
        // screen image or sample any application content. It detects a shell
        // PrintWindow implementation that returns blank/wrong background.
        HDC screen=GetDC(nullptr);WinCheck(screen!=nullptr,"Desktop color validation DC");
        int samples=0,matches=0;HWND verifiedDragRoot=nullptr;
        for(int row=0;row<5;row++)for(int column=0;column<7;column++){
            int x=(2*column+1)*width/14,y=(2*row+1)*height/10;POINT point{left+x,top+y};
            HWND hit=WindowFromPoint(point);
            if(!hit)continue;
            HWND root=GetAncestor(hit,GA_ROOT);if(!root)continue;
            if(root!=desktopWindow && root!=verifiedDragRoot){
                if(!IsVerifiedDragValidationLayer(hit))continue;
                verifiedDragRoot=root;
            }
            COLORREF color=GetPixel(screen,point.x,point.y);if(color==CLR_INVALID)continue;
            auto i=(static_cast<size_t>(y)*width+x)*4;samples++;
            if(std::abs(int(GetRValue(color))-int(pixels[i+2]))<=16 &&
               std::abs(int(GetGValue(color))-int(pixels[i+1]))<=16 &&
               std::abs(int(GetBValue(color))-int(pixels[i]))<=16)matches++;
        }
        ReleaseDC(nullptr,screen);
        if(allowOccluded && samples<4)return;
        WinCheck(samples>=4 && matches>=4 && matches*4>=samples*3,"Isolated desktop render must match exposed desktop pixels");
    }
    void PlaceCover(UINT flags) {
        if(!desktopMode){WinCheck(SetWindowPos(hwnd,HWND_TOPMOST,left,top,width,height,flags)!=FALSE,"Position synthetic test cover");return;}
        WinCheck(IsWindow(desktopWindow),"Desktop owner must exist");
        // Insert immediately above Explorer's desktop and BELOW normal apps.
        // We do not parent/reparent or move any Explorer/application window.
        HWND after=GetWindow(desktopWindow,GW_HWNDPREV);
        if(after==hwnd)after=GetWindow(hwnd,GW_HWNDPREV);
        if(!after || (GetWindowLongPtrW(after,GWL_EXSTYLE)&WS_EX_TOPMOST))after=HWND_TOP;
        WinCheck(SetWindowPos(hwnd,after,left,top,width,height,flags)!=FALSE,"Position desktop-only transition");
        WinCheck((GetWindowLongPtrW(hwnd,GWL_EXSTYLE)&WS_EX_TOPMOST)==0,"Desktop transition must not become topmost");
    }
    void Curve(Ptr<IDCompositionAnimation>& animation,float from,float to) {
        Check(composition->CreateAnimation(animation.put()),"CreateAnimation");
        for(int i=0;i<deskpages::Segments;i++){
            auto c=deskpages::SmoothSegment(from,to,i);
            double speed=deskpages::Duration/duration;
            Check(animation->AddCubic(i*duration/deskpages::Segments,c.constant,float(c.linear*speed),float(c.quadratic*speed*speed),float(c.cubic*speed*speed*speed)),"Add easing segment");
        }
        Check(animation->End(duration,to),"Animation.End");
    }
    std::vector<unsigned char> CaptureTarget(Job* job=nullptr) {
        const ULONGLONG start=GetTickCount64();
        PaintTarget();
        auto prior=Capture();int matches=0;
        // Stability is diagnostic, not an animation veto: wallpaper can move.
        pixelStable=false;
        while(GetTickCount64()-start<180){
            if(job && job->cancelled)throw std::runtime_error("Capture cancelled");
            Sleep(25);auto next=Capture();
            matches=deskpages::SameFrame(prior,next)?matches+1:0;
            prior.swap(next);
            if(matches>=2){pixelStable=true;break;}
        }
        if(expectedIcons>=0)VerifyIconCount();
        if(expectedIcons>0 && deskpages::UniformFrame(prior))
            throw std::runtime_error("Nonempty target produced a uniform image; refusing a blank incoming page");
        settleMs=GetTickCount64()-start;return prior;
    }
public:
    ~Renderer(){Abort();if(hwnd)DestroyWindow(hwnd);}
    void Prepare() {
        BOOL valid=FALSE;
        if(composition.p && target.p && root.p && hwnd &&
            SUCCEEDED(composition->CheckDeviceState(&valid)) && valid)return;
        ClearSnapshots();++snapshotEpoch;++snapshotRevision;
        if(hwnd){DestroyWindow(hwnd);hwnd=nullptr;}
        target.reset();root.reset();composition.reset();dc.reset();d2d.reset();d2dFactory.reset();dxgi.reset();d3dContext.reset();d3d.reset();
        D3D_FEATURE_LEVEL level;
        Check(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            nullptr,0,D3D11_SDK_VERSION,d3d.put(),&level,d3dContext.put()),"D3D11 hardware device");
        Check(d3d->QueryInterface(__uuidof(IDXGIDevice),reinterpret_cast<void**>(dxgi.put())),"D3D11 to DXGI");
        Check(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED,__uuidof(ID2D1Factory1),nullptr,reinterpret_cast<void**>(d2dFactory.put())),"D2D1CreateFactory");
        Check(d2dFactory->CreateDevice(dxgi,d2d.put()),"D2D factory CreateDevice");
        Check(d2d->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE,dc.put()),"D2D device context");
        Check(DCompositionCreateDevice(dxgi,__uuidof(IDCompositionDevice),reinterpret_cast<void**>(composition.put())),"DCompositionCreateDevice");
        WNDCLASSEXW wc{};wc.cbSize=sizeof(wc);wc.hInstance=GetModuleHandleW(nullptr);wc.lpfnWndProc=Procedure;wc.lpszClassName=L"DeskPagesNativeComposition052";
        if(!RegisterClassExW(&wc) && GetLastError()!=ERROR_CLASS_ALREADY_EXISTS)WinCheck(false,"RegisterClassExW");
        hwnd=CreateWindowExW(WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW|WS_EX_TRANSPARENT|WS_EX_NOREDIRECTIONBITMAP,
            wc.lpszClassName,L"DeskPages transition",WS_POPUP,0,0,1,1,nullptr,nullptr,wc.hInstance,this);
        WinCheck(hwnd!=nullptr,"Create compositor window");coverWindow=hwnd;
        // Public OS capture-exclusion mode keeps the cover on the monitor while
        // allowing capture of the updated desktop underneath, without hiding it.
        WinCheck(SetWindowDisplayAffinity(hwnd,0x11)!=FALSE,"SetWindowDisplayAffinity(EXCLUDEFROMCAPTURE)");
        Check(composition->CreateTargetForHwnd(hwnd,TRUE,target.put()),"CreateTargetForHwnd");
        Check(composition->CreateVisual(root.put()),"Create root visual");
        Check(target->SetRoot(root),"SetRoot");Check(composition->Commit(),"Initial commit");
        Report("DirectComposition + D3D11 hardware renderer ready; capture exclusion enabled");
    }
    void Abort() {
        if(hwnd){KillTimer(hwnd,1);ShowWindow(hwnd,SW_HIDE);}
        state.Abort();preview.Reset();duration=deskpages::Duration;visible=0;playing=0;motionEnded=0;
        // Retire DWM/GPU surfaces after every transition. Optional bounded CPU snapshots live separately.
        if(root.p)root->RemoveAllVisuals();
        monitors.clear();oldVisual.reset();newVisual.reset();oldSurface.reset();newSurface.reset();oldAnimation.reset();newAnimation.reset();
        capturedTarget=false;expectedIcons=-1;iconWindow=nullptr;desktopWindow=nullptr;desktopMode=false;
        if(composition.p)composition->Commit();
    }
    void Begin(Job& job) {
        auto clock=GetTickCount64();
        Abort();Prepare();incomingKey=job.targetKey;left=job.left;top=job.top;width=job.width;height=job.height;
        desktopMode=job.op==Op::BeginDesktop || job.op==Op::BeginPreview;
        if(job.op==Op::BeginPreview){preview.Begin(snapshotRevision.load());duration=0.240;}
        SetWindowLongPtrW(hwnd,GWL_EXSTYLE,(GetWindowLongPtrW(hwnd,GWL_EXSTYLE)&~WS_EX_TRANSPARENT)|(preview.active?0:WS_EX_TRANSPARENT));
        WinCheck(SetWindowPos(hwnd,HWND_NOTOPMOST,0,0,0,0,SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE|SWP_HIDEWINDOW)!=FALSE,"Remove topmost state");
        if(desktopMode && !preview.active){
            LocateDesktop();auto generation=snapshotEpoch.load();
            bool reused=GetFrame(job.sourceKey,job.pixels);
            if(reused){try{ValidateSourceRender(job.pixels);}catch(...){ClearSnapshots();reused=false;}}
            if(reused && generation!=snapshotEpoch.load())reused=false;
            if(reused)++sourceHits;
            else {if(!job.sourceKey.empty())++snapshotMisses;generation=snapshotEpoch.load();job.pixels=Capture();}
            ValidateSourceRender(job.pixels);
            DWORD_PTR count=0;WinCheck(SendMessageTimeoutW(iconWindow,0x1004,0,0,SMTO_ABORTIFHUNG|SMTO_BLOCK,400,&count)!=0,"Read source desktop count");
            if(count>0 && deskpages::UniformFrame(job.pixels))throw std::runtime_error("Desktop window rendered blank; refusing a screen capture");
            // Only stable target/idle captures are retained; a cold source may be a moving wallpaper.
        }
        if(preview.active)LocateDesktop();
        PlaceCover(SWP_NOACTIVATE|SWP_HIDEWINDOW);
        const D2D_RECT_F clip{0,0,static_cast<float>(width),static_cast<float>(height)};
        Check(root->SetClip(clip),"Clip page");
        Upload(oldSurface,job.pixels);
        auto regions=job.regions;
        if(regions.empty())regions.push_back({0,0,width,height,true});
        WinCheck(deskpages::ValidRegions(regions,width,height),"Validate monitor animation regions");
        for(const auto& region:regions){
            std::unique_ptr<MonitorVisual> view(new MonitorVisual);view->region=region;
            Check(composition->CreateVisual(view->group.put()),"Create monitor clip");
            D2D_RECT_F local{0,0,float(region.width),float(region.height)};
            Check(view->group->SetClip(local),"Clip monitor subtree");
            Check(view->group->SetOffsetX(float(region.x)),"Place monitor X");Check(view->group->SetOffsetY(float(region.y)),"Place monitor Y");
            Check(composition->CreateVisual(view->oldPage.put()),"Create monitor source");Check(view->oldPage->SetContent(oldSurface),"Set monitor source");
            Check(view->oldPage->SetOffsetX(float(-region.x)),"Crop source X");Check(view->oldPage->SetOffsetY(float(-region.y)),"Crop source Y");
            // Clip the child to ITS source monitor before translating it; a shared
            // atlas must never slide pixels from a neighbouring monitor into view.
            D2D_RECT_F atlas{float(region.x),float(region.y),float(region.x+region.width),float(region.y+region.height)};
            Check(view->oldPage->SetClip(atlas),"Crop source monitor texture");
            Check(view->group->AddVisual(view->oldPage,TRUE,nullptr),"Add monitor source");Check(root->AddVisual(view->group,TRUE,nullptr),"Add monitor group");monitors.push_back(std::move(view));
        }
        Check(composition->Commit(),"Source commit");Check(composition->WaitForCommitCompletion(),"Source commit fence");
        if(job.cancelled){Abort();return;}
        ShowWindow(hwnd,SW_SHOWNOACTIVATE);
        Check(DwmFlush(),"Source presentation fence");
        WinCheck(IsWindowVisible(hwnd) && !IsIconic(hwnd),"Cover must be visible before desktop mutation");
        if(job.cancelled){Abort();return;}
        state.Begin(true);visible=1;playing=0;expiry=GetTickCount64()+12000;
        HWND desktop=FindWindowW(L"Progman",nullptr);GetWindowThreadProcessId(desktop,&sourceExplorer);
        WinCheck(SetTimer(hwnd,1,15,nullptr)!=0,"Start cover watchdog");
        sourceMs=GetTickCount64()-clock;
        Report(desktopMode?"Desktop HWND source ready; transition below application windows":"Synthetic source presented");
    }
    void ConfirmTarget(Job& job) {
        if((state.phase!=deskpages::Phase::Covering && !(preview.active && state.phase==deskpages::Phase::Playing)) || job.iconCount<0)
            throw std::runtime_error("A source cover and valid target count are required");
        // Explorer can recreate/reparent its desktop owner while refreshing
        // the target page or changing displays. Rebind the icon view AND its
        // verified Progman/WorkerW capture owner, not just the child HWND.
        LocateDesktop();
        expectedIcons=job.iconCount;if(preview.active)preview.Confirm(snapshotRevision.load(),incomingKey==job.targetKey);else incomingKey=job.targetKey;VerifyIconCount();
        Report("Target membership verified; original Explorer icon count="+std::to_string(expectedIcons));
    }
    void Play(Job& job) {
        if(state.phase!=deskpages::Phase::Covering)throw std::runtime_error("No presented source cover");
        if(!job.pixels.empty() && (job.width!=width || job.height!=height))throw std::runtime_error("Target texture dimensions differ from source");
        auto clock=GetTickCount64();auto generation=snapshotEpoch.load();
        capturedTarget=job.pixels.empty();settleMs=0;
        std::vector<unsigned char> pixels;
        if(preview.active){pixels=std::move(job.targetPixels);capturedTarget=false;}
        else if(capturedTarget && GetFrame(incomingKey,pixels) && generation==snapshotEpoch.load()){PaintTarget();VerifyIconCount();if(generation!=snapshotEpoch.load())pixels=CaptureTarget(&job);else ++targetHits;}
        else {if(capturedTarget && !incomingKey.empty())++snapshotMisses;pixels=capturedTarget?CaptureTarget(&job):job.pixels;if(capturedTarget)KeepFrame(incomingKey,pixels,generation);}
        Upload(newSurface,pixels);
        if(job.cancelled){Abort();return;}
        for(auto& view:monitors){
            const auto& r=view->region;
            Check(composition->CreateVisual(view->newPage.put()),"Create monitor target");Check(view->newPage->SetContent(newSurface),"Set monitor target texture");
            D2D_RECT_F atlas{float(r.x),float(r.y),float(r.x+r.width),float(r.y+r.height)};
            Check(view->newPage->SetClip(atlas),"Crop target monitor texture");Check(view->newPage->SetOffsetY(float(-r.y)),"Crop target Y");
            if(r.animate){Curve(view->oldMotion,deskpages::SourceOffset(r,job.direction,0),deskpages::SourceOffset(r,job.direction,1));Curve(view->newMotion,deskpages::TargetOffset(r,job.direction,0),deskpages::TargetOffset(r,job.direction,1));
                Check(view->oldPage->SetOffsetX(view->oldMotion),"Animate monitor source");Check(view->newPage->SetOffsetX(view->newMotion),"Animate monitor target");
            }else{Check(view->group->RemoveVisual(view->oldPage),"Retire stationary source");Check(view->newPage->SetOffsetX(float(-r.x)),"Stationary monitor target");}
            Check(view->group->AddVisual(view->newPage,FALSE,nullptr),"Add monitor target");
        }
        LARGE_INTEGER qpc;QueryPerformanceCounter(&qpc);playStart=qpc.QuadPart;
        Check(composition->Commit(),"Slide commit");Check(composition->WaitForCommitCompletion(),"Slide commit fence");
        if(job.cancelled){Abort();return;}
        if(!state.Play(true))throw std::runtime_error("Invalid transition state");
        targetMs=GetTickCount64()-clock;playing=1;motionEnds=GetTickCount64()+static_cast<ULONGLONG>(duration*1000);
        // DWM owns every motion frame. Timer only retires the finished cover.
        double hz=60;DCOMPOSITION_FRAME_STATISTICS stats{};
        if(SUCCEEDED(composition->GetFrameStatistics(&stats)) && stats.currentCompositionRate.Numerator && stats.currentCompositionRate.Denominator)
            hz=static_cast<double>(stats.currentCompositionRate.Numerator)/stats.currentCompositionRate.Denominator;
        if(hz<20 || hz>1000)hz=60;
        hideAt=GetTickCount64()+static_cast<ULONGLONG>(duration*1000+3000/hz+10);
        Report(std::to_string(int(duration*1000))+" ms quintic-eased DWM slide; verified target icons="+std::to_string(expectedIcons)+"; capture wait="+std::to_string(settleMs)+" ms; pixels stable="+(pixelStable?"yes":"no (moving background allowed)"));
    }
    bool BeginPreview(Job& job){
        Abort();Prepare();left=job.left;top=job.top;width=job.width;height=job.height;LocateDesktop();
        auto generation=snapshotEpoch.load();auto revision=snapshotRevision.load();
        // Cache-only: a miss never starts capture or changes the desktop.
        if(!GetFrame(job.sourceKey,job.pixels) || !GetFrame(job.targetKey,job.targetPixels) || generation!=snapshotEpoch.load()){++snapshotMisses;return false;}
        Begin(job);
        if(generation!=snapshotEpoch.load() || revision!=snapshotRevision.load()){Abort();return false;}
        ++sourceHits;++targetHits;Play(job);return true;
    }
    void CompletePreview(){
        WinCheck(preview.active && state.phase==deskpages::Phase::Playing && expectedIcons>=0,"Cached preview must have a verified live target");
        PaintTarget();preview.Confirm(snapshotRevision.load(),true);preview.Complete();
        if(!preview.valid){Abort();Report("Cached preview refreshed by verified live desktop");return;}
        hideAt=std::max(hideAt,GetTickCount64()+32);Tick();
    }
    void InvalidatePage(Job& job){snapshots.RemovePrefix(job.sourceKey);snapshotBytes=snapshots.Bytes();}
    void ClearSnapshots(){snapshots.Clear();snapshotBytes=0;}
    bool WarmSnapshot(Job& job){
        if(state.phase!=deskpages::Phase::Idle || job.sourceKey.empty())return false;
        bool saved=false;
        auto clock=GetTickCount64();Prepare();left=job.left;top=job.top;width=job.width;height=job.height;desktopMode=true;
        try{
            LocateDesktop();auto generation=snapshotEpoch.load();
            DWORD_PTR count=0;WinCheck(SendMessageTimeoutW(iconWindow,0x1004,0,0,SMTO_ABORTIFHUNG|SMTO_BLOCK,400,&count)!=0,"Warm icon count");
            auto pixels=Capture();ValidateSourceRender(pixels,true);
            if(count>0 && deskpages::UniformFrame(pixels))throw std::runtime_error("Warm desktop rendered blank");
            KeepFrame(job.sourceKey,pixels,generation);saved=generation==snapshotEpoch.load();
        }catch(...){Abort();throw;}
        warmMs=GetTickCount64()-clock;Abort();return saved;
    }
    void Tick() {
        if(state.phase==deskpages::Phase::Idle)return;
        if(desktopMode && GetWindow(desktopWindow,GW_HWNDPREV)!=hwnd)PlaceCover(SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE);
        DWORD explorer=0;GetWindowThreadProcessId(FindWindowW(L"Progman",nullptr),&explorer);
        if(sourceExplorer && explorer!=sourceExplorer){Abort();Report("Transition cancelled: Explorer restarted");return;}
        if(GetTickCount64()>expiry){Abort();Report("Transition cancelled: 12-second cover watchdog");return;}
        if(state.phase==deskpages::Phase::Playing && GetTickCount64()>=motionEnds)motionEnded=1;
        if(state.phase==deskpages::Phase::Playing && preview.CanRetire() && GetTickCount64()>=hideAt) {
            Check(DwmFlush(),"Final composition fence");
            // Never replace a moving target texture or cancel motion because
            // unrelated wallpaper pixels differ. Membership was checked first.
            LARGE_INTEGER end,freq;QueryPerformanceCounter(&end);QueryPerformanceFrequency(&freq);
            auto elapsed=(end.QuadPart-playStart)*1000.0/freq.QuadPart;
            auto wait=settleMs;
            auto count=expectedIcons;auto motionMs=int(duration*1000);
            Abort();Report(std::to_string(motionMs)+" ms eased slide completed; verified target icons="+std::to_string(count)+"; capture wait="+std::to_string(wait)+" ms; motion/retirement lifetime="+std::to_string(elapsed)+" ms (not FPS)");
        }
    }
};
DWORD WINAPI Worker(void*) {
    SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    HRESULT com=CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);
    MSG msg;PeekMessageW(&msg,nullptr,WM_USER,WM_USER,PM_NOREMOVE);
    SetEvent(started);
    {
        Renderer renderer;
        while(GetMessageW(&msg,nullptr,0,0)>0) {
            if(msg.message==JobMessage) {
                Job* job=reinterpret_cast<Job*>(msg.lParam);
                try {
                    if(!job->cancelled){
                        switch(job->op){
                        case Op::Prepare:renderer.Prepare();break;
                        case Op::Begin:case Op::BeginDesktop:renderer.Begin(*job);break;
                        case Op::ConfirmTarget:renderer.ConfirmTarget(*job);break;
                        case Op::Play:renderer.Play(*job);break;
                        case Op::BeginPreview:job->accepted=renderer.BeginPreview(*job);break;
                        case Op::CompletePreview:renderer.CompletePreview();break;
                        case Op::WarmSnapshot:job->accepted=renderer.WarmSnapshot(*job);break;
                        case Op::ClearSnapshots:renderer.ClearSnapshots();break;
                        case Op::InvalidatePage:renderer.InvalidatePage(*job);break;
                        case Op::Abort:renderer.Abort();break;
                        case Op::Shutdown:renderer.Abort();break;
                        }
                        job->ok=job->accepted && !job->cancelled;
                        if(job->cancelled)renderer.Abort();
                    }
                }catch(const std::exception& e){renderer.Abort();Report(e.what());}
                 catch(...){renderer.Abort();Report("Unexpected native renderer failure");}
                bool exit=job->op==Op::Shutdown;
                SetEvent(job->done);job->release();if(exit)break;
            }else{
                try {TranslateMessage(&msg);DispatchMessageW(&msg);}
                catch(const std::exception& e){renderer.Abort();Report(e.what());}
                catch(...){renderer.Abort();Report("Native window callback failed");}
            }
        }
    }
    if(SUCCEEDED(com))CoUninitialize();
    return 0;
}
bool Start() {
    AcquireSRWLockExclusive(&lifecycle);
    bool ok=true;
    if(!worker) {
        started=CreateEventW(nullptr,TRUE,FALSE,nullptr);
        if(!started)ok=false;
        else {worker=CreateThread(nullptr,0,Worker,nullptr,0,&workerId);ok=worker && WaitForSingleObject(started,3000)==WAIT_OBJECT_0;}
    }
    ReleaseSRWLockExclusive(&lifecycle);return ok;
}
bool Submit(Job* job) {
    if(!job->done || !Start() || !PostThreadMessageW(workerId,JobMessage,0,reinterpret_cast<LPARAM>(job))){job->release();job->release();return false;}
    bool done=WaitForSingleObject(job->done,5000)==WAIT_OBJECT_0;
    bool ok=done && job->ok;
    if(!done){job->cancelled=true;Report("Native compositor command timed out; transition cancelled");}
    job->release();return ok;
}
bool CopyPixels(Job& job,const unsigned char* pixels,int width,int height,int stride) {
    if(!pixels || width<=0 || height<=0 || width>16384 || height>16384 ||
        static_cast<uint64_t>(width)*height>40000000 || static_cast<int64_t>(stride)<width*4)return false;
    job.width=width;job.height=height;job.pixels.resize(static_cast<size_t>(width)*height*4);
    for(int y=0;y<height;y++)std::memcpy(job.pixels.data()+static_cast<size_t>(y)*width*4,pixels+static_cast<size_t>(y)*stride,width*4);
    for(size_t i=3;i<job.pixels.size();i+=4)job.pixels[i]=255;
    return true;
}
}
extern "C" {
__declspec(dllexport) int __cdecl DPInitialize(){auto job=new(std::nothrow)Job;if(!job)return 0;job->op=Op::Prepare;return Submit(job);}
__declspec(dllexport) int __cdecl DPConfirmTarget(int iconCount){auto job=new(std::nothrow)Job;if(!job)return 0;job->op=Op::ConfirmTarget;job->iconCount=iconCount;return Submit(job);}
__declspec(dllexport) int __cdecl DPBegin(const unsigned char* pixels,int width,int height,int stride,int left,int top){
    Job* job=nullptr;
    try{job=new Job;job->op=Op::Begin;job->left=left;job->top=top;
        if(!CopyPixels(*job,pixels,width,height,stride)){job->release();job->release();return 0;}
        return Submit(job);
    }catch(...){if(job){job->release();job->release();}return 0;}
}
__declspec(dllexport) int __cdecl DPBeginDesktop(int left,int top,int width,int height){
    auto job=new(std::nothrow)Job;if(!job)return 0;
    job->op=Op::BeginDesktop;job->left=left;job->top=top;job->width=width;job->height=height;return Submit(job);
}
__declspec(dllexport) int __cdecl DPBeginRegions(const unsigned char* pixels,int width,int height,int stride,int left,int top,const int* regions,int count){
    if(!regions || count<1 || count>32)return 0;
    Job* job=nullptr;
    try{job=new Job;job->op=Op::Begin;job->left=left;job->top=top;
        if(!CopyPixels(*job,pixels,width,height,stride)){job->release();job->release();return 0;}
        for(int i=0;i<count;i++){const int* r=regions+i*5;job->regions.push_back({r[0],r[1],r[2],r[3],r[4]!=0});}
        if(!deskpages::ValidRegions(job->regions,width,height)){job->release();job->release();return 0;}
        return Submit(job);
    }catch(...){if(job){job->release();job->release();}return 0;}
}
__declspec(dllexport) int __cdecl DPBeginDesktopRegions(int left,int top,int width,int height,const int* regions,int count){
    if(!regions || count<1 || count>32)return 0;
    Job* job=nullptr;
    try{job=new Job;job->op=Op::BeginDesktop;job->left=left;job->top=top;job->width=width;job->height=height;
        for(int i=0;i<count;i++){const int* r=regions+i*5;job->regions.push_back({r[0],r[1],r[2],r[3],r[4]!=0});}
        if(!deskpages::ValidRegions(job->regions,width,height)){job->release();job->release();return 0;}
        return Submit(job);
    }catch(...){if(job){job->release();job->release();}return 0;}
}
// Windows-only fixture: a deliberately occluded test HWND must still produce
// its own pixels. Production capture independently selects Explorer's owner.
__declspec(dllexport) int __cdecl DPProbeWindowCapture(HWND source,int left,int top,int width,int height,unsigned int* center){
    if(!center)return 0;
    try{auto pixels=CaptureWindowPixels(source,left,top,width,height);std::memcpy(center,pixels.data()+(static_cast<size_t>(height/2)*width+width/2)*4,4);return 1;}
    catch(const std::exception& ex){Report(ex.what());return 0;}
}
__declspec(dllexport) int __cdecl DPPlay(const unsigned char* pixels,int width,int height,int stride,int direction){
    Job* job=nullptr;
    try{job=new Job;job->op=Op::Play;job->direction=direction>=0?1:-1;
        if(pixels && !CopyPixels(*job,pixels,width,height,stride)){job->release();job->release();return 0;}
        return Submit(job);
    }catch(...){if(job){job->release();job->release();}return 0;}
}
__declspec(dllexport) int __cdecl DPConfigureSnapshotStore(const wchar_t* folder,const wchar_t* scope){try{if(!folder||!scope)return 0;diskSnapshots.Configure(folder,scope);return 1;}catch(...){return 0;}}
__declspec(dllexport) int __cdecl DPGetSavedSnapshotKey(int page,wchar_t* output,int count){try{if(!output||count<1)return 0;auto key=diskSnapshots.Known(page);if(key.empty()||key.size()>=size_t(count))return 0;std::memcpy(output,key.c_str(),(key.size()+1)*sizeof(wchar_t));return int(key.size());}catch(...){return 0;}}
__declspec(dllexport) void __cdecl DPInvalidateSnapshotPage(int page){
    if(page<=0)return;
    ++snapshotRevision;
    auto job=new(std::nothrow)Job;if(!job){++snapshotEpoch;++snapshotRevision;return;}
    job->op=Op::InvalidatePage;job->sourceKey=std::to_wstring(page)+L":";
    if(!job->done || !Start() || !PostThreadMessageW(workerId,JobMessage,0,reinterpret_cast<LPARAM>(job))){++snapshotEpoch;++snapshotRevision;job->release();job->release();return;}
    job->release(); // Worker owns the remaining reference.
}
__declspec(dllexport) void __cdecl DPInvalidateSnapshots(){++snapshotEpoch;++snapshotRevision;}
__declspec(dllexport) int __cdecl DPClearSnapshots(){++snapshotEpoch;++snapshotRevision;auto job=new(std::nothrow)Job;if(!job)return 0;job->op=Op::ClearSnapshots;return Submit(job);}
__declspec(dllexport) unsigned long long __cdecl DPGetSnapshotValue(int index){switch(index){case 0:return snapshotBytes.load();case 1:return sourceHits.load();case 2:return targetHits.load();case 3:return snapshotMisses.load();case 4:return captureCalls.load();case 5:return sourceMs.load();case 6:return targetMs.load();case 7:return warmMs.load();case 8:return diskSnapshots.hits.load();case 9:return diskSnapshots.writes.load();case 10:return diskSnapshots.errors.load();default:return 0;}}
__declspec(dllexport) int __cdecl DPConfirmCachedTarget(int count,const wchar_t* key){auto job=new(std::nothrow)Job;if(!job)return 0;job->op=Op::ConfirmTarget;job->iconCount=count;if(key)job->targetKey=key;return Submit(job);}
__declspec(dllexport) int __cdecl DPWarmSnapshot(const wchar_t* key,int left,int top,int width,int height){
    if(!key || std::wcslen(key)>256 || width<=0 || height<=0 || static_cast<uint64_t>(width)*height>40000000)return 0;
    auto job=new(std::nothrow)Job;if(!job)return 0;job->op=Op::WarmSnapshot;job->sourceKey=key;job->left=left;job->top=top;job->width=width;job->height=height;return Submit(job);
}
__declspec(dllexport) int __cdecl DPBeginDesktopCachedRegions(int left,int top,int width,int height,const int* regions,int count,const wchar_t* source){
    if(!regions || count<1 || count>32 || !source || std::wcslen(source)>256)return 0;
    Job* job=nullptr;try{job=new Job;job->op=Op::BeginDesktop;job->sourceKey=source;job->left=left;job->top=top;job->width=width;job->height=height;
        for(int i=0;i<count;i++){const int* r=regions+i*5;job->regions.push_back({r[0],r[1],r[2],r[3],r[4]!=0});}
        if(!deskpages::ValidRegions(job->regions,width,height)){job->release();job->release();return 0;}return Submit(job);
    }catch(...){if(job){job->release();job->release();}return 0;}
}
__declspec(dllexport) int __cdecl DPBeginCachedPreview(int left,int top,int width,int height,const int* regions,int count,const wchar_t* source,const wchar_t* target,int direction){
    if(!regions || count<1 || count>32 || !source || !target || std::wcslen(source)>256 || std::wcslen(target)>256)return 0;
    Job* job=nullptr;try{job=new Job;job->op=Op::BeginPreview;job->sourceKey=source;job->targetKey=target;job->left=left;job->top=top;job->width=width;job->height=height;job->direction=direction>=0?1:-1;
        for(int i=0;i<count;i++){const int* r=regions+i*5;job->regions.push_back({r[0],r[1],r[2],r[3],r[4]!=0});}
        if(!deskpages::ValidRegions(job->regions,width,height)){job->release();job->release();return 0;}return Submit(job);
    }catch(...){if(job){job->release();job->release();}return 0;}
}
__declspec(dllexport) int __cdecl DPCompleteCachedPreview(){auto job=new(std::nothrow)Job;if(!job)return 0;job->op=Op::CompletePreview;return Submit(job);}
__declspec(dllexport) void __cdecl DPAbort(){if(!worker)return;auto job=new(std::nothrow)Job;if(job){job->op=Op::Abort;Submit(job);}}
__declspec(dllexport) int __cdecl DPIsTransitionWindow(HWND window){return visible.load() && window!=nullptr && window==coverWindow.load();}
__declspec(dllexport) int __cdecl DPGetState(){return visible.load()|(playing.load()<<1)|(motionEnded.load()<<2);}
__declspec(dllexport) unsigned int __cdecl DPGetCaptureSample(){return captureSample.load();}
__declspec(dllexport) int __cdecl DPGetReport(wchar_t* text,int count){
    if(!text || count<=0)return 0;
    AcquireSRWLockShared(&reportLock);
    int written=static_cast<int>(std::min(report.size(),static_cast<size_t>(count-1)));
    std::memcpy(text,report.c_str(),written*sizeof(wchar_t));text[written]=0;ReleaseSRWLockShared(&reportLock);return written;
}
__declspec(dllexport) void __cdecl DPShutdown(){
    diskSnapshots.Stop();
    if(!worker)return;
    auto job=new(std::nothrow)Job;if(!job)return;job->op=Op::Shutdown;
    if(!Submit(job))return;
    if(WaitForSingleObject(worker,3000)!=WAIT_OBJECT_0)return;
    AcquireSRWLockExclusive(&lifecycle);CloseHandle(worker);CloseHandle(started);worker=nullptr;started=nullptr;workerId=0;ReleaseSRWLockExclusive(&lifecycle);
}
}
