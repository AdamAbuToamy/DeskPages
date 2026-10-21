# DeskPages 0.8.19 Alpha

- Fix edge dragging becoming unavailable for the rest of the session after an agent reply takes more than eight seconds. Keep the original request ID until acknowledgement, then safely cancel the obsolete drag without replaying navigation.
- Move minimal mouse/Escape hooks off the OLE/UI thread and recover missed mouse presses by polling. Real Explorer OLE file data and agent validation still gate all page and membership changes.
- Preserve 0.8.18 name-only pin labels, Windows Shell icons, the live memory indicator and automatic update notifications.

Windows fixtures exercise the actual drag broker with a delayed navigation reply, one acknowledgement, cancellation and a subsequent drag request. Physical Explorer/touchpad testing remains separate from these automated fixtures.
