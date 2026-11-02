# DeskPages 0.8.38 Alpha 1 — taskbar group startup and removal fixes

This release builds on 0.8.37 and includes the latest tested taskbar-group fixes. It is an engineering pre-release for Windows 11 x64.

## What's fixed

- **Create the first group immediately:** Win+Alt+Plus is registered by DeskPages' main host at startup. The main keyboard Plus/Equal (with or without Shift) and the numeric keypad Plus are supported. You do not need to open the tray menu first.
- **Lower idle memory for Groups:** Groups stays enabled by default, but the separate controller and its recovery helper no longer start merely because DeskPages starts. They launch when a group is first requested; desktop snapshot caching is unchanged.
- **Delete without the extra blank app:** Before removing a numbered group tab from the taskbar, DeskPages hides its anchor window and removes the APPWINDOW classification without recreating its HWND. This prevents Explorer from briefly discovering it as an ordinary program window. Group member ownership is restored before the anchor is removed.
- **Safer teardown:** Group removal and failed creation both hide the original anchor before calling the native taskbar removal API.

## Validation and scope

GitHub's Windows build checks shortcut registration, group-window lifecycle, native taskbar adapter operations, installation and workspace stress tests. The release pipeline also requires a Microsoft Defender on-demand scan with fresh signatures and unchanged SHA-256. Passing these checks does not replace visual verification on a physical Windows 11 taskbar.

This Alpha installer is **not Authenticode-signed** and Windows SmartScreen may display an unknown-publisher warning. DeskPages does not need administrator access for ordinary usage.

## Existing functionality retained

DeskPages maintains native Windows desktop pages, resident screenshot caching, saved page assignments and navigation gestures/keyboard controls. This update does not replace the desktop directory or intentionally modify the user's files.
