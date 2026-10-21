# Durable snapshot experiment — 0.8.16

Use Settings → Appearance → Page capture → Cached · experimental. First visits to previously uncaptured pages require an ordinary capture. Reuse is not limited by a timer. RAM retains up to four images / 96 MiB; valid images evicted from RAM are restored from protected local files. The files persist across normal shutdown and restart, with scene/layout/monitor validation. Shared changes may legitimately invalidate images.

## Local data and protection

Folder: `%LOCALAPPDATA%\DeskPages\HiddenPages\Snapshots`, the writable program-data area for the account. One `.dpc` per page, losslessly compressed using Windows XPRESS Huffman and protected with current-user Windows DPAPI. No ordinary PNG/JPG is written. The directory restricts inherited access to the current account and System. Files and the directory are held without delete sharing, and image files without read sharing, while the app runs. Hidden/System/ReadOnly attributes add protection against accidental deletion. The owner/administrator can deliberately remove files, and code running as the same account can use DPAPI; absolute anti-delete or same-user secrecy is not claimed.

Compression/encryption/writing runs on a separate bounded worker. Updates of the same page coalesce; up to four raw images can wait plus one active write. Temporary compression/encryption buffers and file loading add memory beyond the retained-RAM cap. The cap is not total process RAM. The most recent complete file is atomically replaced; partial/tampered ciphertext is rejected. On normal shutdown queued writes drain before locks close. A crash may lose a not-yet-committed update, in which case verified live capture repairs it.

## Changes and responsiveness

There is no 15-second capture loop or 30-second expiry. Layout checks follow structural icon events and mouse release. After release, the next 40ms UI tick verifies actual layout and captures only when the saved key/scene differs. The image becomes available in RAM immediately; local persistence follows on the writer. Real source/target icon and layout checks still run behind a 240ms cached preview. A cached preview can finish visually before Explorer becomes interactive; these are separate metrics.

Touchpad input previously rejected contacts while the cover was visible, including a following gesture. The cover is now recognized by its actual owned HWND and accepts fresh gesture sequences. One contact sequence still emits once. The legacy wheel route is suppressed only by recent two-finger or recognized HID movement, not arbitrary one-finger traffic for one second. Recognition and queue delay remain measurable device behavior, not certified instant response.

## Validation

`Tests/Test-SnapshotStoreWindows.ps1` runs the included native Windows fixture: encrypted/compressed byte round trip, exclusive open/delete locks, reopening/persistence, changed layout/scene/bounds rejection and corrupted-ciphertext rejection. This fixture needs Windows 11 and was cross-compiled, not executed, on the Linux build host.

Local portable tests cover recognition latches, rapid new contact IDs without lift reports, inertia, repeated wheel gestures, RAM LRU/expiry policy, layout identity and preview lifecycle. Actual touchpad hardware, Explorer/DWM, DPAPI/filesystem locks and installation remain physical Windows acceptance.

Use Settings → General → Measure resources for equal direct/cached phases. `transition-performance-v3.csv` includes first-slide and desktop-ready latency, capture calls during navigation and cumulative disk hits/writes/errors. Compare after long idle periods, repeated touchpad use, RAM eviction and application restart. Any GitHub claim must include raw reports and machine/display/page/icon counts; no performance claim is published with this preview.
