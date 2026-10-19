#pragma once
#include "DragCapturePolicy.h"

bool IsVerifiedDragValidationLayer(HWND hit){
    HWND root=GetAncestor(hit,GA_ROOT);
    if(!root || reinterpret_cast<ULONG_PTR>(GetPropW(root,L"DeskPages.DragOwnerPid"))!=GetCurrentProcessId())return false;
    LONG_PTR style=GetWindowLongPtrW(root,GWL_EXSTYLE);BYTE alpha=0;DWORD flags=0;COLORREF key=0;
    if(!GetLayeredWindowAttributes(root,&key,&alpha,&flags))return false;
    DWORD pid=0;GetWindowThreadProcessId(root,&pid);if(!pid)return false;
    HANDLE process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,pid);if(!process)return false;
    std::vector<wchar_t> actual(32768);DWORD length=static_cast<DWORD>(actual.size());
    bool read=QueryFullProcessImageNameW(process,0,actual.data(),&length)!=FALSE;CloseHandle(process);if(!read)return false;
    HMODULE module=nullptr;
    if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&IsVerifiedDragValidationLayer),&module))return false;
    std::vector<wchar_t> buffer(32768);DWORD got=GetModuleFileNameW(module,buffer.data(),static_cast<DWORD>(buffer.size()));
    if(!got || got>=buffer.size())return false;
    std::wstring expected(buffer.data(),got);auto split=expected.find_last_of(L"\\/");if(split==std::wstring::npos)return false;expected.resize(split);
    split=expected.find_last_of(L"\\/");if(split==std::wstring::npos)return false;expected.resize(split+1);expected+=L"DeskPagesDrag.exe";
    bool image=CompareStringOrdinal(actual.data(),static_cast<int>(length),expected.c_str(),static_cast<int>(expected.size()),TRUE)==CSTR_EQUAL;
    return deskpages::AllowDragValidationLayer(true,image,(style&WS_EX_LAYERED)!=0,(flags&LWA_ALPHA)!=0,(flags&LWA_COLORKEY)!=0,alpha);
}
