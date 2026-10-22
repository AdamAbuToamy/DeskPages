# Live taskbar groups - device-test preview

Enable **Taskbar groups - experimental** from the DeskPages tray menu. Windows taskbar combine should be **Always** for one collapsed group button. The helper runs with Explorer's normal user token.

1. Click **+** near Widgets/weather to create a numbered native taskbar group.
2. Drag a running application button from the taskbar onto the group. Hold for 1.2 seconds; a green border/check confirms readiness. Release to commit.
3. Move numbered groups with the usual native taskbar drag. This gesture does not change membership.
4. Hover a populated group to see native window previews. Drag a uniquely identified member preview away from the group and release to restore its original taskbar identity, or drag it to another group.
5. Previously populated groups disappear when their last member closes/leaves. A new empty group stays until its first assignment, or can be deleted through its menu.
6. Choose **Restore apps and turn off** in the groups tray menu to stop the feature. Exiting DeskPages also requests restoration.

The normal-user helper changes only per-window Shell properties. The original AppID, relaunch command, display-name resource and icon resource are durably journaled before mutation. A separate guardian restores them if the UI process exits. Recovery checks HWND, process ID/start time and a per-window session marker; app changes to unrelated/currently non-owned property values are preserved. No application is launched/closed and no files are moved. Closing an app does not assign future windows to a group.

UI Automation resolves running buttons by exact AppID, and individual native thumbnails by an unambiguous window title. Unresolvable/ambiguous or privileged windows are rejected; the helper does not guess based on a program label. If a native taskbar button already represents several open windows, dropping that button assigns those currently resolved windows together.

Only the add button is an overlay. When its near-weather rectangle is occupied by application buttons, it moves just above that rectangle. Auto-hidden taskbars and full-screen windows hide the add button. Groups themselves use Explorer's taskbar; there is no injected Explorer DLL, replacement taskbar or programmatic pinning.

## Acceptance required on Windows 11

Test Chrome + Store and Terminal + VS Code. Verify numbered icons, reorder after grouping, hover previews, extraction from a preview, moving a member between groups, closing the final window, Escape/short dwell, mouse and touchpad, multiple Chrome windows, display/DPI changes, auto-hide, Explorer restart, disable, forced helper exit and reinstall. Check that target windows remain visible and original properties/icons return.

Windows CI fixtures verify cross-process property changes, journal-based guardian restoration, exact restoration of unset values, stale process rejection, group cleanup and dwell rules. They do not certify physical Windows 11 taskbar gestures or every application's behavior.
