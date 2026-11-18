# DeskPages 0.8.42 Alpha 1 — verified recovery from suspended navigation

Windows 11 x64 engineering preview addressing navigation that remains blocked after a failed display/layout recovery.

## Fix

When navigation is requested after transient protective suspension, DeskPages rereads the committed manifest, validates workspace state where supported, reconnects Explorer, repairs pending workspace metadata and restores the committed page. It clears suspension only after layout verification succeeds and the display revision remains unchanged.

Failed icon positioning, another display change, an unreadable manifest or an explicit show-all recovery marker keep navigation protected. Recovery does not save the partially painted live layout or overwrite committed metadata. If recovery fails, the dialog now includes the actual restoration error instead of only saying switching is blocked.

## Validation

Isolated temporary-data tests passed for rejected positions, display revision changes, successful recovery, unchanged manifest hashes/current page, explicit recovery mode and corrupt manifests. Existing show-all recovery, repeated/mixed page navigation, PowerShell 5.1 parser and package integrity checks also passed locally.

Actual recovery on the affected user's desktop has not yet been verified. Persistent Windows/Explorer positioning failures still require resolving their underlying cause; this change does not bypass the safety checks. Public assets require the repository's GitHub Windows regression/stress and Microsoft Defender scan gates.

The previous screenshot persistence and drag lifetime fixes remain included. This Alpha installer is not Authenticode-signed.
