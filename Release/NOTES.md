# DeskPages 0.8.39 Alpha 1 — drag lifetime and input recovery

Replacement build of the existing 0.8.39 release for intermittent edge dragging that stops after idle time or switching applications while ordinary page navigation continues.

## Changes

- Do not stop the drag broker because a state snapshot is temporarily missing or malformed, or because process liveness is unknown. Exit only after verified owner death or startup failure.
- Renew low-level mouse/keyboard hooks on application changes and every 15 seconds while idle. Recover a stopped input thread on the same broker.
- Supervise the helper from the main host and relaunch it through the normal-user Explorer Shell after confirmed process death. Drain the last packet and wait for button release before recovery; retain the committed page and file/layout transactions.
- Prevent duplicate helper instances for the same session, log exit reasons, and retain small health snapshots containing input counters and state flags without file contents.

## Validation

The old 0.8.39 broker permanently exits in the new regression when a state snapshot temporarily disappears after uptime. The patched broker survives the same test.

Native Windows tests explicitly remove the mouse hook, verify input resumes after renewal, stop the input thread and verify recovery, exercise 30 application-change renewals, and kill an isolated helper process. Lifetime tests cover an hour of simulated idle time, uncertain process access, PID reuse, in-flight operations and held-button recovery fences. Existing mailbox, input and startup/relaunch tests also pass.

Physical Explorer dragging after extended use of other applications remains device acceptance. This Alpha installer is not Authenticode-signed. The replacement assets use the existing GitHub build, regression/stress and Microsoft Defender scan gates.
