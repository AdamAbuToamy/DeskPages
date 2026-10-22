# 0.8.22 taskbar groups device trial

- Empty numbered groups reject restore commands before their client window appears; clicking opens only the group menu.
- Take ownership of a running-app button press before Explorer starts rearranging buttons. The drag uses an icon ghost and stationary native group targets, with the existing 1.2-second green confirmation.
- Numbered-group drags pass through to native taskbar reordering. Ordinary app clicks and drops between other app buttons replay the completed native gesture after release. Escape/early release over a group cancels safely.
- Input hooks run on a separate message thread, use pre-resolved live-window hit regions and expire stale regions; injected input, modifier clicks, closed pins and unrelated windows pass through. Regions refresh after native taskbar gestures.
- Windows validation adds restore rejection, hook startup/shutdown and ownership/cancellation regressions. Physical Windows 11 taskbar behavior, native app replay and touchpad input remain device acceptance.
