# DeskPages 0.8.41 Alpha 1 — preserve unchanged desktop screenshots

Windows 11 x64 engineering preview addressing cold page switches after idle time, application use and session/power notifications.

## Changes

- Power, display, theme and session notifications request scene validation instead of immediately discarding resident CPU screenshots. Real geometry, DPI, wallpaper, theme, icon font/spacing and OS rendering changes still invalidate the scene.
- Re-prime missing hot images from persistent storage on the background worker while idle. Existing resident hits do not reread image files.
- Let existing disk preloads finish before scheduling an idle capture. Startup/layout verification reuses a matching stored image; explicit pixel-change events still refresh it.
- Log unchanged scene validation and retain the previous drag lifetime and performance repairs.

Snapshots remain compressed and protected for the current Windows user under LocalAppData/DeskPages/HiddenPages/Snapshots. They have no age-based expiry. The expanded scene fingerprint can require one initial recapture after upgrading. Actual disk loading still takes time after process startup; this update schedules it before navigation where possible.

## Validation

The old renderer failed a regression where an actual Windows resume notification discarded an unchanged resident snapshot. The patched renderer passed power/session/display/theme/color notification retention and validation-signal checks, GPU rebuild retention, and 24 DWM fixture transitions under CPU contention.

Cache tests cleared RAM then reloaded through the disk loader with zero captures. The native encrypted store test aged a temporary record's timestamps by 366 days, launched a fresh Windows process and verified identical restored pixels without capture; changed scene/layout/geometry and corrupted data remained rejected. This is simulated file age plus fresh-process restoration, not an actual year-long wait or physical Windows reboot.

Warm-request tests preserved 100 explicit image refreshes while 100 unchanged restored-image checks added zero captures. Native/managed builds and focused key, catalog, scene, transaction, PowerShell and package checks passed locally. Public assets require the repository's GitHub Windows regression/stress and Microsoft Defender gates.

Extended physical Chrome/YouTube/Netflix workflows, physical reboot behavior, hours-long endurance and the 50ms navigation objective remain device acceptance. This Alpha installer is not Authenticode-signed.
