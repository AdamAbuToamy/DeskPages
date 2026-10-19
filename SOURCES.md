# Implementation references

The HID adapter and gesture logic are implemented for DeskPages. Win32 interop
and descriptor parsing were informed by the following MIT-licensed sample;
no dependency on a compiled third-party binary is included.

- emoacht/RawInput.Touchpad, reviewed commit 38a9e68c1c42cb19bae02fe84ee88b2a4b2a8639:
  https://github.com/emoacht/RawInput.Touchpad/tree/38a9e68c1c42cb19bae02fe84ee88b2a4b2a8639
  Retained MIT notice: ThirdParty/RawInput.Touchpad-LICENSE.txt.
- Microsoft Raw Input model, including background INPUTSINK and HID collections:
  https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input
- Precision Touchpad collection, contact IDs, coordinates, Tip, Confidence, scan time and count:
  https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/touchpad-windows-precision-touchpad-collection
- HIDP_VALUE_CAPS layout:
  https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/ns-hidpi-_hidp_value_caps
- Folder redirection changes the known folder location (not included in this build):
  https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shsetknownfolderpath

Differences from the reference sample: .NET Framework/C#5 compatibility;
background registration; cached preparsed descriptors; independent per-device
state; processing every report in RAWHID.dwCount; explicit RAWINPUT bounds;
hybrid-frame assembly; normalized coordinates; Tip/Confidence checks; scoped
swipe recognition. The native HID parser itself has not run in this Linux build environment.

# Native compositor (0.5.0)

Native/Compositor/Compositor.cpp is an original implementation using documented Windows APIs.
It does not embed rusty-desktop-icons, Pickets, DexCorral, Lively or Fences code.
Research and source-review references, product comparisons, design tradeoffs and pinned
reviewed revisions are in RESEARCH-AR.md. The renderer uses full-page captured textures,
not the reference Rust library's per-icon redraw approach.

Build on Windows from the Visual Studio x64 Native Tools prompt with
Native/Compositor/Build-Windows.cmd, or cross-compile using MinGW-w64 and link
d3d11, dxgi, d2d1, dwmapi, dcomp, gdi32, user32, ole32 and uuid.

# 0.5.1 easing and capture gate
- https://learn.microsoft.com/en-us/windows/win32/api/dcompanimation/nf-dcompanimation-idcompositionanimation-addcubic
- https://github.com/microsoft/Windows-classic-samples/blob/main/Samples/DirectCompositionLayeredChildWindow/cpp/DirectComposition_LayeredChildWindow.cpp
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos
- https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute

Easing coefficients and the frame gate are original code. No source from desktop
replacement projects was incorporated.

# 0.5.2 target paint / count fence
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-redrawwindow
- https://learn.microsoft.com/en-us/windows/win32/controls/lvm-getitemcount

A paint fence and count checks are used before capturing the target, not as proof
that a screen recording or a physical display has no dropped frames.

# 0.6.0 checkpoint / session / startup guardian
- https://learn.microsoft.com/en-us/windows/win32/shutdown/wm-queryendsession
- https://learn.microsoft.com/en-us/windows/win32/shutdown/wm-endsession
- https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setprocessshutdownparameters
- https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shgetsetsettings
- https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/ns-shlobj_core-shellstatea
- https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-settingchange

The new lifecycle, gate, guardian, and tests are original DeskPages code.
The referenced Shell API may change in later Windows versions; device acceptance
is required. No Explorer injection, Winlogon shell replacement or service is installed.

## 0.6.4 recovery grid
IFolderView::GetSpacing gives icon dimensions including surrounding space, used for collision-free show-all layout planning:
https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-getspacing

## 0.6.5 isolated desktop rendering
- PrintWindow (the window owner renders into the supplied DC; synchronous):
  https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow
- SetWindowPos (Z-order insertion and nonactivation):
  https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos

Original implementation: WindowCapture.h renders only the Explorer desktop HWND; source and target never use a full-screen image. Sparse exposed-desktop color checks reject invalid source renders. Overlay placement is below application windows. Unsupported shell rendering disables animation without a screen-capture fallback. This build does not implement Windows.Graphics.Capture. Windows acceptance fixture is included but was not run in Linux.

## 0.6.6 new-item positioning
- Microsoft IFolderView::SelectAndPositionItems:
  https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-selectandpositionitems
- Microsoft IFolderView::GetAutoArrange:
  https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-getautoarrange
