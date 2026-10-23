# DeskPages 0.8.31 · Trial

- Primary-only transitions capture and animate only the primary working area. Stationary secondary displays stay live; disconnecting or reconnecting them no longer changes primary snapshot dimensions or the display scope.
- Primary page keys exclude fixed secondary-page icons. Saved canonical checkpoints and live target views use the same primary key, including when Page 2 returns to main navigation after its monitor disconnects.
- Native snapshot validity takes priority over remembered script keys. A fixed page edited on another screen cannot reuse an older main-display image after unplugging.
- Actual primary geometry, DPI, wallpaper, membership and Desktop file changes continue to invalidate stale images. All-display mode retains topology-dependent frames.

Includes the unreleased 0.8.30 original Desktop exit restoration and fixed-page arrangement protection. Validation adds 50 secondary disconnect/reconnect cycles with production PowerShell functions and native bounds/scope selector fixtures. These use simulated geometry, not physical monitor hotplug. User acceptance is required before public release.
