# DeskPages 0.8.33 · Trial

- Removes pages assigned to additional monitors. Existing assignments are retired automatically, all active pages return to primary navigation, and the settings control is removed. Legacy assignment metadata is backed up; canonical page layouts, current page, original Desktop checkpoint, pin membership and layout history stay intact. Old imports/history and APIs cannot re-enable assignments.
- Normal page transitions notify Explorer about changed file attributes instead of refreshing the entire Desktop. A full refresh remains available for changed system-icon registry state or a stale/incomplete view. Target membership, stable view and saved positions remain verified before handoff.
- The native cover rebinds to Explorer's current Desktop owner. A temporarily missing replacement icon view keeps the cover alive until the new view exists, within the existing watchdog, instead of exposing the refresh gap.
- Retains primary-only capture/cache independence from secondary hotplug, original Desktop restoration on exit, icon-arrangement protection, pinned icons, taskbar groups, updates and installer security/inventory checks.

Validation includes upgrade retirement, byte-preserved layouts, retired API rejection, incremental/fallback view updates and covered animation transaction sequencing. Fixtures do not certify physical Windows monitor/touchpad rendering. Trial only; do not publish before user acceptance.
