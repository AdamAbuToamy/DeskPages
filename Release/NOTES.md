# DeskPages 0.8.37 trial

The taskbar + button follows native taskbar exposure and slide geometry. It hides when the bar is auto-hidden, unavailable or covered by a fullscreen app, and reappears with the taskbar. No synthetic visible-bar fallback is used while the real bar is hidden. Native owned-window occlusion/reappearance and auto-hide fixtures cover the visibility gate.

Cached navigation reads immutable resident RAM images only. Startup and neighbouring pages preload from the protected disk store on a separate worker. Idle screenshots, DPAPI, compression, decompression and persistence no longer hold navigation behind disk locks. Current/next/previous pages receive eviction protection within a RAM budget of 1/32 physical memory (128-512 MiB cap); images have no timed expiry. Persistence backlog is separately bounded and reported.

Ordinary battery/power-status notifications and graphics-device recreation no longer discard unchanged CPU snapshots. Content fingerprints invalidate only affected pages; duplicate file notifications preserve images. Desktop file metadata is collected asynchronously and compared with persisted image keys after restart. Genuine display/theme/session changes retain scene invalidation.

Input and rendering threads use AboveNormal CPU priority and HighQoS; navigation dispatch retains its bounded priority scope. The compression writer uses Windows background scheduling. Stable navigation uses the native display revision fence; periodic geometry checks remain enabled. Background maintenance is deferred after navigation input. Diagnostic writes are asynchronous; transition-performance-v5.csv adds resident budget/count, preloads, captures, worker errors and persistence deferrals.

Adds blocked-disk/capture concurrency, pinned-neighbour memory-pressure, battery-event, GPU rebuild, stale-generation, asynchronous per-page fingerprint and stable-monitor fast-path tests. Existing graphics, workspace, recovery, input, groups, installer and Defender gates remain enabled. Physical Windows 11 touchpad/mixed-GPU latency requires user acceptance. Trial only; do not publish before approval.
