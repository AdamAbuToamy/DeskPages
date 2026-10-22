# 0.8.24 taskbar groups device trial

- Own taskbar app-button presses before window metadata is ready. Hit-region refresh no longer waits for process and shortcut resolution; unmatched drops require explicit selection of a currently open window instead of silently passing the drag to Explorer.
- Ignore only DeskPages-tagged replay input, rather than every injected/promoted input event. Accept taskbar children and owned content islands. Group-source drags and modifier gestures stay native.
- Add **Add open apps…** to a group menu, with a checkbox picker for currently open windows, app names and icons. The actual selected HWNDs use the same guarded Shell assignment/recovery path.
- Keep '+' visible over application windows and fullscreen windows. Missing/auto-hidden taskbar geometry uses a persistent fallback; opening an app no longer hides it.
- Add a Copy taskbar diagnostics command that copies the last taskbar press decision locally when requested.
- Native tests exercise the actual low-level hook using externally generated press/move/release events on an owned fixture, own-replay bypass, native group-source pass-through, and actual picker assignment of a foreign window. Physical Windows 11 taskbar/touchpad acceptance remains required.
