# DeskPages 0.8.30 · Trial

- Exit restores an original Desktop checkpoint before considering page-local positions. Older installations recover earliest retained positions from layout history and safety backups. Original free-form spacing and connected monitor positions take priority; newer files use remaining space. Page snapshots stay intact.
- Fixed secondary pages remain protected after primary-screen edits. Automatic Explorer reflow is repaired; a secondary layout is saved only after a matching deliberate edit on that display. Consumed edit gestures cannot authorize later automatic rearrangements.
- Navigation commands run before periodic monitor repair and background snapshot preparation. Monitor geometry and membership are reused during each layout operation, lightweight icon copies replace repeated serialization, and unchanged composed checkpoints skip copying and writing.
- Restoring a page positions only icons whose coordinates changed. Keyed files never inherit another file's position because their display labels match.
- Cached previews reuse the verified monitor scope and cached layout metadata. Main-display pin copies do not invalidate all images; actual stationary-page changes still invalidate the composed scene.

Validation includes production-function fixtures and Windows build/standard/stress suites. Real Windows 11 monitor behavior and navigation latency require user acceptance before publication.
