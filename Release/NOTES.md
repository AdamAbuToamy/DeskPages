# DeskPages 0.8.37 Alpha 2 — input and drag reliability update

The floating taskbar + button has been removed. Create a numbered group from the existing DeskPages tray menu (Add taskbar group) or Win+Alt+Plus, using the main keyboard or numeric keypad. Win+Plus retains its Windows Magnifier function. Group creation uses a hidden message window, with no second tray icon or visible add overlay.

Cached navigation reads immutable resident RAM images only. Startup and neighbouring pages preload from the protected disk store on a separate worker. Idle screenshots, DPAPI, compression, decompression and persistence no longer hold navigation behind disk locks. Current/next/previous pages receive eviction protection within a RAM budget of 1/32 physical memory (128-512 MiB cap); images have no timed expiry. Persistence backlog is separately bounded and reported.

Ordinary battery/power-status notifications and graphics-device recreation no longer discard unchanged CPU snapshots. Content fingerprints invalidate only affected pages; duplicate file notifications preserve images. Desktop file metadata is collected asynchronously and compared with persisted image keys after restart. Genuine display/theme/session changes retain scene invalidation.

Input and rendering threads use AboveNormal CPU priority and HighQoS; navigation dispatch retains its bounded priority scope. The compression writer uses Windows background scheduling. Stable navigation uses the native display revision fence; periodic geometry checks remain enabled. Background maintenance is deferred after navigation input. Diagnostic writes are asynchronous; transition-performance-v5.csv adds resident budget/count, preloads, captures, worker errors and persistence deferrals.

Adds blocked-disk/capture concurrency, pinned-neighbour memory-pressure, battery-event, GPU rebuild, stale-generation, asynchronous per-page fingerprint and stable-monitor fast-path tests. Existing graphics, workspace, recovery, input, groups, installer and Defender gates remain enabled. Physical Windows 11 touchpad/mixed-GPU latency requires user acceptance. Alpha pre-release; device-specific behavior is not guaranteed.


## Alpha 2 changes

- Track physical left/right Windows key events and recognize desktop pointer scope when handling Win+Left/Right. Ctrl+Alt+Left/Right remains an alternative. If the Windows key is disabled by the keyboard or system even when DeskPages is closed, this update cannot restore it.
- Improve drag broker cancellation cleanup and log candidate/drag-enter failures to investigate repeat drag attempts. A recoverable unexpected error no longer ends the broker message loop. Physical repeated drags still require acceptance testing.
- No change to touchpad handling or stored desktop file contents and page layouts.
