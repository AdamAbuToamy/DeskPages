# DeskPages 0.8.39 Alpha 1 — edge drag recovery

This update fixes two cases that can prevent files from being dragged between desktop pages. It is an engineering pre-release for Windows 11 x64.

## Fixes

- Restore the exact acknowledgement when a completed drag operation's response is missing or stale. The operation is never executed again, preventing duplicate page switches or file membership changes.
- Preserve the next drag's original mouse press while the preceding drag is finishing. A released press is discarded, and each deferred press is consumed once.

## Validation

The missing-response regression reproduces on the previous mailbox code and passes with this fix. Drag mailbox, input, recovery, placement, image, startup and drop-transaction tests passed locally, along with workspace, visibility, file integrity and recovery checks.

The local Windows taskbar group shortcut-registration fixture could not register its global shortcuts. Its production logic is unchanged. Publication uses the existing GitHub Windows build, regression/stress tests and Microsoft Defender scan gates; the local experimental installer is not used as the release asset.

Physical Explorer dragging, touchpad input and monitor behavior still require device acceptance. This Alpha installer is not Authenticode-signed.

Page membership, file paths and contents, drop placement, layout rollback, settings and taskbar group behavior retain their existing logic.
