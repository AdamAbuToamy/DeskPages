# DeskPages 0.8.8 — Navigation feedback preview

A focused visual update to the 0.8.7 Alpha:

- Immediate page-number acknowledgement before source layout verification and capture.
- A subtle moving progress line on an independent, event-driven STA UI thread. Hidden feedback has no polling timer.
- Feedback remains until the page transaction succeeds AND the native transition retires, then holds 300 ms and fades over 200 ms.
- Rejected transitions show a short error cue. Stale completion signals cannot hide newer feedback.
- A wider 18 logical-pixel white edge cue (previously 3 physical pixels), with a bright core and soft blue-white fill. Drag hit areas and dwell timing are unchanged.

The capture/compositor, page transaction engine, file membership, visibility rules, drag IPC and drop placement algorithms are unchanged. Startup/install version metadata is 0.8.8.0.

This is an unsigned preview. Portable regression tests and Mono/Xvfb UI rendering passed; actual Windows focus/capture/OLE and upgrade/reboot acceptance still require Windows. Do not interpret a simulated UI test as a physical touchpad, GPU or reboot certification.

## Previous release context

DeskPages 0.8.7-alpha.1 — first engineering preview

DeskPages adds icon pages to the original Windows Explorer desktop. Files keep
their original paths. Created by Adam Abu Toamy; independent of Microsoft.

Included:
- Compact page switcher and a separate Windows 11-inspired settings window.
- Per-monitor clipped DirectComposition transitions, including extended displays.
- Optional Settings > More options > Main display only: switch the primary
  display while secondary icons stay visible at their saved positions.
- Edge dragging, page-number feedback, selected-icon pinning and manual layout
  save/restore. Internal monitor boundaries are left free for normal dragging.
- Main page at sign-in, nonempty-page deletion guard and independent Rescue.

Download the EXE installer. The source ZIP is optional for developers. Verify
SHA256SUMS.txt after download. The installer is unsigned: the author metadata
is not a trusted code-signing identity and Windows may show publisher/reputation
warnings. Do not disable Windows protections to install it.

Target: Windows 11 x64, .NET Framework 4.8, Explorer desktop with hidden items not
shown. Windows 10, native ARM64, cloud placeholders/reparse points and third-party
shell replacements are not supported. Physical multi-monitor/mixed-GPU/driver
compatibility remains experimental. Extremely large desktop atlases (over 40M
pixels or 16,384 pixels on either axis), GPU resets and unavailable isolated
capture safely fall back to nonanimated switching. Touchpad support depends on
the driver. Windows virtual desktops still share the same physical Desktop.

Evidence: monitor math/pixel simulator, display-scope fixtures, real temporary
file stress/crash recovery tests, and isolated UI checks. These do not certify
all hardware, native compositor frame pacing, physical unplug/replug or reboots.
A prior native UI access-violation report requires retesting on affected devices;
it is not represented as universally fixed. See VERIFICATION.txt and
Release/ACCEPTANCE.md for precise evidence and limitations.

Organization recovery restores metadata, not deleted file contents. Uninstall
through Windows Settings; use DeskPagesRescue.exe if the agent fails to open.
Review/redact local logs before reporting issues. See LICENSE.txt, PRIVACY.txt,
THIRD-PARTY-NOTICES.md and SECURITY.md. This is an Alpha, not a stable release.
