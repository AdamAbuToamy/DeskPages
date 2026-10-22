# 0.8.21 taskbar groups device trial

- Fix native numbered group anchors: unowned APPWINDOW, minimized, identity set before first show; remove the tool-window/no-activation styles that suppressed the taskbar button.
- White translucent add button with per-pixel alpha and a spring-like press/release motion. Cancel releases outside the button.
- Roll back failed group creation so it leaves no orphan model or anchor.
- Windows regression covers native anchor eligibility, minimized visibility, Shell registration, translucent pixels and pressed surface. Physical Windows 11 button appearance and dragging still require device acceptance.

# DeskPages 0.8.20 Alpha - taskbar groups experiment

- Add optional live-window taskbar groups from the DeskPages tray menu. Only the add button is placed near Widgets/weather; numbered groups use native Explorer taskbar grouping and can be reordered like other taskbar buttons.
- Drag a resolvable running app button onto a numbered group, dwell for 1.2 seconds until the green cue, then release. Native hover previews show member windows. Drag an individually identified member thumbnail outside the group to restore its original Shell identity.
- Remove a previously populated group after its last window closes or leaves; keep newly created empty groups available for the first drop. Closed pinned apps are excluded.
- Restore original window properties on disable/exit, maintain a local recovery journal and an independent normal-user guardian, and include both helper processes in the RAM indicator.

This is a device-test build. Native Windows 11 taskbar accessibility IDs, numbered icons, drag behavior, Chrome and Store compatibility require testing on the target Windows 11 build. Windows fixtures cover model rules and cross-process Shell property/guardian restoration, not physical taskbar interaction. Native collapse/hover depends on Windows taskbar combine settings. When the '+' position is occupied, it moves just above that position to avoid covering an app button.
