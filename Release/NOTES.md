# DeskPages 0.8.18 Alpha

- Pinned icons now show only the file or program name and its actual Windows Shell icon. Shortcut titles omit .lnk/.url; full paths remain internal for correct pin/unpin persistence.
- Replace Measure resources with a live read-only RAM indicator: percent of total physical RAM and MB used by the DeskPages agent plus same-installation helpers in the current Windows session.
- Refresh the indicator every three seconds only while General settings is visible, on a background worker. Working sets can count shared pages more than once; the tooltip explains this estimate. GPU memory and disk snapshot sizes are separate from this RAM value.
- Remove the interactive benchmark launcher and scripts. Preserve page membership, pin state, cache settings and update notifications.

Automated Windows fixtures cover multilingual settings geometry, live memory reads and visibility-driven sampling, name-only shortcut labels, icons and pin persistence. Hardware touchpad/Explorer acceptance remains separate.