- Microsoft supported desktop positioning guidance:
  https://devblogs.microsoft.com/oldnewthing/20211122-00/?p=105948
New-item discovery/eligibility/near-click policy and transaction integration are original DeskPages code.

## 0.6.7 identity across inline rename
- https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfileinformationbyhandle
- https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-getfocuseditem
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawmouse
The creation ticket is retained by volume/file index, not by filename or selection. APIs are not a substitute for physical Windows acceptance.

## 0.6.8 creation placement cancellation
Original input-only drag policy replaces inference from icon coordinate changes.
Read-only Explorer diagnostics:
https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-getcurrentviewmode
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-iswindowenabled
No menu/arrange preference repair is claimed.

## 0.6.9 creation and visible-view synchronization
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getparent
- https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-folderflags
- https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview2-setcurrentfolderflags
- https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview2-getcurrentfolderflags
The shared desktop already supports custom positioning. Partial IFolderView2 ABI was checked against MinGW-w64 shobjidl.h (base 14 methods, then 7 methods before SetCurrentFolderFlags). Live NoIcons mismatch is a conditional hypothesis until device flags confirm it. No undocumented registry deletion or native menu patching is used.

# 0.8.7 drag interaction

Original implementation: separate normal-user OLE target, temporary desktop-only surfaces, strict data-only IPC, selected Desktop path/file identity checks and metadata-only drop transaction. No Explorer injection or replacement. Final OLE Drop returns DROPEFFECT_NONE: Explorer must not perform a filesystem MOVE. The agent changes page membership only. Actual source drag continuity through Explorer refresh and real elevation/launch must be tested on Windows.

- https://learn.microsoft.com/en-us/windows/win32/api/oleidl/nf-oleidl-idroptarget-drop
- https://learn.microsoft.com/en-us/windows/win32/shell/dataobject
- https://learn.microsoft.com/en-us/dotnet/framework/winforms/advanced/walkthrough-performing-a-drag-and-drop-operation-in-windows-forms
- https://devblogs.microsoft.com/oldnewthing/20131118-00/?p=2643

# 0.8.7 native drag image and capture validation

The previous custom OLE target did not forward drag image events; use the original IDataObject with the Windows Shell IDropTargetHelper. No fake file image or independent drag loop is generated. The transparent canvas also covered all exposed-desktop sample points in the renderer, so source capture validation could reject animation. Validation now accepts this exact installed broker only after checking owner PID property, executable path and bounded constant alpha. It still compares pixels and captures the original Explorer HWND, never a foreground app screenshot.

- https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-idroptargethelper
- https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-idroptargethelper-dragenter
- https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.dataobject?view=netframework-4.8.1


0.8.7 integration references:
- https://learn.microsoft.com/en-us/windows/win32/shell/shellfolderview-selectitem — selecting/revealing an existing item with flags 1|4|8|16.
- https://learn.microsoft.com/en-us/windows/win32/gdi/wm-displaychange — display change notification.
No competitor source code was imported. Workspace transaction/UI/rule/profile code is original project code.

## Simplified Windows-style settings, 0.8.7

- Microsoft app settings guidance: https://learn.microsoft.com/windows/apps/design/app-settings/guidelines-for-app-settings — settings rows, toggles and secondary options behind an expander.
- Microsoft DWM attributes: https://learn.microsoft.com/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute — documented Windows 11 corner preference; optional guarded call only.
- Direct user feedback: https://forums.stardock.com/530013/fences-constantly-move-position-all-icons-bunch-up — complaints about broken icon positioning and preference for reliability over added features.
- Direct feature requests: https://forums.stardock.com/539986/feature-request-command-line-cli-support-for-saving-restoring-fences-layouts — requests for simple restore and display-layout recovery.

These are selected examples, not a representative market survey. They motivate focusing on arrangement recovery and minimal interaction. They do not establish that DeskPages satisfies all monitor/hardware configurations.

0.8.7 display composition: Microsoft DirectComposition clipping contracts,
https://learn.microsoft.com/en-us/windows/win32/directcomp/clipping
and IDCompositionVisual::SetClip. MonitorPlan.h and the per-monitor atlas
subtrees are original DeskPages implementation, not copied from another utility.
Publication uses documented GitHub CLI `gh release create --prerelease
--verify-tag --notes-file`: https://cli.github.com/manual/gh_release_create
