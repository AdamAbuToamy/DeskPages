# 0.8.27 icon arrangement preservation trial

Preserves valid hand-arranged icon positions during page restoration and display changes, including close spacing and shifted desktop origins. Only out-of-bounds icons are fitted after reserving all existing positions. Verifies repaired coordinates before saving.

An animation failure after a successful page commit now ends the cover without refreshing/reapplying the desktop or showing a transition error. Existing layout history is retained; previous arrangements are not guessed or automatically overwritten.

Includes the 0.8.26 native taskbar dragging changes. Windows CI uses simulated Explorer adapters for layout assertions; physical monitor and touchpad validation remains a device test.

# 0.8.26 native taskbar dragging and primary display trial

- Page icons stay on the primary display after connecting, disconnecting or changing displays. Existing installations migrate to Main display only once; the setting can still be changed explicitly.
- Display profiles are revalidated against the primary work area, including shifted Explorer coordinates, mixed sizes and a changed primary display. A full primary display does not spill onto another display.
- Displaced icons no longer become shared icons across every page. Permanent user pins and file membership remain intact.
- Before saving, delayed Explorer spillover is repaired using the last saved positions. New-file placement and pin/membership layout planning also stay within the primary display.
- Ordinary taskbar clicks and app reordering remain native from press through release. Only entering a numbered group transfers the drag; leaving it resumes native movement while held.
- Dropping on a group assigns immediately, with no long green-light wait and no automatic picker. Resolve the original source AppID after native movement; exact executable-path Shell identities are supported.
- Keep + visible over applications. The open-app picker remains an explicitly invoked menu command.
- Windows 11 physical taskbar/trackpad gestures and physical monitor hotplug still need device testing.

Trial build only; no public release.
