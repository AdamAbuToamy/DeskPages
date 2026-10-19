# DeskPages

[DeskPages website and download](https://adamabutoamy.github.io/DeskPages/) · [All releases](https://github.com/AdamAbuToamy/DeskPages/releases)

<!-- deskpages-demo:start -->
## Preview

https://github.com/user-attachments/assets/1604baac-4e1b-4fa8-92d0-7a5c943df32d

*A short concept visualization of DeskPages.*

**[Download DeskPages for Windows 11 x64 - Alpha](https://github.com/AdamAbuToamy/DeskPages/releases)**
<!-- deskpages-demo:end -->


Small icon pages for the original Windows desktop. Created by Adam Abu Toamy.
Version 0.8.8 Alpha.

This Alpha is unsigned. Windows may display an unknown-publisher or SmartScreen warning.

Run the installer, then click the tray icon to see your pages. Double-click a
page or choose Open. Add a page with + Page. Right-click a page to rename it,
lock its icon positions or delete an empty page. Previously archived pages can
be restored from that same context menu.

Settings is a separate compact window, also available from the tray menu. It
uses Windows 11-inspired neutral surfaces, Segoe UI, setting rows and toggles.
Its four basic switches cover touchpad gestures, edge dragging, animation and
page-number feedback. Changes save automatically. Keyboard/reverse/reduced
motion, new-file destination and Main display only are under More options.

Useful desktop features:

- Drag physical desktop files to a screen edge to switch pages and place them.
- Select desktop shortcuts, then use the tray's Selected desktop icons menu to
  show them on every page or return them to their assigned page.
- Save a known-good arrangement in Settings, then explicitly restore it later.
  It includes page organization and icon positions, not file contents. Newer
  files remain managed on Main. Restoring does not roll back current app
  interaction/language preferences. Saving again replaces the saved point.
- Files retain their original paths. Page deletion still refuses nonempty pages.

Search, rules, operation history and diagnostic dashboards are absent from the
daily interface. Existing organization metadata is preserved during upgrades.

Extended displays use per-monitor clipped slides. Main display only keeps icons
on other screens visible at their current positions while the primary page changes.
Move an icon onto the main screen to make it page-specific again. On disconnection,
icons belonging to the current page are fitted onto the remaining display; the
saved display profile is retained for reconnection. Internal screen boundaries
are not page-drag targets. Use external edges or keyboard navigation.

Settings changes validate and atomically write preferences without running a
Desktop visibility/layout transaction. Display-scope changes use a verified
workspace transaction because they change what is visible. The saved manual arrangement is separate
from rolling operation history.

Run DeskPagesRescue.exe if the agent is unavailable. Uninstall through Windows
Settings; do not manually delete local page metadata while icons are hidden.
The app starts at sign-in on Main. It does not run before Windows or in BIOS.

No account, telemetry or cloud upload. See PRIVACY.txt, LICENSE.txt and
THIRD-PARTY-NOTICES.md. The installer is unsigned unless independently verified;
the author name is not a trusted digital signature.

This preview has isolated logic/file/crash tests and Mono/Xvfb UI evidence.
Actual Windows installation, reboot, touchpad, Explorer/OLE, monitor/DPI and
frame-pacing acceptance are not certified here. See Release/ACCEPTANCE.md.

Automated tests: `Validate.cmd`. A real Windows DWM simulator can test two virtual
regions on one physical screen: `Simulate-Monitors.cmd`. No display-driver changes
or virtual-display software is required. CI/release instructions: [BUILD](Release/BUILD.md).
Known limitations and Alpha notes: [release notes](Release/NOTES.md).

<!-- deskpages-feedback-088 -->

### Navigation feedback

DeskPages acknowledges page navigation immediately with a translucent
page indicator that stays visible until the transition completes.
Edge dragging uses a wider illuminated cue.
