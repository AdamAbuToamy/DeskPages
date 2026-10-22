# 0.8.23 taskbar groups device trial

- Fix the ObjectDisposedException / ContextMenuStrip failure observed in the device recording. Reuse a popup owned by the stable add-control window; never dispose it in its Closed event. Defer group deletion and tray shutdown until the current click unwinds.
- Place and render the add button at startup using native taskbar bounds, before UI Automation finishes. Keep a safe above-bar fallback while accessibility is unavailable, and redraw after hide/resize/show.
- Isolate disappearing or unavailable UIA controls instead of dropping all taskbar geometry.
- Add native regressions for 40 real menu open/close cycles, deferred deletion, disposal of the group anchor, popup reuse and add-button reshow. Existing input ownership, recovery and stress checks remain required.
- The recording proves the menu crash. Physical Windows 11 taskbar app assignment still requires device acceptance after this fix; automated fixtures do not certify it.
