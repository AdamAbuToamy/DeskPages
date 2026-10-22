# DeskPages 0.8.29 · Trial

- Keep the translucent + button clear of left-aligned taskbar controls, running applications and the notification area.
- Delete an individual empty or populated group from its right-click menu. Its applications return to their original taskbar identities and remain open.
- Resolve installed classic app identities using live taskbar buttons, including VS Code shortcuts and user-pinned shortcuts, without combining ambiguous active profiles.
- Taskbar groups start enabled by default, preserve an explicit off preference and use the main settings window without a second tray icon.
- Optionally assign a distinct page to each additional monitor. Main-display navigation skips connected assigned pages; disconnected pages return to navigation. Monitor assignments use persistent monitor interface identities.
- Save each displayed page independently and replay interrupted layout checkpoints. Pinned icons remain on the main display; the existing monitor arrangement fence remains active.

Validation uses Windows CI and isolated display/input fixtures. Actual Windows taskbar, VS Code and multiple physical monitors require user acceptance before publishing.
