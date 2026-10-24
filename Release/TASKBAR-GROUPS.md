# Live taskbar groups - device-test preview

Groups are enabled by default and can be disabled in Settings. Choose **Add taskbar group** from the existing DeskPages tray menu to enable them and create a group. Windows taskbar combine should be **Always** for one collapsed group button. The helper runs with Explorer's normal user token.

1. Use **Add taskbar group** in the DeskPages tray menu, or press **Win+Alt+Plus** (main keyboard or numeric keypad) to create a numbered native taskbar group. Holding the shortcut creates only one group until the Plus key is released. Win+Plus remains Windows Magnifier.
2. Drag a running application button from the taskbar onto the group. Release on the group to commit immediately. There is no hold timer or automatic selection window.
3. Move numbered groups with the usual native taskbar drag. This gesture does not change membership.
4. Hover a populated group to see native window previews. Drag a uniquely identified member preview away from the group and release to restore its original taskbar identity, or drag it to another group.
5. Previously populated groups disappear when their last member closes/leaves. A new empty group stays until its first assignment, or can be deleted through its menu.
6. Disable **Taskbar groups** in DeskPages Settings to restore apps and stop the feature. Exiting DeskPages also requests restoration.

The normal-user helper changes only per-window Shell properties. The original AppID, relaunch command, display-name resource and icon resource are durably journaled before mutation. A separate guardian restores them if the UI process exits. Recovery checks HWND, process ID/start time and a per-window session marker; app changes to unrelated/currently non-owned property values are preserved. No application is launched/closed and no files are moved. Closing an app does not assign future windows to a group.

UI Automation resolves running buttons by exact AppID, and individual native thumbnails by an unambiguous window title. Unresolvable/ambiguous or privileged windows are rejected; the helper does not guess based on a program label. If a native taskbar button already represents several open windows, dropping that button assigns those currently resolved windows together.

There is no floating add button or extra tray icon. Only the temporary drag ghost and acceptance border are overlays during drag operations. Groups themselves use Explorer's taskbar; there is no injected Explorer DLL, replacement taskbar or programmatic pinning.

## Acceptance required on Windows 11

Test Chrome + Store and Terminal + VS Code. Verify numbered icons, reorder after grouping, hover previews, extraction from a preview, moving a member between groups, closing the final window, Escape/immediate release, mouse and touchpad, multiple Chrome windows, display/DPI changes, auto-hide, Explorer restart, disable, forced helper exit and reinstall. Check that target windows remain visible and original properties/icons return.

Windows CI fixtures verify cross-process property changes, journal-based guardian restoration, exact restoration of unset values, stale process rejection, group cleanup and immediate target acceptance. They do not certify physical Windows 11 taskbar gestures or every application's behavior.

Classic process-only AppIDs may be resolved through a read-only mapping of installed Start menu/Desktop shortcut AppIDs to running executable paths. Shortcuts are not executed or modified.

## Native taskbar dragging

App clicks and movement between ordinary taskbar buttons pass to Explorer immediately, from mouse-down through mouse-up. There is no deferred click/reorder replay. Dragging a numbered group itself also remains native.

Only entry into a numbered target transfers the final drag segment to DeskPages. The helper completes Explorer's drag at the last point before the group and shows its own target cue/ghost. Release assigns the exact live windows associated with the original dragged AppID immediately; source identity is not looked up at its old screen position. No automatic picker opens. If a source cannot be identified unambiguously, nothing is assigned and a brief message explains the failure.

Leaving the numbered target resumes Explorer while the button is held, after a fresh post-transfer hit map identifies the same source AppID. Source rectangles are never guessed from stale positions. The helper tags only its own continuation events so they cannot recursively start a new drag. Fresh hit regions are published independently from slower window/shortcut resolution. Escape cancels; an abandoned transfer has a 30-second recovery watchdog. The + button stays visible over applications and fullscreen windows.

The optional **Add open apps…** menu remains an explicitly invoked command. It never opens because a dragged app was released. Closed pinned apps are not automatically grouped. Classic Shell IDs that contain an executable path, including known-folder GUID prefixes, can be matched to the exact running executable without program-label guessing.

Windows fixtures verify ordinary live native input, transfer only on group entry, immediate release, continuation when leaving a group while held, and untouched group-source dragging on an owned fixture window. Physical Windows 11 taskbar/trackpad behavior still requires device testing.
