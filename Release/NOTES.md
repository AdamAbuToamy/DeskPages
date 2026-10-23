# DeskPages 0.8.35 trial

Navigation commands wake the host through a posted message and are dispatched before idle capture, routine layout repair and resource polling. Active transactions remain protected against reentry; display-change commands retain FIFO ordering and page changes retain their monitor and layout guards. A short AboveNormal priority scope affects only the dispatch thread during navigation and restores its original priority.

Taskbar groups coalesce window lifecycle events, preserve the 80 ms active drag hit-region refresh, and avoid fast polling for desktop mouse presses. Idle UI Automation refresh uses a 2-second fallback. Open-window identity enumeration runs only during taskbar interaction when events/identities change or a 10-second fallback is due. Left-aligned +, individual deletion, VS Code resolution and integrated settings are retained.

transition-performance-v4.csv records input queue wait, input-to-slide submission and input-to-verified-desktop times. These are software timing measurements, not guaranteed physical display frame times.

The 0.8.28 visibility, layout transaction, encrypted cache and native compositor implementations remain unchanged. Physical Windows 11 latency and multi-window behavior require user acceptance. Trial only; no publication before approval.
