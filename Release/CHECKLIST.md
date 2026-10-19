# Public release checklist

0.8.7 is a preview, not a claim of complete Windows certification.

Implemented (not a substitute for Windows acceptance): installation folder choice and protected directory policy, compact panel, privacy/license links, author metadata, safe empty-page deletion, transaction journal recovery, saved layouts, main-page logon behavior, metadata export, package identity validation, signing build path and release verification gate. See VERIFICATION.txt for checks actually performed on this artifact.

Before calling a version stable, record PASS evidence on a real Windows 11 machine for:

- Fresh install, upgrade from 0.6.10, first animation immediately after install, chosen protected paths, long paths and non-Latin account names.
- Create/rename/select/drag/delete items at different desktop points, Auto arrange/Align menu availability, per-page layouts after switching, 3+ pages and failed nonempty-page deletion.
- Edge drag: white cue remains through page preparation and entire animation, dismisses on matching completion, does not reappear at latched edge or enter captures; test animation on/off, cancel, IO delay, and release while transition is active; confirm no 0.7.6-style Drop callback stall, original grab offset and native resulting positions after release, white cue and 48px approach/320ms dwell, native Move text expected; native Shell drag image visible through strip/canvas handoff and animation; positive low-alpha capture validation and foreign-window rejection; actual Explorer OLE drag ghost stays attached through page refresh/animation; left/right dwell, rearm for 3+ pages, one/many files, sparse and rectangular selections, grabbed icon not first in file-list order, exact relative spacing after switching away/back, collision shifts entire group, insufficient group space refuses without repacking, drop point with Align to grid both on and off, negative monitor coordinates, occupied target cells, actual saved positions after switching away/back, Escape, release over another app, no filesystem move/copy/delete and original paths/contents intact. Check broker runs unelevated, main agent elevated, clean quit/upgrade/uninstall and stale session cleanup. Failure/cancel must not change membership. Run Test-DragMailbox.ps1 on Windows; verify sustained dragging while packets are read/replaced, retry after a short exclusive lock, and a delayed acknowledgement never causing duplicate navigation.
- Page-number HUD after successful switches only, rapid repeated navigation, fade/retirement, no stolen focus, click-through, no HUD in source/target captures, optional setting persists.
- Panel at 100/125/150/200% scaling, small screen, keyboard navigation and long/Arabic page names; no clipped installer text.
- Supported touchpad swipe, direction toggle, Win arrows over desktop only, no hijacking app shortcuts, and animation beneath open windows. Video review for dropped frames/old-page flashes.
- Sign-out/sign-in, restart with different pages active, interrupted transition, agent crash, Explorer restart and power-loss recovery in a VM. Verify actual file hashes, membership and coordinates against the pre-test baseline, not just file existence.
- Metadata export ZIP content and privacy, full disk and denied write handling, recovery after failed startup.
- Uninstall from a non-main page, crowded main desktop, interrupted uninstall, return of attributes/system icons and layout; recovery data remains usable.
- Multiple monitors and resolution/DPI changes if advertised; OneDrive Desktop and Public Desktop privilege handling if advertised. Do not advertise untested configurations.

External publication steps still required:

1. Create the official GitHub repository under Adam's account, push the reviewed source and tag an immutable release commit. Enable private vulnerability reporting and Issues; update documentation with the actual support URL.
2. Obtain an eligible trusted signing identity, or apply for Foundation signing if choosing that route. No certificate is included in this package.
3. Build and sign from the tagged source on Windows. Run `Release/Verify-Release.ps1` and retain its result and hashes. Test the downloaded final signed installer in a clean Windows VM. Hashes are integrity checks, not substitutes for signing.
4. Publish the installer as a GitHub Release asset, source tag, SHA256, changelog, known limitations and privacy/license links. Mark preview until the hardware/Windows acceptance matrix passes. Do not upload local user data, logs, settings, credentials or signing keys.

Do not claim BIOS/pre-sign-in operation. Explorer runs in the signed-in user session. The startup guardian can reduce the visible gap, but is not a boot driver or guaranteed pre-Explorer execution.

## 0.8.7 Alpha packaging

- [x] Per-monitor animation, optional primary scope and external-edge policy.
- [x] Portable simulator + scope fixtures and opt-in native graphics simulator.
- [x] Modern simple UI docs, privacy and license notices reviewed for this scope.
- [x] Windows CI build/stress workflow and gated Pre-release publication script.
- [x] Installer/source/checksum generation and secret/runtime-state preflight.
- [ ] Actual GitHub Windows CI run on the public repository (not yet dispatched).
- [ ] Trusted Authenticode signature (the Alpha is explicitly unsigned).
- [ ] Physical two-screen/GPU, real reboot and affected-device crash acceptance.

No checkbox for simulated tests is evidence that physical-device tests ran.
